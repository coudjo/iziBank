using Banking.Application.Abstractions;
using Banking.Application.Errors;
using Banking.Application.Security;
using Banking.Domain.Institution;
using Banking.Domain.Ledger;
using Banking.Domain.Lookups;
using Banking.Domain.Security;
using Banking.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Banking.Infrastructure.Security;

public sealed record StaffAssignment(int BranchId, string BranchCode, string RoleGroupCode, string RoleGroupName);

public sealed record StaffSummary(
    long StaffUserId,
    string StaffCode,
    string FullName,
    int BranchId,
    string BranchCode,
    bool IsDisabled,
    bool CanSwitchBranch,
    bool MustChangeSecretKey);

public sealed record StaffDetail(
    StaffSummary Staff,
    short? SecretKeyExpiryDays,
    IReadOnlyList<StaffAssignment> Assignments,
    bool IsTeller = false,
    long? TellerGLAccountId = null);

public sealed record NewStaff(
    string StaffCode,
    string FullName,
    int BranchId,
    bool CanSwitchBranch,
    string InitialSecretKey,
    bool IsTeller = false,
    long? TellerGLAccountId = null);

public sealed record StaffChanges(
    string FullName,
    bool CanSwitchBranch,
    bool IsDisabled,
    short? SecretKeyExpiryDays,
    bool IsTeller = false,
    long? TellerGLAccountId = null);

/// <summary>Staff user administration (A2-08). Role groups are assigned through <see cref="RoleGroupAdmin"/>.</summary>
public sealed class StaffAdmin
{
    private readonly BankingDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IActivityContext _activity;
    private readonly IPasswordHasher<StaffUser> _hasher;
    private readonly LookupIdCache _lookups;
    private readonly TimeProvider _timeProvider;
    private readonly SessionStore _sessions;
    private readonly RoleGroupAdmin _roleGroups;
    private readonly SecurityOptions _security;

    public StaffAdmin(
        BankingDbContext db,
        ICurrentUser currentUser,
        IActivityContext activity,
        IPasswordHasher<StaffUser> hasher,
        LookupIdCache lookups,
        TimeProvider timeProvider,
        SessionStore sessions,
        RoleGroupAdmin roleGroups,
        IOptions<SecurityOptions> security)
    {
        ArgumentNullException.ThrowIfNull(security);
        _db = db;
        _currentUser = currentUser;
        _activity = activity;
        _hasher = hasher;
        _lookups = lookups;
        _timeProvider = timeProvider;
        _sessions = sessions;
        _roleGroups = roleGroups;
        _security = security.Value;
    }

    public async Task<PagedResult<StaffSummary>> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = _db.Set<StaffUser>().AsNoTracking().Where(row => row.StaffCode != StaffCodes.System);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = "%" + search.Trim().Replace("[", "[[]", StringComparison.Ordinal).Replace("%", "[%]", StringComparison.Ordinal).Replace("_", "[_]", StringComparison.Ordinal) + "%";
            query = query.Where(row => EF.Functions.Like(row.StaffCode, pattern) || EF.Functions.Like(row.FullName, pattern));
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query
            .OrderBy(row => row.StaffCode)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new StaffSummary(
                row.StaffUserId,
                row.StaffCode,
                row.FullName,
                row.BranchId,
                row.Branch!.BranchCode,
                row.IsDisabled,
                row.CanSwitchBranch == true,
                row.ChangeKeyAtNextLogOn == true))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<StaffSummary>(items, page, pageSize, total);
    }

    public async Task<StaffDetail?> GetAsync(long staffUserId, CancellationToken cancellationToken)
    {
        var staff = await _db.Set<StaffUser>()
            .AsNoTracking()
            .Where(row => row.StaffUserId == staffUserId && row.StaffCode != StaffCodes.System)
            .Select(row => new
            {
                Summary = new StaffSummary(
                    row.StaffUserId,
                    row.StaffCode,
                    row.FullName,
                    row.BranchId,
                    row.Branch!.BranchCode,
                    row.IsDisabled,
                    row.CanSwitchBranch == true,
                    row.ChangeKeyAtNextLogOn == true),
                row.SecretKeyExpiryDays,
                row.IsTeller,
                row.TellerGLAccountId
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (staff is null)
        {
            return null;
        }

        var assignments = await _db.Set<StaffUserRoleGroup>()
            .AsNoTracking()
            .Where(row => row.StaffUserId == staffUserId && (!row.IsSupervisionRequired || row.SupervisedByUserId != null))
            .OrderBy(row => row.Branch!.BranchCode)
            .ThenBy(row => row.RoleGroup!.RoleGroupCode)
            .Select(row => new StaffAssignment(row.BranchId, row.Branch!.BranchCode, row.RoleGroup!.RoleGroupCode, row.RoleGroup.RoleGroupName))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new StaffDetail(staff.Summary, staff.SecretKeyExpiryDays, assignments, staff.IsTeller, staff.TellerGLAccountId);
    }

    /// <summary>A new staff member has no role groups and must change the initial password at first sign-in.</summary>
    public async Task<OperationResult<long>> CreateAsync(NewStaff draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var code = draft.StaffCode.Trim().ToUpperInvariant();
        var name = draft.FullName.Trim();
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (code.Length is 0 or > 20 || !code.All(static character => char.IsAsciiLetterUpper(character) || char.IsAsciiDigit(character) || character is '_' or '-'))
        {
            errors["staffCode"] = ["Use 1 to 20 capital letters, digits, dashes or underscores."];
        }

        if (name.Length is 0 or > 100)
        {
            errors["fullName"] = ["Enter a name of up to 100 characters."];
        }

        var rules = SecretKeyRules.Check(draft.InitialSecretKey, code, _security.PasswordMinLength);
        if (rules.Count > 0)
        {
            errors["initialSecretKey"] = [.. rules];
        }

        if (!await _db.Set<Branch>().AnyAsync(row => row.BranchId == draft.BranchId, cancellationToken).ConfigureAwait(false))
        {
            errors["branchId"] = ["Choose an existing branch."];
        }
        else if (await TellerProblemAsync(draft.IsTeller, draft.TellerGLAccountId, draft.BranchId, cancellationToken).ConfigureAwait(false) is { } tellerProblem)
        {
            errors["tellerGLAccountId"] = [tellerProblem];
        }

        if (errors.Count > 0)
        {
            return OperationResult.Fail<long>(ProblemCodes.Validation, errors);
        }

        if (await _db.Set<StaffUser>().AnyAsync(row => row.StaffCode == code, cancellationToken).ConfigureAwait(false))
        {
            return OperationResult.Fail<long>(ProblemCodes.StaffCodeTaken);
        }

        var user = new StaffUser
        {
            StaffCode = code,
            FullName = name,
            BranchId = draft.BranchId,
            CanSwitchBranch = draft.CanSwitchBranch,
            IsTeller = draft.IsTeller,
            TellerGLAccountId = draft.IsTeller ? draft.TellerGLAccountId : null,
            CanChangeSecretKey = true,
            ChangeKeyAtNextLogOn = true,
            SecretKeyChangedOn = _timeProvider.GetUtcNow().UtcDateTime
        };
        user.SecretKeyHash = _hasher.HashPassword(user, draft.InitialSecretKey);

        _db.Set<StaffUser>().Add(user);
        _activity.Record(EventCodes.UserAdmin, nameof(StaffUser), code);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(user.StaffUserId);
    }

    /// <summary>
    /// Disabling a user ends their session at once. Nobody can change their own record here, and the change is
    /// refused if it would leave nobody able to administer the institution.
    /// </summary>
    public async Task<OperationResult<long>> UpdateAsync(long staffUserId, StaffChanges changes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);

        if (staffUserId == _currentUser.StaffUserId)
        {
            return OperationResult.Fail<long>(ProblemCodes.StaffSelfChange);
        }

        var user = await FindAsync(staffUserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        var name = changes.FullName.Trim();
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (name.Length is 0 or > 100)
        {
            errors["fullName"] = ["Enter a name of up to 100 characters."];
        }

        if (await TellerProblemAsync(changes.IsTeller, changes.TellerGLAccountId, user.BranchId, cancellationToken).ConfigureAwait(false) is { } tellerProblem)
        {
            errors["tellerGLAccountId"] = [tellerProblem];
        }

        if (errors.Count > 0)
        {
            return OperationResult.Fail<long>(ProblemCodes.Validation, errors);
        }

        var disabling = changes.IsDisabled && !user.IsDisabled;
        user.FullName = name;
        user.CanSwitchBranch = changes.CanSwitchBranch;
        user.IsDisabled = changes.IsDisabled;
        user.SecretKeyExpiryDays = changes.SecretKeyExpiryDays;
        user.IsTeller = changes.IsTeller;
        user.TellerGLAccountId = changes.IsTeller ? changes.TellerGLAccountId : null;
        _activity.Record(EventCodes.UserAdmin, nameof(StaffUser), user.StaffCode);

        var transaction = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var closed = disabling
                ? await CloseSessionsAsync(staffUserId, LookupCodes.LogoutReasons.AccountDisabled, cancellationToken).ConfigureAwait(false)
                : [];
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (disabling && !await _roleGroups.AdministratorRemainsAsync(cancellationToken).ConfigureAwait(false))
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                _db.ChangeTracker.Clear();
                _activity.Written = false;
                return OperationResult.Fail<long>(ProblemCodes.LastAdministrator);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            closed.ForEach(_sessions.Invalidate);
        }

        return OperationResult.Ok(staffUserId);
    }

    /// <summary>Sets a temporary password the user must change at next sign-in, and ends their open session.</summary>
    public async Task<OperationResult<long>> ResetSecretKeyAsync(long staffUserId, string newSecretKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(newSecretKey);

        if (staffUserId == _currentUser.StaffUserId)
        {
            return OperationResult.Fail<long>(ProblemCodes.StaffSelfChange);
        }

        var user = await FindAsync(staffUserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        var rules = SecretKeyRules.Check(newSecretKey, user.StaffCode, _security.PasswordMinLength);
        if (rules.Count > 0)
        {
            return OperationResult.Fail<long>(
                ProblemCodes.Validation,
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["newSecretKey"] = [.. rules] });
        }

        user.SecretKeyHash = _hasher.HashPassword(user, newSecretKey);
        user.SecretKeyChangedOn = _timeProvider.GetUtcNow().UtcDateTime;
        user.ChangeKeyAtNextLogOn = true;
        _activity.Record(EventCodes.UserAdmin, nameof(StaffUser), user.StaffCode, new Dictionary<string, object?> { ["action"] = "reset-secret-key" });

        var closed = await CloseSessionsAsync(staffUserId, LookupCodes.LogoutReasons.SecretKeyChanged, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        closed.ForEach(_sessions.Invalidate);
        return OperationResult.Ok(staffUserId);
    }

    /// <summary>
    /// Plan 6.11: a teller's till account must be an approved ASSET ledger account in the teller's own branch.
    /// Returns the message for the field, or null when the combination is valid.
    /// </summary>
    private async Task<string?> TellerProblemAsync(bool isTeller, long? tellerGLAccountId, int branchId, CancellationToken cancellationToken)
    {
        if (!isTeller)
        {
            return null;
        }

        if (tellerGLAccountId is not { } accountId)
        {
            return "Choose the teller's till account.";
        }

        var assetId = await _lookups.GetIdAsync<LedgerAccountType>(_db, LookupCodes.LedgerAccountTypes.Asset, cancellationToken).ConfigureAwait(false);
        var account = await _db.Set<LedgerAccount>()
            .AsNoTracking()
            .Where(row => row.LedgerAccountId == accountId)
            .Select(row => new { row.BranchId, row.LedgerAccountTypeId, Approved = !row.IsSupervisionRequired || row.SupervisedByUserId != null })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return account is null || account.LedgerAccountTypeId != assetId || account.BranchId != branchId || !account.Approved
            ? "Choose an approved asset ledger account in the teller's own branch."
            : null;
    }

    private Task<StaffUser?> FindAsync(long staffUserId, CancellationToken cancellationToken) =>
        _db.Set<StaffUser>().SingleOrDefaultAsync(
            row => row.StaffUserId == staffUserId && row.StaffCode != StaffCodes.System,
            cancellationToken);

    private async Task<List<long>> CloseSessionsAsync(long staffUserId, string reasonCode, CancellationToken cancellationToken)
    {
        var open = await _db.Set<UserSession>()
            .Where(row => row.StaffUserId == staffUserId && row.LogoutAt == null)
            .Select(row => row.SessionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var sessionId in open)
        {
            await SessionStore.CloseInContextAsync(_db, _lookups, _timeProvider, sessionId, reasonCode, cancellationToken)
                .ConfigureAwait(false);
        }

        return open;
    }
}
