using System.Globalization;
using System.Text.Json;
using Banking.Application.Abstractions;
using Banking.Application.Errors;
using Banking.Application.Supervision;
using Banking.Domain.Accounts;
using Banking.Domain.Institution;
using Banking.Domain.Lookups;
using Banking.Domain.Security;
using Banking.Domain.Supervision;
using Banking.Domain.Transactions;
using Banking.Infrastructure.Parties;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Banking.Infrastructure.Transactions;

/// <summary>Who is posting, and in which working branch.</summary>
public sealed record PostingActor(long StaffUserId, int BranchId);

/// <summary>
/// Everything the engine needs to post or re-post a movement (plan B 5.1). It is stored as the request's payload, so
/// an approval runs exactly what was requested. <see cref="LedgerLines"/> are the lines other than the product control
/// accounts, which the engine adds for every <see cref="AccountLines"/> entry.
/// </summary>
public sealed record PostingDraft(
    string TransactionCode,
    string? LimitCode,
    decimal Amount,
    string? Narration,
    IReadOnlyList<AccountLine> AccountLines,
    IReadOnlyList<LedgerLine> LedgerLines,
    bool CheckSignerKyc,
    IReadOnlyDictionary<string, string?>? Details = null,
    IReadOnlyList<LedgerFloor>? Floors = null,
    long? ClosesTillSessionId = null);

/// <summary>
/// A cash ledger account (till or vault) whose balance in one branch must not go below zero after the posting
/// (plan B 6.4). The account row is locked while it is checked, so two postings cannot both spend the same cash.
/// </summary>
public sealed record LedgerFloor(long LedgerAccountId, int BranchId, string ProblemCode);

/// <summary>What happened to a submission: posted at once, or waiting for approval.</summary>
public sealed record PostingOutcome(long PostingRequestId, string Reference, string Status, long? PostedTransactionId);

public sealed record PostingRequestView(
    long PostingRequestId,
    string Reference,
    int BranchId,
    DateOnly WorkingDate,
    string TransactionCode,
    decimal Amount,
    string Status,
    PostingDraft Request,
    long RequestedByUserId,
    string? RequestedByName,
    DateTime RequestedOn,
    long? SupervisedByUserId,
    DateTime? SupervisedOn,
    string? DecisionReason,
    long? PostedTransactionId);

/// <summary>
/// B1: the only writer of ledger rows and customer balances (plan B 5.1, 5.2). One database transaction per call:
/// lock every balance row involved in ascending account order, re-read, check (5.3), add the control-account lines,
/// check every branch balances, then either post or hold the amounts in a pending request. Approval repeats the checks
/// on the stored request, because the accounts may have changed while it waited.
/// </summary>
public sealed class PostingEngine
{
    public const int ListLimit = 200;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly BankingDbContext _db;
    private readonly IWorkingDateProvider _workingDates;
    private readonly TransactionLimitResolver _limits;
    private readonly PermissionResolver _permissions;
    private readonly KycService _kyc;
    private readonly IActivityContext _activity;
    private readonly TimeProvider _timeProvider;
    private readonly SupervisionOptions _supervision;

    public PostingEngine(
        BankingDbContext db,
        IWorkingDateProvider workingDates,
        TransactionLimitResolver limits,
        PermissionResolver permissions,
        KycService kyc,
        IActivityContext activity,
        TimeProvider timeProvider,
        IOptions<SupervisionOptions> supervision)
    {
        ArgumentNullException.ThrowIfNull(supervision);
        _db = db;
        _workingDates = workingDates;
        _limits = limits;
        _permissions = permissions;
        _kyc = kyc;
        _activity = activity;
        _timeProvider = timeProvider;
        _supervision = supervision.Value;
    }

    // ---------------------------------------------------------------- Submit

    public async Task<OperationResult<PostingOutcome>> SubmitAsync(PostingActor actor, PostingDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(draft);

        var invalid = Validate(draft);
        if (invalid.Count > 0)
        {
            return OperationResult.Fail<PostingOutcome>(ProblemCodes.Validation, invalid);
        }

        var codeId = await CodeIdAsync(draft.TransactionCode, cancellationToken).ConfigureAwait(false);
        var workingDate = await _workingDates.GetAsync(actor.BranchId, cancellationToken).ConfigureAwait(false);

        var transaction = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var accounts = await LockAsync(draft, cancellationToken).ConfigureAwait(false);
            if (accounts is null)
            {
                _db.ChangeTracker.Clear();
                return OperationResult.Fail<PostingOutcome>(ProblemCodes.NotFound);
            }

            var refused = await CheckAsync(actor.BranchId, draft, accounts, cancellationToken).ConfigureAwait(false);
            if (refused is not null)
            {
                _db.ChangeTracker.Clear();
                return Fail(refused);
            }

            var ledger = LedgerLines(draft, accounts);
            if (ledger.Problem is not null)
            {
                _db.ChangeTracker.Clear();
                return Fail(ledger.Problem);
            }

            var needsApproval = await NeedsApprovalAsync(actor, draft, accounts, cancellationToken).ConfigureAwait(false);
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var request = new PostingRequest
            {
                Reference = await NextReferenceAsync(actor.BranchId, workingDate, cancellationToken).ConfigureAwait(false),
                BranchId = actor.BranchId,
                WorkingDate = workingDate,
                TransactionCodeId = codeId,
                Amount = draft.Amount,
                PayloadJson = JsonSerializer.Serialize(draft, Json),
                Status = needsApproval ? PostingRequestStatuses.Pending : PostingRequestStatuses.Posted,
                RequestedByUserId = actor.StaffUserId,
                RequestedOn = now
            };
            _db.Set<PostingRequest>().Add(request);

            long? postedId = null;
            if (needsApproval)
            {
                foreach (var line in draft.AccountLines)
                {
                    PostingRules.Hold(accounts[line.AccountId].Balance!, line.CrDb, line.Amount);
                }

                _activity.Record(EventCodes.PostingRequest, nameof(PostingRequest), request.Reference, Details(draft));
                await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                postedId = await PostAsync(request, draft, codeId, workingDate, ledger.Lines!, accounts, actor.StaffUserId, null, now, cancellationToken)
                    .ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok(new PostingOutcome(request.PostingRequestId, request.Reference, request.Status, postedId));
        }
    }

    // ---------------------------------------------------------------- Decisions

    /// <summary>
    /// Four-eyes approval (plan B 5.2): a different person with supervise on the posting's sub-module, working in the
    /// request's branch, on the same business day. The checks run again; if they now fail nothing changes and the
    /// request stays pending (it can still be rejected).
    /// </summary>
    public async Task<OperationResult<PostingOutcome>> ApproveAsync(PostingActor actor, long postingRequestId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var transaction = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var (request, draft, problem) = await DecisionTargetAsync(actor, postingRequestId, cancellationToken).ConfigureAwait(false);
            if (problem is not null)
            {
                return OperationResult.Fail<PostingOutcome>(problem);
            }

            if (request!.RequestedByUserId == actor.StaffUserId && !_supervision.AllowSelfApproval)
            {
                return OperationResult.Fail<PostingOutcome>(SupervisionProblems.SelfApproval);
            }

            var workingDate = await _workingDates.GetAsync(request.BranchId, cancellationToken).ConfigureAwait(false);
            if (workingDate != request.WorkingDate)
            {
                return OperationResult.Fail<PostingOutcome>(ProblemCodes.PostingExpired);
            }

            var accounts = await LockAsync(draft!, cancellationToken).ConfigureAwait(false);
            if (accounts is null)
            {
                return OperationResult.Fail<PostingOutcome>(ProblemCodes.NotFound);
            }

            foreach (var line in draft!.AccountLines)
            {
                PostingRules.Release(accounts[line.AccountId].Balance!, line.CrDb, line.Amount);
            }

            var refused = await CheckAsync(request.BranchId, draft, accounts, cancellationToken).ConfigureAwait(false);
            var ledger = refused is null ? LedgerLines(draft, accounts) : default;
            if (refused is not null || ledger.Problem is not null)
            {
                _db.ChangeTracker.Clear();
                return Fail(refused ?? ledger.Problem!);
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            request.Status = PostingRequestStatuses.Posted;
            request.SupervisedByUserId = actor.StaffUserId;
            request.SupervisedOn = now;
            var postedId = await PostAsync(
                    request,
                    draft,
                    request.TransactionCodeId,
                    request.WorkingDate,
                    ledger.Lines!,
                    accounts,
                    request.RequestedByUserId,
                    actor.StaffUserId,
                    now,
                    cancellationToken)
                .ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok(new PostingOutcome(request.PostingRequestId, request.Reference, request.Status, postedId));
        }
    }

    /// <summary>A supervisor refuses a pending request; the held amounts are released. The requester cancels instead.</summary>
    public async Task<OperationResult<PostingOutcome>> RejectAsync(PostingActor actor, long postingRequestId, string reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var text = reason.Trim();
        return await DecideAsync(
                actor,
                postingRequestId,
                asSupervisor: true,
                PostingRequestStatuses.Rejected,
                text.Length > 300 ? text[..300] : text,
                EventCodes.PostingReject,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>The requester withdraws a pending request; the held amounts are released.</summary>
    public Task<OperationResult<PostingOutcome>> CancelAsync(PostingActor actor, long postingRequestId, CancellationToken cancellationToken) =>
        DecideAsync(actor, postingRequestId, asSupervisor: false, PostingRequestStatuses.Cancelled, null, EventCodes.PostingCancel, cancellationToken);

    // ---------------------------------------------------------------- Reads

    /// <summary>
    /// Requests in the actor's working branch that the actor made or may approve, newest first (approvals inbox,
    /// plan B 5.5). Defaults to pending ones.
    /// </summary>
    public async Task<IReadOnlyList<PostingRequestView>> ListAsync(PostingActor actor, string? status, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var wanted = string.IsNullOrWhiteSpace(status) ? PostingRequestStatuses.Pending : status.Trim().ToUpperInvariant();
        var rows = await Query(_db.Set<PostingRequest>()
                .Where(row => row.BranchId == actor.BranchId && row.Status == wanted)
                .OrderByDescending(row => row.PostingRequestId)
                .Take(ListLimit))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var permissions = await _permissions.ResolveAsync(actor.StaffUserId, actor.BranchId, cancellationToken).ConfigureAwait(false);
        return rows
            .Where(row => row.Request.RequestedByUserId == actor.StaffUserId || MaySupervise(permissions, row.Code))
            .Select(View)
            .ToList();
    }

    public async Task<PostingRequestView?> GetAsync(PostingActor actor, long postingRequestId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var row = await Query(_db.Set<PostingRequest>()
                .Where(item => item.PostingRequestId == postingRequestId && item.BranchId == actor.BranchId))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        if (row.Request.RequestedByUserId == actor.StaffUserId)
        {
            return View(row);
        }

        var permissions = await _permissions.ResolveAsync(actor.StaffUserId, actor.BranchId, cancellationToken).ConfigureAwait(false);
        return MaySupervise(permissions, row.Code) ? View(row) : null;
    }

    // ---------------------------------------------------------------- Steps

    private sealed record RequestRow(PostingRequest Request, string Code, string? RequestedByName);

    private sealed record Refusal(string ProblemCode, IReadOnlyDictionary<string, object?>? Extensions = null);

    private readonly record struct LedgerPlan(IReadOnlyList<LedgerLine>? Lines, Refusal? Problem);

    /// <summary>Filter and order first, then project: EF cannot filter on members of a constructor projection.</summary>
    private static IQueryable<RequestRow> Query(IQueryable<PostingRequest> requests) =>
        requests
            .AsNoTracking()
            .Select(row => new RequestRow(row, row.TransactionCode!.Code, row.RequestedByUser!.FullName));

    private static PostingRequestView View(RequestRow row) => new(
        row.Request.PostingRequestId,
        row.Request.Reference,
        row.Request.BranchId,
        row.Request.WorkingDate,
        row.Code,
        row.Request.Amount,
        row.Request.Status,
        JsonSerializer.Deserialize<PostingDraft>(row.Request.PayloadJson, Json)!,
        row.Request.RequestedByUserId,
        row.RequestedByName,
        row.Request.RequestedOn,
        row.Request.SupervisedByUserId,
        row.Request.SupervisedOn,
        row.Request.DecisionReason,
        row.Request.PostedTransactionId);

    private static bool MaySupervise(PermissionSet permissions, string transactionCode)
    {
        var (module, subModule) = PostingPermissions.For(transactionCode);
        return permissions.Allows(module, subModule, PermissionAction.Supervise);
    }

    private static Dictionary<string, string[]> Validate(PostingDraft draft)
    {
        var problems = new List<string>();
        if (!PostingRules.IsValidAmount(draft.Amount))
        {
            problems.Add("Enter an amount above zero with at most 2 decimal places.");
        }

        if (draft.AccountLines.Count == 0 && draft.LedgerLines.Count == 0)
        {
            problems.Add("A posting needs at least one line.");
        }

        if (draft.AccountLines.Any(static line => !PostingRules.IsSide(line.CrDb) || !PostingRules.IsValidAmount(line.Amount))
            || draft.LedgerLines.Any(static line => !PostingRules.IsSide(line.CrDb) || !PostingRules.IsValidAmount(line.Amount)))
        {
            problems.Add("Every line needs a side (C or D) and an amount above zero with at most 2 decimal places.");
        }

        if (draft.Narration is { Length: > 200 })
        {
            problems.Add("Keep the narration to 200 characters.");
        }

        return problems.Count == 0
            ? new Dictionary<string, string[]>(StringComparer.Ordinal)
            : new Dictionary<string, string[]>(StringComparer.Ordinal) { ["amount"] = [.. problems] };
    }

    private async Task<int> CodeIdAsync(string code, CancellationToken cancellationToken) =>
        await _db.Set<TransactionCode>()
            .AsNoTracking()
            .Where(row => row.Code == code && row.IsActive)
            .Select(row => (int?)row.TransactionCodeId)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
        ?? throw new InvalidOperationException($"Transaction code {code} is not seeded. Run the Migrator.");

    /// <summary>
    /// Takes an update lock on every balance row, one at a time in ascending account id (handoff 5), so two postings on
    /// the same accounts queue instead of deadlocking, then loads what the checks need. Null when an account is missing.
    /// </summary>
    private async Task<Dictionary<long, Account>?> LockAsync(PostingDraft draft, CancellationToken cancellationToken)
    {
        var ids = draft.AccountLines.Select(static line => line.AccountId).Distinct().Order().ToList();
        foreach (var id in ids)
        {
            await _db.Database
                .ExecuteSqlInterpolatedAsync($"SELECT [AccountId] FROM [AccountBalance] WITH (UPDLOCK, ROWLOCK) WHERE [AccountId] = {id}", cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var ledgerAccountId in (draft.Floors ?? []).Select(static floor => floor.LedgerAccountId).Distinct().Order())
        {
            await _db.Database
                .ExecuteSqlInterpolatedAsync($"SELECT [LedgerAccountId] FROM [LedgerAccount] WITH (UPDLOCK, ROWLOCK) WHERE [LedgerAccountId] = {ledgerAccountId}", cancellationToken)
                .ConfigureAwait(false);
        }

        var accounts = await _db.Set<Account>()
            .Include(row => row.Balance)
            .Include(row => row.Condition)
            .Include(row => row.Product)
            .Include(row => row.Holders).ThenInclude(holder => holder.Role)
            .Include(row => row.Holders).ThenInclude(holder => holder.Party!.PartyType)
            .AsSplitQuery()
            .Where(row => ids.Contains(row.AccountId))
            .ToDictionaryAsync(row => row.AccountId, cancellationToken)
            .ConfigureAwait(false);

        return accounts.Count == ids.Count && accounts.Values.All(static row => row.Balance is not null && row.Condition is not null && row.Product is not null)
            ? accounts
            : null;
    }

    /// <summary>Plan B 5.3 in order, per account, on the totals of its lines; then currency, then signer KYC on debits.</summary>
    private async Task<Refusal?> CheckAsync(int actorBranchId, PostingDraft draft, Dictionary<long, Account> accounts, CancellationToken cancellationToken)
    {
        // Every other branch touched must have its business day open too (plan B 5.3, Q4).
        foreach (var branchId in accounts.Values.Select(static row => row.BranchId).Concat(draft.LedgerLines.Select(static line => line.BranchId)).Distinct())
        {
            if (branchId != actorBranchId)
            {
                await _workingDates.GetAsync(branchId, cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (var group in draft.AccountLines.GroupBy(static line => line.AccountId))
        {
            var account = accounts[group.Key];
            var credit = group.Where(static line => line.CrDb == EntrySides.Credit).Sum(static line => line.Amount);
            var debit = group.Where(static line => line.CrDb == EntrySides.Debit).Sum(static line => line.Amount);
            var state = new AccountPostingState(
                Closed: account.ClosedOn is not null,
                Approved: SupervisionRules.CheckUsable(account).Allowed,
                Frozen: account.Condition!.IsFreezed,
                DoNotAllowCredit: account.Condition.DoNotAllowCredit,
                DoNotAllowDebit: account.Condition.DoNotAllowDebit,
                CreditLimit: account.Condition.CreditLimit,
                DebitLimit: account.Condition.DebitLimit,
                Dormant: account.IsDormant,
                AvailableBalance: account.Balance!.AvailableBalance,
                MinimumBalance: account.Product!.MinimumBalance ?? 0m);

            var refusal = PostingRules.Check(state, credit, debit);
            if (refusal != PostingRefusal.None)
            {
                return new Refusal(ProblemFor(refusal), new Dictionary<string, object?>(StringComparer.Ordinal) { ["accountNumber"] = account.AccountNumber });
            }
        }

        foreach (var floor in draft.Floors ?? [])
        {
            var current = await _db.Set<LedgerEntry>()
                .AsNoTracking()
                .Where(row => row.LedgerAccountId == floor.LedgerAccountId && row.BranchId == floor.BranchId)
                .SumAsync(row => row.CrDb == EntrySides.Debit ? row.Amount : -row.Amount, cancellationToken)
                .ConfigureAwait(false);
            var effect = draft.LedgerLines
                .Where(line => line.LedgerAccountId == floor.LedgerAccountId && line.BranchId == floor.BranchId)
                .Sum(static line => line.CrDb == EntrySides.Debit ? line.Amount : -line.Amount);
            if (current + effect < 0m)
            {
                return new Refusal(floor.ProblemCode);
            }
        }

        if (accounts.Values.Select(static row => row.Product!.CurrencyId).Distinct().Count() > 1)
        {
            return new Refusal(ProblemCodes.TransactionCurrencyMismatch);
        }

        if (draft.CheckSignerKyc)
        {
            var debited = draft.AccountLines.Where(static line => line.CrDb == EntrySides.Debit).Select(static line => line.AccountId).Distinct();
            foreach (var account in debited.Select(id => accounts[id]))
            {
                var signers = MandateRules.Signers(account.Holders.Select(static holder =>
                    new MandateHolder(holder.PartyId, holder.Role!.Code, holder.Party!.PartyType!.Code)));
                var statuses = await _kyc.EffectiveStatusesAsync(signers, cancellationToken).ConfigureAwait(false);
                if (signers.Count == 0 || signers.Any(id => statuses.GetValueOrDefault(id) != LookupCodes.KycStatuses.Verified))
                {
                    return new Refusal(ProblemCodes.AccountSignerKycLapsed, new Dictionary<string, object?>(StringComparer.Ordinal) { ["accountNumber"] = account.AccountNumber });
                }
            }
        }

        return null;
    }

    /// <summary>The draft's own GL lines plus one product-control line per customer line, in the account's branch (ADR B-3).</summary>
    private static LedgerPlan LedgerLines(PostingDraft draft, Dictionary<long, Account> accounts)
    {
        var lines = new List<LedgerLine>(draft.LedgerLines);
        foreach (var line in draft.AccountLines)
        {
            var account = accounts[line.AccountId];
            if (account.Product!.LedgerControlAccountId is not { } control)
            {
                return new LedgerPlan(null, new Refusal(
                    ProblemCodes.LedgerSettingMissing,
                    new Dictionary<string, object?>(StringComparer.Ordinal) { ["settingCode"] = "CONTROL", ["productId"] = account.ProductId }));
            }

            lines.Add(new LedgerLine(control, account.BranchId, line.CrDb, line.Amount));
        }

        var unbalanced = PostingRules.UnbalancedBranches(lines);
        if (unbalanced.Count > 0)
        {
            // The builders always balance; reaching this is a programming error, never a user error.
            throw new InvalidOperationException(
                "Posting " + draft.TransactionCode + " does not balance in branch " + string.Join(", ", unbalanced.Select(static id => id.ToString(CultureInfo.InvariantCulture))) + ".");
        }

        return new LedgerPlan(lines, null);
    }

    /// <summary>
    /// Plan B 5.2 step 5: approval is needed when the posting has no limit kind (reversals, journals), the actor has no
    /// limit or the amount is above it, or an account has <c>SuperviseAllTransactions</c>.
    /// </summary>
    private async Task<bool> NeedsApprovalAsync(PostingActor actor, PostingDraft draft, Dictionary<long, Account> accounts, CancellationToken cancellationToken)
    {
        if (draft.LimitCode is null || accounts.Values.Any(static row => row.Condition!.SuperviseAllTransactions))
        {
            return true;
        }

        var limit = await _limits.LimitAsync(actor.StaffUserId, actor.BranchId, draft.LimitCode, cancellationToken).ConfigureAwait(false);
        return limit is null || draft.Amount > limit.Value;
    }

    private async Task<string> NextReferenceAsync(int branchId, DateOnly workingDate, CancellationToken cancellationToken)
    {
        var branchCode = await _db.Set<Branch>()
            .AsNoTracking()
            .Where(row => row.BranchId == branchId)
            .Select(row => row.BranchCode)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        // Not composed (no Single/First): SQL Server does not allow NEXT VALUE FOR inside a derived table.
        var values = await _db.Database
            .SqlQueryRaw<long>("SELECT NEXT VALUE FOR [dbo].[" + BankingModelConventions.TransactionReferenceSequence + "] AS [Value]")
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return string.Create(CultureInfo.InvariantCulture, $"{branchCode}-{workingDate:yyMMdd}-{values[0]:D6}");
    }

    /// <summary>Writes the header, the customer lines with their running balance, and the GL lines; links the request.</summary>
    private async Task<long> PostAsync(
        PostingRequest request,
        PostingDraft draft,
        int codeId,
        DateOnly workingDate,
        IReadOnlyList<LedgerLine> ledgerLines,
        Dictionary<long, Account> accounts,
        long operatorId,
        long? supervisorId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var posted = new PostedTransaction
        {
            Reference = request.Reference,
            PostingRequest = request,
            BranchId = request.BranchId,
            EntryDate = workingDate,
            TransactionCodeId = codeId,
            Narration = draft.Narration,
            OperatorStaffUserId = operatorId,
            SupervisorStaffUserId = supervisorId,
            PostedOn = now
        };

        var customerLines = new List<(Account Account, AccountLine Line, decimal BalanceAfter)>(draft.AccountLines.Count);
        foreach (var line in draft.AccountLines)
        {
            var account = accounts[line.AccountId];
            var balanceAfter = PostingRules.Post(account.Balance!, line.CrDb, line.Amount);
            if (line.CrDb == EntrySides.Credit)
            {
                account.Balance!.LastCreditTransactionDate = now;
            }
            else
            {
                account.Balance!.LastDebitTransactionDate = now;
            }

            customerLines.Add((account, line, balanceAfter));
        }

        _db.Set<PostedTransaction>().Add(posted);
        _activity.Record(
            supervisorId is null ? EventCodes.PostingPost : EventCodes.PostingApprove,
            nameof(PostedTransaction),
            request.Reference,
            Details(draft));
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // The lines are written one plain INSERT each. EF batches four or more inserts into one table as a MERGE,
        // which SQL Server refuses on an append-only ledger table.
        foreach (var (account, line, balanceAfter) in customerLines)
        {
            await _db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT INTO [dbo].[AccountLedgerEntry]
                        ([PostedTransactionId], [AccountId], [BranchId], [EntryDate], [CrDb], [Amount], [BalanceAfter], [TransactionCodeId])
                    VALUES ({posted.PostedTransactionId}, {account.AccountId}, {account.BranchId}, {workingDate}, {line.CrDb}, {line.Amount}, {balanceAfter}, {codeId})
                    """,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var line in ledgerLines)
        {
            await _db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT INTO [dbo].[LedgerEntry]
                        ([PostedTransactionId], [LedgerAccountId], [BranchId], [EntryDate], [CrDb], [Amount])
                    VALUES ({posted.PostedTransactionId}, {line.LedgerAccountId}, {line.BranchId}, {workingDate}, {line.CrDb}, {line.Amount})
                    """,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (draft.ClosesTillSessionId is { } tillSessionId)
        {
            var session = await _db.Set<TillSession>().SingleAsync(row => row.TillSessionId == tillSessionId, cancellationToken).ConfigureAwait(false);
            session.Status = TillSessionStatuses.Closed;
            session.ClosedOn = now;
            _activity.Record(EventCodes.TillClose, nameof(TillSession), tillSessionId.ToString(CultureInfo.InvariantCulture));
        }

        request.PostedTransactionId = posted.PostedTransactionId;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return posted.PostedTransactionId;
    }

    /// <summary>Loads a pending request in the actor's branch the actor may decide on, with its draft.</summary>
    private async Task<(PostingRequest? Request, PostingDraft? Draft, string? Problem)> DecisionTargetAsync(
        PostingActor actor,
        long postingRequestId,
        CancellationToken cancellationToken)
    {
        var request = await _db.Set<PostingRequest>()
            .Include(row => row.TransactionCode)
            .SingleOrDefaultAsync(row => row.PostingRequestId == postingRequestId, cancellationToken)
            .ConfigureAwait(false);
        if (request is null)
        {
            return (null, null, ProblemCodes.NotFound);
        }

        if (request.BranchId != actor.BranchId)
        {
            return (null, null, ProblemCodes.RecordOtherBranch);
        }

        if (request.Status != PostingRequestStatuses.Pending)
        {
            return (null, null, SupervisionProblems.NotPending);
        }

        var permissions = await _permissions.ResolveAsync(actor.StaffUserId, actor.BranchId, cancellationToken).ConfigureAwait(false);
        if (!MaySupervise(permissions, request.TransactionCode!.Code))
        {
            return (null, null, SupervisionProblems.PermissionDenied);
        }

        return (request, JsonSerializer.Deserialize<PostingDraft>(request.PayloadJson, Json)!, null);
    }

    private async Task<OperationResult<PostingOutcome>> DecideAsync(
        PostingActor actor,
        long postingRequestId,
        bool asSupervisor,
        string status,
        string? reason,
        string eventCode,
        CancellationToken cancellationToken)
    {
        var transaction = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            PostingRequest? request;
            PostingDraft? draft;
            if (asSupervisor)
            {
                string? problem;
                (request, draft, problem) = await DecisionTargetAsync(actor, postingRequestId, cancellationToken).ConfigureAwait(false);
                if (problem is not null)
                {
                    return OperationResult.Fail<PostingOutcome>(problem);
                }
            }
            else
            {
                request = await _db.Set<PostingRequest>()
                    .SingleOrDefaultAsync(row => row.PostingRequestId == postingRequestId && row.BranchId == actor.BranchId, cancellationToken)
                    .ConfigureAwait(false);
                if (request is null)
                {
                    return OperationResult.Fail<PostingOutcome>(ProblemCodes.NotFound);
                }

                if (request.Status != PostingRequestStatuses.Pending)
                {
                    return OperationResult.Fail<PostingOutcome>(SupervisionProblems.NotPending);
                }

                if (request.RequestedByUserId != actor.StaffUserId)
                {
                    return OperationResult.Fail<PostingOutcome>(ProblemCodes.PostingNotRequester);
                }

                draft = JsonSerializer.Deserialize<PostingDraft>(request.PayloadJson, Json)!;
            }

            var accounts = await LockAsync(draft!, cancellationToken).ConfigureAwait(false);
            if (accounts is null)
            {
                return OperationResult.Fail<PostingOutcome>(ProblemCodes.NotFound);
            }

            foreach (var line in draft!.AccountLines)
            {
                PostingRules.Release(accounts[line.AccountId].Balance!, line.CrDb, line.Amount);
            }

            if (draft.ClosesTillSessionId is { } tillSessionId)
            {
                // The close did not go through: the till is open again and can be counted again.
                var session = await _db.Set<TillSession>().SingleAsync(row => row.TillSessionId == tillSessionId, cancellationToken).ConfigureAwait(false);
                session.Status = TillSessionStatuses.Open;
                session.CountedAmount = null;
                session.ExpectedAmount = null;
                session.ClosePostingRequestId = null;
            }

            request!.Status = status;
            request.SupervisedByUserId = asSupervisor ? actor.StaffUserId : null;
            request.SupervisedOn = _timeProvider.GetUtcNow().UtcDateTime;
            request.DecisionReason = reason;
            _activity.Record(eventCode, nameof(PostingRequest), request.Reference);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok(new PostingOutcome(request.PostingRequestId, request.Reference, request.Status, null));
        }
    }

    private static Dictionary<string, object?> Details(PostingDraft draft) =>
        new(StringComparer.Ordinal)
        {
            ["transactionCode"] = draft.TransactionCode,
            ["amount"] = draft.Amount
        };

    private static OperationResult<PostingOutcome> Fail(Refusal refusal) =>
        refusal.Extensions is { Count: > 0 } extensions
            ? OperationResult.FailWith<PostingOutcome>(refusal.ProblemCode, extensions)
            : OperationResult.Fail<PostingOutcome>(refusal.ProblemCode);

    private static string ProblemFor(PostingRefusal refusal) => refusal switch
    {
        PostingRefusal.Closed => ProblemCodes.AccountClosed,
        PostingRefusal.NotApproved => SupervisionProblems.Pending,
        PostingRefusal.Frozen => ProblemCodes.AccountFrozen,
        PostingRefusal.CreditBlocked => ProblemCodes.AccountCreditBlocked,
        PostingRefusal.DebitBlocked => ProblemCodes.AccountDebitBlocked,
        PostingRefusal.CreditLimit => ProblemCodes.AccountCreditLimit,
        PostingRefusal.DebitLimit => ProblemCodes.AccountDebitLimit,
        PostingRefusal.Dormant => ProblemCodes.AccountDormant,
        PostingRefusal.InsufficientFunds => ProblemCodes.AccountInsufficientFunds,
        _ => ProblemCodes.Unexpected
    };
}
