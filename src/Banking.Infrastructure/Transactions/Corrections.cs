using System.Globalization;
using Banking.Application.Abstractions;
using Banking.Application.Errors;
using Banking.Application.Transactions;
using Banking.Domain.Accounts;
using Banking.Domain.Institution;
using Banking.Domain.Ledger;
using Banking.Domain.Products;
using Banking.Domain.Security;
using Banking.Domain.Supervision;
using Banking.Domain.Transactions;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Banking.Infrastructure.Transactions;

/// <summary>A request to reverse a posted transaction, found by its reference.</summary>
public sealed record ReversalDraft(string? Reference, string? Reason);

/// <summary>One journal line: a GL line (ledger account, optional branch) or a customer account line.</summary>
public sealed record JournalLineDraft(long? LedgerAccountId, long? AccountId, int? BranchId, string? CrDb, decimal? Amount);

public sealed record JournalDraft(string? Narration, IReadOnlyList<JournalLineDraft>? Lines);

/// <summary>Helpers the reversal and journal builders share with the tills.</summary>
internal static class Corrections
{
    /// <summary>Whether a pending reversal's payload has <paramref name="value"/> at <paramref name="jsonPath"/>.</summary>
    internal static async Task<bool> PendingReversalsAsync(BankingDbContext db, string jsonPath, string value, CancellationToken cancellationToken)
    {
        var counts = await db.Database
            .SqlQuery<int>(
                $"""
                SELECT COUNT(*) AS [Value]
                FROM [dbo].[PostingRequest] r
                INNER JOIN [dbo].[TransactionCode] c ON c.[TransactionCodeId] = r.[TransactionCodeId]
                WHERE r.[Status] = {PostingRequestStatuses.Pending}
                  AND c.[Code] = {TransactionCodes.Reversal}
                  AND JSON_VALUE(r.[PayloadJson], {jsonPath}) = {value}
                """)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return counts[0] > 0;
    }

    /// <summary>Every till ledger account (any teller's).</summary>
    internal static async Task<HashSet<long>> TillAccountsAsync(BankingDbContext db, CancellationToken cancellationToken) =>
        [.. await db.Set<StaffUser>()
            .AsNoTracking()
            .Where(row => row.TellerGLAccountId != null)
            .Select(row => row.TellerGLAccountId!.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false)];

    /// <summary>Every branch's vault ledger account, by branch.</summary>
    internal static async Task<HashSet<(long LedgerAccountId, int BranchId)>> VaultAccountsAsync(BankingDbContext db, CancellationToken cancellationToken) =>
        [.. (await db.Set<SystemLedgerSetting>()
            .AsNoTracking()
            .Where(row => row.SettingCode == LedgerSettingCodes.Vault)
            .Select(row => new { row.LedgerAccountId, row.BranchId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false)).Select(static row => (row.LedgerAccountId, row.BranchId))];

    internal static PostingActor Actor(ICurrentUser currentUser) =>
        new(
            currentUser.StaffUserId ?? throw new InvalidOperationException("Corrections need a signed-in user."),
            currentUser.BranchId ?? throw new InvalidOperationException("Corrections need a working branch."));

    internal static string Key(long id) => id.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// B4-01: reversal (plan B 6.7). A posted transaction from the working branch, at most
/// <c>Transactions:ReversalDays</c> old, is reversed once, by a request that always waits for a <c>TXN/REVERSAL</c>
/// supervisor. The reversal mirrors every customer and GL line (C and D swapped) on today's working date, and the
/// normal checks run on the mirror, so reversing a deposit is refused once the money has gone. A reversal, a vault
/// movement or a till close cannot be reversed; neither can cash once its till session has started closing.
/// </summary>
public sealed class Reversals
{
    private const int ReasonLength = 150;

    private static readonly HashSet<string> NotReversible = new(StringComparer.Ordinal)
    {
        TransactionCodes.Reversal,
        TransactionCodes.TillBuy,
        TransactionCodes.TillSell,
        TransactionCodes.TillShortage,
        TransactionCodes.TillOverage
    };

    private readonly BankingDbContext _db;
    private readonly PostingEngine _engine;
    private readonly ICurrentUser _currentUser;
    private readonly IWorkingDateProvider _workingDates;
    private readonly TransactionOptions _options;

    public Reversals(BankingDbContext db, PostingEngine engine, ICurrentUser currentUser, IWorkingDateProvider workingDates, IOptions<TransactionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _db = db;
        _engine = engine;
        _currentUser = currentUser;
        _workingDates = workingDates;
        _options = options.Value;
    }

    public async Task<OperationResult<PostingOutcome>> ReverseAsync(ReversalDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var reference = draft.Reference?.Trim();
        if (string.IsNullOrEmpty(reference) || reference.Length > 30)
        {
            errors["reference"] = ["Enter the reference of the transaction to reverse."];
        }

        var reason = draft.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > ReasonLength)
        {
            errors["reason"] = ["Say why this transaction is being reversed, in at most 150 characters."];
        }

        if (errors.Count > 0)
        {
            return OperationResult.Fail<PostingOutcome>(ProblemCodes.Validation, errors);
        }

        var actor = Corrections.Actor(_currentUser);
        var original = await _db.Set<PostedTransaction>()
            .AsNoTracking()
            .Include(row => row.AccountEntries)
            .Include(row => row.LedgerEntries)
            .Include(row => row.TransactionCode)
            .Include(row => row.PostingRequest)
            .AsSplitQuery()
            .SingleOrDefaultAsync(row => row.Reference == reference, cancellationToken)
            .ConfigureAwait(false);
        if (original is null)
        {
            return OperationResult.Fail<PostingOutcome>(ProblemCodes.NotFound);
        }

        if (original.BranchId != actor.BranchId)
        {
            return OperationResult.Fail<PostingOutcome>(ProblemCodes.RecordOtherBranch);
        }

        var code = original.TransactionCode!.Code;
        if (original.ReversesTransactionId is not null || NotReversible.Contains(code))
        {
            return OperationResult.Fail<PostingOutcome>(ProblemCodes.TransactionNotReversible);
        }

        if (await _db.Set<PostedTransaction>().AnyAsync(row => row.ReversesTransactionId == original.PostedTransactionId, cancellationToken).ConfigureAwait(false))
        {
            return OperationResult.Fail<PostingOutcome>(ProblemCodes.TransactionAlreadyReversed);
        }

        var key = Corrections.Key(original.PostedTransactionId);
        if (await Corrections.PendingReversalsAsync(_db, "$.reversesTransactionId", key, cancellationToken).ConfigureAwait(false))
        {
            return OperationResult.Fail<PostingOutcome>(ProblemCodes.TransactionReversalPending);
        }

        var workingDate = await _workingDates.GetAsync(actor.BranchId, cancellationToken).ConfigureAwait(false);
        if (original.EntryDate < workingDate.AddDays(-_options.ReversalDays))
        {
            return OperationResult.Fail<PostingOutcome>(ProblemCodes.TransactionReversalWindow);
        }

        var originalDraft = original.PostingRequest is { } request ? PostingEngine.ReadDraft(request.PayloadJson) : null;
        var details = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["originalReference"] = original.Reference,
            ["originalTransactionCode"] = code,
            ["reason"] = reason
        };

        // Cash goes back over the counter of the till that took it, so that till must still be open.
        if (code is TransactionCodes.CashDeposit or TransactionCodes.CashWithdrawal)
        {
            var tillSessionId = originalDraft?.Details?.GetValueOrDefault("tillSessionId");
            var open = long.TryParse(tillSessionId, NumberStyles.None, CultureInfo.InvariantCulture, out var sessionId)
                       && await _db.Set<TillSession>().AnyAsync(row => row.TillSessionId == sessionId && row.Status == TillSessionStatuses.Open, cancellationToken).ConfigureAwait(false);
            if (!open)
            {
                return OperationResult.Fail<PostingOutcome>(ProblemCodes.TransactionTillClosed);
            }

            details["tillSessionId"] = tillSessionId;
        }

        var accountLines = original.AccountEntries
            .OrderBy(static row => row.AccountLedgerEntryId)
            .Select(static row => new AccountLine(row.AccountId, PostingRules.Opposite(row.CrDb), row.Amount))
            .ToList();
        var ledgerLines = original.LedgerEntries
            .OrderBy(static row => row.LedgerEntryId)
            .Select(static row => new LedgerLine(row.LedgerAccountId, row.BranchId, PostingRules.Opposite(row.CrDb), row.Amount))
            .ToList();

        // Cash taken out of a till or the vault by the mirror must be there.
        var tills = await Corrections.TillAccountsAsync(_db, cancellationToken).ConfigureAwait(false);
        var vaults = await Corrections.VaultAccountsAsync(_db, cancellationToken).ConfigureAwait(false);
        var floors = ledgerLines
            .Where(static line => line.CrDb == EntrySides.Credit)
            .Select(line => tills.Contains(line.LedgerAccountId)
                ? new LedgerFloor(line.LedgerAccountId, line.BranchId, ProblemCodes.TillInsufficientCash)
                : vaults.Contains((line.LedgerAccountId, line.BranchId))
                    ? new LedgerFloor(line.LedgerAccountId, line.BranchId, ProblemCodes.VaultInsufficientCash)
                    : null)
            .OfType<LedgerFloor>()
            .Distinct()
            .ToList();

        var amount = original.PostingRequest?.Amount
                     ?? ledgerLines.Where(static line => line.CrDb == EntrySides.Debit).Sum(static line => line.Amount);
        var narration = "Reversal of " + original.Reference + ": " + reason;

        return await _engine.SubmitAsync(
                actor,
                new PostingDraft(
                    TransactionCodes.Reversal,
                    LimitCode: null,
                    amount,
                    narration.Length > 200 ? narration[..200] : narration,
                    accountLines,
                    ledgerLines,
                    CheckSignerKyc: false,
                    details,
                    floors,
                    ExactLedgerLines: true,
                    ReversesTransactionId: original.PostedTransactionId),
                cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>
/// B4-02: manual journal (plan B 6.7). Balanced lines on GL accounts, optionally with customer account lines for
/// corrections; always approved by a <c>LED/JOURNAL</c> supervisor. Customer lines run the normal account checks and
/// the engine adds their control-account lines. Till, vault and product control accounts are refused: cash moves only
/// through tills, and a control account only through the customer accounts it controls.
/// </summary>
public sealed class Journals
{
    public const int MaxLines = 50;

    private readonly BankingDbContext _db;
    private readonly PostingEngine _engine;
    private readonly ICurrentUser _currentUser;

    public Journals(BankingDbContext db, PostingEngine engine, ICurrentUser currentUser)
    {
        _db = db;
        _engine = engine;
        _currentUser = currentUser;
    }

    public async Task<OperationResult<PostingOutcome>> PostAsync(JournalDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var actor = Corrections.Actor(_currentUser);
        var lines = draft.Lines ?? [];
        var errors = Validate(draft.Narration, lines);
        if (errors.Count > 0)
        {
            return OperationResult.Fail<PostingOutcome>(ProblemCodes.Validation, errors);
        }

        var ledgerIds = lines.Where(static line => line.LedgerAccountId is not null).Select(static line => line.LedgerAccountId!.Value).Distinct().ToList();
        var accountIds = lines.Where(static line => line.AccountId is not null).Select(static line => line.AccountId!.Value).Distinct().ToList();
        var branchIds = lines.Where(static line => line.BranchId is not null).Select(static line => line.BranchId!.Value).Distinct().ToList();

        var ledgerAccounts = await _db.Set<LedgerAccount>()
            .AsNoTracking()
            .Where(row => ledgerIds.Contains(row.LedgerAccountId))
            .ToDictionaryAsync(row => row.LedgerAccountId, cancellationToken)
            .ConfigureAwait(false);
        var accountBranches = await _db.Set<Account>()
            .AsNoTracking()
            .Where(row => accountIds.Contains(row.AccountId))
            .ToDictionaryAsync(row => row.AccountId, row => row.BranchId, cancellationToken)
            .ConfigureAwait(false);
        var branches = await _db.Set<Branch>()
            .AsNoTracking()
            .Where(row => branchIds.Contains(row.BranchId))
            .Select(row => row.BranchId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (line.LedgerAccountId is { } ledgerId && !ledgerAccounts.ContainsKey(ledgerId))
            {
                errors[Field(index, "ledgerAccountId")] = ["This ledger account does not exist."];
            }

            if (line.AccountId is { } accountId && !accountBranches.ContainsKey(accountId))
            {
                errors[Field(index, "accountId")] = ["This account does not exist."];
            }

            if (line.BranchId is { } branchId && !branches.Contains(branchId))
            {
                errors[Field(index, "branchId")] = ["This branch does not exist."];
            }
        }

        if (errors.Count > 0)
        {
            return OperationResult.Fail<PostingOutcome>(ProblemCodes.Validation, errors);
        }

        var refused = await RefusedAccountAsync([.. ledgerAccounts.Values], cancellationToken).ConfigureAwait(false);
        if (refused is not null)
        {
            return OperationResult.FailWith<PostingOutcome>(
                ProblemCodes.JournalAccountNotAllowed,
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["ledgerAccountCode"] = refused.LedgerAccountCode });
        }

        var ledgerLines = new List<LedgerLine>();
        var accountLines = new List<AccountLine>();
        var balance = new List<LedgerLine>();
        foreach (var line in lines)
        {
            var side = line.CrDb!.Trim().ToUpperInvariant();
            var value = line.Amount!.Value;
            if (line.LedgerAccountId is { } ledgerId)
            {
                var entry = new LedgerLine(ledgerId, line.BranchId ?? actor.BranchId, side, value);
                ledgerLines.Add(entry);
                balance.Add(entry);
            }
            else
            {
                var accountId = line.AccountId!.Value;
                accountLines.Add(new AccountLine(accountId, side, value));

                // The engine adds the control line in the account's branch; the balance check here counts it the same way.
                balance.Add(new LedgerLine(0, accountBranches[accountId], side, value));
            }
        }

        if (PostingRules.UnbalancedBranches(balance).Count > 0)
        {
            return OperationResult.Fail<PostingOutcome>(
                ProblemCodes.Validation,
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["lines"] = ["Debits and credits must be equal in each branch."] });
        }

        var amount = balance.Where(static line => line.CrDb == EntrySides.Debit).Sum(static line => line.Amount);
        return await _engine.SubmitAsync(
                actor,
                new PostingDraft(
                    TransactionCodes.Journal,
                    LimitCode: null,
                    amount,
                    draft.Narration!.Trim(),
                    accountLines,
                    ledgerLines,
                    CheckSignerKyc: false,
                    new Dictionary<string, string?>(StringComparer.Ordinal) { ["lineCount"] = Corrections.Key(lines.Count) }),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static Dictionary<string, string[]> Validate(string? narration, IReadOnlyList<JournalLineDraft> lines)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(narration) || narration.Trim().Length > 200)
        {
            errors["narration"] = ["Explain the journal, in at most 200 characters."];
        }

        if (lines.Count < 2 || lines.Count > MaxLines)
        {
            errors["lines"] = ["A journal needs between 2 and 50 lines."];
            return errors;
        }

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if ((line.LedgerAccountId is null) == (line.AccountId is null))
            {
                errors[Field(index, "ledgerAccountId")] = ["Choose a ledger account or a customer account, not both."];
            }
            else if (line.AccountId is not null && line.BranchId is not null)
            {
                errors[Field(index, "branchId")] = ["A customer account line is always in the account's branch."];
            }

            if (!PostingRules.IsSide(line.CrDb?.Trim().ToUpperInvariant()))
            {
                errors[Field(index, "crDb")] = ["Choose debit (D) or credit (C)."];
            }

            if (line.Amount is not { } amount || !PostingRules.IsValidAmount(amount))
            {
                errors[Field(index, "amount")] = ["Enter an amount above zero with at most 2 decimal places."];
            }
        }

        return errors;
    }

    /// <summary>The first ledger account a journal may not use: not approved, a till, a vault or a product control account.</summary>
    private async Task<LedgerAccount?> RefusedAccountAsync(List<LedgerAccount> accounts, CancellationToken cancellationToken)
    {
        var tills = await Corrections.TillAccountsAsync(_db, cancellationToken).ConfigureAwait(false);
        var vaults = (await Corrections.VaultAccountsAsync(_db, cancellationToken).ConfigureAwait(false)).Select(static row => row.LedgerAccountId).ToHashSet();
        var controls = (await _db.Set<Product>()
                .AsNoTracking()
                .Where(row => row.LedgerControlAccountId != null)
                .Select(row => row.LedgerControlAccountId!.Value)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToHashSet();

        return accounts
            .OrderBy(static row => row.LedgerAccountId)
            .FirstOrDefault(row => !SupervisionRules.CheckUsable(row).Allowed
                                   || tills.Contains(row.LedgerAccountId)
                                   || vaults.Contains(row.LedgerAccountId)
                                   || controls.Contains(row.LedgerAccountId));
    }

    private static string Field(int index, string name) => string.Create(CultureInfo.InvariantCulture, $"lines[{index}].{name}");
}
