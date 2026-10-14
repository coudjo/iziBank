using System.Globalization;
using System.Text.Json;
using Banking.Application.Abstractions;
using Banking.Application.Errors;
using Banking.Application.Supervision;
using Banking.Domain.Institution;
using Banking.Domain.Ledger;
using Banking.Domain.Lookups;
using Banking.Domain.Products;
using Banking.Domain.Security;
using Banking.Domain.Supervision;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Banking.Infrastructure.Ledger;

public sealed record PendingLedgerRename(long ChangeRequestId, string ProposedName, long RequestedByUserId, DateTime RequestedOn);

public sealed record LedgerAccountView(
    long LedgerAccountId,
    int BranchId,
    string BranchCode,
    string LedgerAccountCode,
    string AccountName,
    int? LedgerAccountTypeId,
    string? LedgerAccountTypeCode,
    int? CurrencyId,
    string? CurrencyCode,
    decimal Balance,
    string SupervisionState,
    long? CreatedByUserId,
    long? ModifiedByUserId,
    long? SupervisedByUserId,
    DateTime? SupervisedOn,
    PendingLedgerRename? PendingChange);

public sealed record LedgerAccountDraft(string? LedgerAccountCode, string AccountName, int? LedgerAccountTypeId, int? CurrencyId);

public sealed record LedgerAccountFilter(int? BranchId, int? LedgerAccountTypeId, string? Search, string? SupervisionState);

/// <summary>What an edit did: applied at once, or waiting for approval as a change request.</summary>
public sealed record LedgerEditOutcome(bool Applied, long? ChangeRequestId);

public static class LedgerSupervisionStates
{
    public const string Pending = "PENDING";
    public const string Approved = "APPROVED";
    public const string NotRequired = "NOT_REQUIRED";
}

/// <summary>
/// A3-03: chart of accounts (plan 6.11) with maker-checker (5.4). A new account is Pending until a different
/// supervisor approves it, and only a Pending account can be rejected (it is deleted). After approval the code,
/// type, currency and branch never change; a new name is a change request that applies only when approved, so an
/// account in use never drops back to Pending (ADR 0002, A3-a).
/// </summary>
public sealed class LedgerAccountAdmin
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly BankingDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IActivityContext _activity;
    private readonly TimeProvider _timeProvider;
    private readonly SupervisionOptions _supervision;

    public LedgerAccountAdmin(
        BankingDbContext db,
        ICurrentUser currentUser,
        IActivityContext activity,
        TimeProvider timeProvider,
        IOptions<SupervisionOptions> supervision)
    {
        ArgumentNullException.ThrowIfNull(supervision);
        _db = db;
        _currentUser = currentUser;
        _activity = activity;
        _timeProvider = timeProvider;
        _supervision = supervision.Value;
    }

    public async Task<PagedResult<LedgerAccountView>> ListAsync(LedgerAccountFilter filter, int page, int pageSize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = _db.Set<LedgerAccount>().AsNoTracking();
        if (filter.BranchId is { } branchId)
        {
            query = query.Where(row => row.BranchId == branchId);
        }

        if (filter.LedgerAccountTypeId is { } typeId)
        {
            query = query.Where(row => row.LedgerAccountTypeId == typeId);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = "%" + filter.Search.Trim()
                .Replace("[", "[[]", StringComparison.Ordinal)
                .Replace("%", "[%]", StringComparison.Ordinal)
                .Replace("_", "[_]", StringComparison.Ordinal) + "%";
            query = query.Where(row => EF.Functions.Like(row.LedgerAccountCode, pattern) || EF.Functions.Like(row.AccountName, pattern));
        }

        switch (filter.SupervisionState?.Trim().ToUpperInvariant())
        {
            case LedgerSupervisionStates.Pending:
                query = query.Where(row => row.IsSupervisionRequired && row.SupervisedByUserId == null);
                break;
            case LedgerSupervisionStates.Approved:
                query = query.Where(row => row.IsSupervisionRequired && row.SupervisedByUserId != null);
                break;
            case LedgerSupervisionStates.NotRequired:
                query = query.Where(row => !row.IsSupervisionRequired);
                break;
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await Project(query.OrderBy(row => row.LedgerAccountCode).Skip((page - 1) * pageSize).Take(pageSize))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = await WithPendingAsync(rows, cancellationToken).ConfigureAwait(false);
        return new PagedResult<LedgerAccountView>(items, page, pageSize, total);
    }

    public async Task<LedgerAccountView?> GetAsync(long ledgerAccountId, CancellationToken cancellationToken)
    {
        var rows = await Project(_db.Set<LedgerAccount>().AsNoTracking().Where(row => row.LedgerAccountId == ledgerAccountId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return (await WithPendingAsync(rows, cancellationToken).ConfigureAwait(false)).SingleOrDefault();
    }

    /// <summary>Creates the account in the effective branch. It starts Pending when supervision is on (R1).</summary>
    public async Task<OperationResult<long>> CreateAsync(LedgerAccountDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var branchId = _currentUser.BranchId ?? throw new InvalidOperationException("Creating a ledger account needs a branch.");
        var code = draft.LedgerAccountCode?.Trim().ToUpperInvariant() ?? string.Empty;
        var errors = await ValidateAsync(draft, cancellationToken).ConfigureAwait(false);
        if (code.Length is 0 or > 20
            || !code.All(static character => char.IsAsciiLetterUpper(character) || char.IsAsciiDigit(character) || character is '-' or '.'))
        {
            errors["ledgerAccountCode"] = ["Use 1 to 20 capital letters, digits, dots or dashes."];
        }

        if (errors.Count > 0)
        {
            return OperationResult.Fail<long>(ProblemCodes.Validation, errors);
        }

        if (await _db.Set<LedgerAccount>().AnyAsync(row => row.LedgerAccountCode == code, cancellationToken).ConfigureAwait(false))
        {
            return OperationResult.Fail<long>(ProblemCodes.ReferenceCodeTaken);
        }

        var account = new LedgerAccount
        {
            BranchId = branchId,
            LedgerAccountCode = code,
            AccountName = draft.AccountName.Trim(),
            LedgerAccountTypeId = draft.LedgerAccountTypeId,
            CurrencyId = draft.CurrencyId,
            Balance = 0m
        };
        SupervisionRules.StartNew(account, _supervision.Enabled);
        _db.Set<LedgerAccount>().Add(account);
        _activity.Record(EventCodes.CreateRecord, nameof(LedgerAccount), code);

        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            _db.Entry(account).State = EntityState.Detached;
            _activity.Written = false;
            return OperationResult.Fail<long>(ProblemCodes.ReferenceCodeTaken);
        }

        return OperationResult.Ok(account.LedgerAccountId);
    }

    /// <summary>
    /// A Pending account is edited in place (it stays Pending, and the editor can no longer approve it). An approved
    /// account keeps its type and currency; a new name becomes a change request when supervision is on.
    /// </summary>
    public async Task<OperationResult<LedgerEditOutcome>> UpdateAsync(long ledgerAccountId, LedgerAccountDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var account = await _db.Set<LedgerAccount>().SingleOrDefaultAsync(row => row.LedgerAccountId == ledgerAccountId, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return OperationResult.Fail<LedgerEditOutcome>(ProblemCodes.NotFound);
        }

        if (account.BranchId != _currentUser.BranchId)
        {
            return OperationResult.Fail<LedgerEditOutcome>(ProblemCodes.RecordOtherBranch);
        }

        if (draft.LedgerAccountCode is not null
            && !string.Equals(draft.LedgerAccountCode.Trim(), account.LedgerAccountCode, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Fail<LedgerEditOutcome>(ProblemCodes.ReferenceCodeImmutable);
        }

        var errors = await ValidateAsync(draft, cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return OperationResult.Fail<LedgerEditOutcome>(ProblemCodes.Validation, errors);
        }

        var name = draft.AccountName.Trim();
        if (SupervisionRules.StateOf(account) == SupervisionState.Pending)
        {
            account.AccountName = name;
            account.LedgerAccountTypeId = draft.LedgerAccountTypeId;
            account.CurrencyId = draft.CurrencyId;
            _activity.Record(EventCodes.UpdateRecord, nameof(LedgerAccount), account.LedgerAccountCode);
            return await SaveEditAsync(account, cancellationToken).ConfigureAwait(false);
        }

        if (draft.LedgerAccountTypeId != account.LedgerAccountTypeId || draft.CurrencyId != account.CurrencyId)
        {
            return OperationResult.Fail<LedgerEditOutcome>(ProblemCodes.LedgerFieldLocked);
        }

        if (string.Equals(name, account.AccountName, StringComparison.Ordinal))
        {
            return OperationResult.Ok(new LedgerEditOutcome(Applied: true, ChangeRequestId: null));
        }

        if (!_supervision.Enabled || !account.IsSupervisionRequired)
        {
            account.AccountName = name;
            _activity.Record(EventCodes.UpdateRecord, nameof(LedgerAccount), account.LedgerAccountCode);
            return await SaveEditAsync(account, cancellationToken).ConfigureAwait(false);
        }

        if (await PendingRenameQuery(ledgerAccountId).AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return OperationResult.Fail<LedgerEditOutcome>(ProblemCodes.ChangePendingExists);
        }

        var request = new ChangeRequest
        {
            EntityType = ChangeRequestEntities.LedgerAccount,
            EntityId = Key(ledgerAccountId),
            ChangeType = ChangeRequestTypes.LedgerAccountRename,
            Status = ChangeRequestStatuses.Pending,
            BeforeJson = JsonSerializer.Serialize(new RenamePayload(account.AccountName), Json),
            PayloadJson = JsonSerializer.Serialize(new RenamePayload(name), Json),
            RequestedByUserId = _currentUser.StaffUserId ?? throw new InvalidOperationException("A change request needs a signed-in user."),
            RequestedOn = _timeProvider.GetUtcNow().UtcDateTime
        };

        // Only the change request is saved; the live row is untouched until approval.
        _db.Set<ChangeRequest>().Add(request);
        _activity.Record(EventCodes.UpdateRecord, nameof(LedgerAccount), account.LedgerAccountCode, new Dictionary<string, object?> { ["changeRequest"] = true });

        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // The filtered unique index caught a concurrent request for the same account.
            _db.Entry(request).State = EntityState.Detached;
            _activity.Written = false;
            return OperationResult.Fail<LedgerEditOutcome>(ProblemCodes.ChangePendingExists);
        }

        return OperationResult.Ok(new LedgerEditOutcome(Applied: false, request.ChangeRequestId));
    }

    /// <summary>Approves a Pending account (R2), or the pending rename of an approved one (four-eyes).</summary>
    public async Task<OperationResult<long>> ApproveAsync(long ledgerAccountId, CancellationToken cancellationToken)
    {
        var approverId = _currentUser.StaffUserId ?? throw new InvalidOperationException("Approval needs a signed-in user.");

        var account = await _db.Set<LedgerAccount>().SingleOrDefaultAsync(row => row.LedgerAccountId == ledgerAccountId, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        if (account.BranchId != _currentUser.BranchId)
        {
            return OperationResult.Fail<long>(ProblemCodes.RecordOtherBranch);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (SupervisionRules.StateOf(account) == SupervisionState.Pending)
        {
            var check = SupervisionRules.CheckApprove(account, approverId, hasSupervisePermission: true, _supervision.AllowSelfApproval);
            if (!check.Allowed)
            {
                return OperationResult.Fail<long>(check.ProblemCode!);
            }

            SupervisionRules.Approve(account, approverId, now);
            _activity.Record(EventCodes.SuperviseApprove, nameof(LedgerAccount), account.LedgerAccountCode);
            return await SaveDecisionAsync(account.LedgerAccountId, cancellationToken).ConfigureAwait(false);
        }

        var request = await PendingRenameQuery(ledgerAccountId).AsTracking().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (request is null)
        {
            return OperationResult.Fail<long>(SupervisionProblems.NotPending);
        }

        if (request.RequestedByUserId == approverId && !_supervision.AllowSelfApproval)
        {
            return OperationResult.Fail<long>(SupervisionProblems.SelfApproval);
        }

        account.AccountName = JsonSerializer.Deserialize<RenamePayload>(request.PayloadJson, Json)!.AccountName;
        account.SupervisedByUserId = approverId;
        account.SupervisedOn = now;
        request.Status = ChangeRequestStatuses.Approved;
        request.DecidedByUserId = approverId;
        request.DecidedOn = now;
        _activity.Record(EventCodes.SuperviseApprove, nameof(LedgerAccount), account.LedgerAccountCode, new Dictionary<string, object?> { ["changeRequestId"] = request.ChangeRequestId });
        return await SaveDecisionAsync(account.LedgerAccountId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Rejects a Pending account (R5: deleted, provided nothing refers to it), or the pending rename of an approved
    /// account (the change request is closed and the account is untouched).
    /// </summary>
    public async Task<OperationResult<long>> RejectAsync(long ledgerAccountId, string? reason, CancellationToken cancellationToken)
    {
        var deciderId = _currentUser.StaffUserId ?? throw new InvalidOperationException("Rejection needs a signed-in user.");

        var account = await _db.Set<LedgerAccount>().SingleOrDefaultAsync(row => row.LedgerAccountId == ledgerAccountId, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        if (account.BranchId != _currentUser.BranchId)
        {
            return OperationResult.Fail<long>(ProblemCodes.RecordOtherBranch);
        }

        if (SupervisionRules.StateOf(account) == SupervisionState.Pending)
        {
            var hasDependents =
                await _db.Set<StaffUser>().AnyAsync(row => row.TellerGLAccountId == ledgerAccountId, cancellationToken).ConfigureAwait(false)
                || await _db.Set<Product>().AnyAsync(row => row.LedgerControlAccountId == ledgerAccountId, cancellationToken).ConfigureAwait(false);

            // A Pending account was never approved: an approved account never returns to Pending (A3-a).
            var check = SupervisionRules.CheckReject(account, hasRejectPermission: true, wasEverSupervised: false, hasDependents);
            if (!check.Allowed)
            {
                return OperationResult.Fail<long>(check.ProblemCode!);
            }

            _db.Set<LedgerAccount>().Remove(account);
            _activity.Record(EventCodes.SuperviseReject, nameof(LedgerAccount), account.LedgerAccountCode, Note(reason));
            return await SaveDecisionAsync(ledgerAccountId, cancellationToken).ConfigureAwait(false);
        }

        var request = await PendingRenameQuery(ledgerAccountId).AsTracking().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (request is null)
        {
            return OperationResult.Fail<long>(SupervisionProblems.NotPending);
        }

        request.Status = ChangeRequestStatuses.Rejected;
        request.DecidedByUserId = deciderId;
        request.DecidedOn = _timeProvider.GetUtcNow().UtcDateTime;
        request.DecisionNote = reason is { Length: > 300 } ? reason[..300] : reason;
        _activity.Record(EventCodes.SuperviseReject, nameof(LedgerAccount), account.LedgerAccountCode, new Dictionary<string, object?> { ["changeRequestId"] = request.ChangeRequestId });
        return await SaveDecisionAsync(ledgerAccountId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<OperationResult<LedgerEditOutcome>> SaveEditAsync(LedgerAccount account, CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok(new LedgerEditOutcome(Applied: true, ChangeRequestId: null));
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.Entry(account).State = EntityState.Detached;
            _activity.Written = false;
            return OperationResult.Fail<LedgerEditOutcome>(ProblemCodes.ConcurrencyConflict);
        }
    }

    private async Task<OperationResult<long>> SaveDecisionAsync(long ledgerAccountId, CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok(ledgerAccountId);
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            _activity.Written = false;
            return OperationResult.Fail<long>(ProblemCodes.ConcurrencyConflict);
        }
    }

    private async Task<Dictionary<string, string[]>> ValidateAsync(LedgerAccountDraft draft, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var name = draft.AccountName?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 100)
        {
            errors["accountName"] = ["Enter a name of up to 100 characters."];
        }

        if (draft.LedgerAccountTypeId is not { } typeId
            || !await _db.Set<LedgerAccountType>().AnyAsync(row => row.Id == typeId && row.IsActive, cancellationToken).ConfigureAwait(false))
        {
            errors["ledgerAccountTypeId"] = ["Choose an account type from the list."];
        }

        if (draft.CurrencyId is not { } currencyId
            || !await _db.Set<Currency>().AnyAsync(row => row.CurrencyId == currencyId, cancellationToken).ConfigureAwait(false))
        {
            errors["currencyId"] = ["Choose a currency from the list."];
        }

        return errors;
    }

    private IQueryable<ChangeRequest> PendingRenameQuery(long ledgerAccountId)
    {
        var key = Key(ledgerAccountId);
        return _db.Set<ChangeRequest>()
            .AsNoTracking()
            .Where(row => row.EntityType == ChangeRequestEntities.LedgerAccount
                          && row.EntityId == key
                          && row.Status == ChangeRequestStatuses.Pending);
    }

    private async Task<List<LedgerAccountView>> WithPendingAsync(List<LedgerAccountView> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return rows;
        }

        var keys = rows.Select(row => Key(row.LedgerAccountId)).ToList();
        var pending = await _db.Set<ChangeRequest>()
            .AsNoTracking()
            .Where(row => row.EntityType == ChangeRequestEntities.LedgerAccount
                          && row.Status == ChangeRequestStatuses.Pending
                          && keys.Contains(row.EntityId))
            .Select(row => new { row.EntityId, row.ChangeRequestId, row.PayloadJson, row.RequestedByUserId, row.RequestedOn })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var byKey = pending.ToDictionary(
            row => row.EntityId,
            row => new PendingLedgerRename(
                row.ChangeRequestId,
                JsonSerializer.Deserialize<RenamePayload>(row.PayloadJson, Json)!.AccountName,
                row.RequestedByUserId,
                row.RequestedOn),
            StringComparer.Ordinal);

        return rows
            .Select(row => byKey.TryGetValue(Key(row.LedgerAccountId), out var change) ? row with { PendingChange = change } : row)
            .ToList();
    }

    private static IQueryable<LedgerAccountView> Project(IQueryable<LedgerAccount> query) =>
        query.Select(row => new LedgerAccountView(
            row.LedgerAccountId,
            row.BranchId,
            row.Branch!.BranchCode,
            row.LedgerAccountCode,
            row.AccountName,
            row.LedgerAccountTypeId,
            row.LedgerAccountTypeId == null ? null : row.LedgerAccountType!.Code,
            row.CurrencyId,
            row.CurrencyId == null ? null : row.Currency!.CurrencyCode,
            row.Balance,
            !row.IsSupervisionRequired
                ? LedgerSupervisionStates.NotRequired
                : row.SupervisedByUserId == null ? LedgerSupervisionStates.Pending : LedgerSupervisionStates.Approved,
            row.CreatedByUserId,
            row.ModifiedByUserId,
            row.SupervisedByUserId,
            row.SupervisedOn,
            null));

    private static Dictionary<string, object?>? Note(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? null : new Dictionary<string, object?> { ["reason"] = reason.Length > 300 ? reason[..300] : reason };

    private static string Key(long ledgerAccountId) => ledgerAccountId.ToString(CultureInfo.InvariantCulture);

    private sealed record RenamePayload(string AccountName);
}
