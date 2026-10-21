using Banking.Application.Abstractions;
using Banking.Application.Errors;
using Banking.Application.Security;
using Banking.Domain.Institution;
using Banking.Domain.Lookups;
using Banking.Domain.Security;
using Banking.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Banking.Infrastructure.Security;

public sealed record SessionInfo(
    long SessionId,
    long StaffUserId,
    int BranchId,
    DateTime LoginAtUtc,
    bool MustChangeSecretKey);

public sealed record AuthResult(
    SessionInfo? Session,
    string? ProblemCode,
    int? RetryAfterSeconds = null,
    IReadOnlyList<string>? RuleFailures = null)
{
    public bool Succeeded => Session is not null;

    public static AuthResult Fail(string problemCode) => new(null, problemCode);
}

public sealed record BranchSummary(int BranchId, string BranchCode, string BranchName);

public sealed record StaffProfile(
    long StaffUserId,
    string StaffCode,
    string FullName,
    int HomeBranchId,
    int EffectiveBranchId,
    bool CanSwitchBranch,
    IReadOnlyList<BranchSummary> Branches,
    IReadOnlyList<PermissionGrant> Permissions);

/// <summary>Login, refresh, logout, change secret key (plan 5.5, task A2-04).</summary>
public sealed class AuthService
{
    /// <summary>Failed-login and login rows carry the attempted code here so lockout can be derived.</summary>
    public const string StaffCodeEntity = "StaffCode";

    private readonly BankingDbContext _db;
    private static readonly StaffUser TimingUser = new();
    private static string? s_timingHash;

    private readonly IPasswordHasher<StaffUser> _hasher;
    private readonly TimeProvider _timeProvider;
    private readonly SecurityOptions _security;
    private readonly LookupIdCache _lookups;
    private readonly ActivityLogWriter _activityWriter;
    private readonly IActivityContext _activity;
    private readonly PermissionResolver _permissions;
    private readonly SessionStore _sessions;

    public AuthService(
        BankingDbContext db,
        IPasswordHasher<StaffUser> hasher,
        TimeProvider timeProvider,
        IOptions<SecurityOptions> security,
        LookupIdCache lookups,
        ActivityLogWriter activityWriter,
        IActivityContext activity,
        PermissionResolver permissions,
        SessionStore sessions)
    {
        ArgumentNullException.ThrowIfNull(security);
        _db = db;
        _hasher = hasher;
        _timeProvider = timeProvider;
        _security = security.Value;
        _lookups = lookups;
        _activityWriter = activityWriter;
        _activity = activity;
        _permissions = permissions;
        _sessions = sessions;
    }

    public async Task<AuthResult> LoginAsync(
        string staffCode,
        string secretKey,
        int? requestedBranchId,
        string? ipAddress,
        string? deviceInfo,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(staffCode);
        ArgumentNullException.ThrowIfNull(secretKey);

        var code = staffCode.Trim().ToUpperInvariant();
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _activity.Written = true;

        var retryAfter = await LockoutRemainingAsync(code, now, cancellationToken).ConfigureAwait(false);
        if (retryAfter is not null)
        {
            await _activityWriter.WriteAsync(
                    new ActivityEntry(EventCodes.PermissionDenied, ActivityOutcomes.Denied, null, null, StaffCodeEntity, code,
                        new Dictionary<string, object?> { ["reason"] = "locked" }),
                    cancellationToken)
                .ConfigureAwait(false);
            return new AuthResult(null, ProblemCodes.Locked, (int)Math.Ceiling(retryAfter.Value.TotalSeconds));
        }

        var user = await _db.Set<StaffUser>()
            .SingleOrDefaultAsync(row => row.StaffCode == code, cancellationToken)
            .ConfigureAwait(false);

        var verified = user is { IsDisabled: false, SecretKeyHash: not null }
            ? _hasher.VerifyHashedPassword(user, user.SecretKeyHash, secretKey)
            : RejectAtEqualCost(secretKey);

        if (user is null || verified == PasswordVerificationResult.Failed)
        {
            await _activityWriter.WriteAsync(
                    new ActivityEntry(EventCodes.LoginFailed, ActivityOutcomes.Denied, null, null, StaffCodeEntity, code),
                    cancellationToken)
                .ConfigureAwait(false);
            return AuthResult.Fail(ProblemCodes.InvalidCredentials);
        }

        var branchId = requestedBranchId ?? user.BranchId;
        if (branchId != user.BranchId
            && (user.CanSwitchBranch != true
                || !await _permissions.HasAssignmentInBranchAsync(user.StaffUserId, branchId, cancellationToken).ConfigureAwait(false)))
        {
            await _activityWriter.WriteAsync(
                    new ActivityEntry(EventCodes.PermissionDenied, ActivityOutcomes.Denied, null, branchId, StaffCodeEntity, code,
                        new Dictionary<string, object?> { ["reason"] = "branch" }),
                    cancellationToken)
                .ConfigureAwait(false);
            return AuthResult.Fail(ProblemCodes.BranchNotAllowed);
        }

        if (verified == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.SecretKeyHash = _hasher.HashPassword(user, secretKey);
        }

        var mustChange = user.ChangeKeyAtNextLogOn == true
            || SecretKeyRules.IsExpired(user.SecretKeyChangedOn, user.SecretKeyExpiryDays, now);

        var transaction = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var openSessionIds = await _db.Set<UserSession>()
                .Where(row => row.StaffUserId == user.StaffUserId && row.LogoutAt == null)
                .Select(row => row.SessionId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var openSessionId in openSessionIds)
            {
                await SessionStore.CloseInContextAsync(
                        _db, _lookups, _timeProvider, openSessionId, LookupCodes.LogoutReasons.SupersededByNewLogin, cancellationToken)
                    .ConfigureAwait(false);
            }

            var session = new UserSession
            {
                StaffUserId = user.StaffUserId,
                LoginAt = now,
                IPAddress = Truncate(ipAddress, 45),
                DeviceInfo = Truncate(deviceInfo, 200)
            };
            _db.Set<UserSession>().Add(session);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await _activityWriter.AddAsync(
                    _db,
                    new ActivityEntry(EventCodes.Login, ActivityOutcomes.Success, session.SessionId, branchId, StaffCodeEntity, code,
                        new Dictionary<string, object?> { ["mustChangeSecretKey"] = mustChange }),
                    cancellationToken)
                .ConfigureAwait(false);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            foreach (var openSessionId in openSessionIds)
            {
                _sessions.Invalidate(openSessionId);
            }

            return new AuthResult(new SessionInfo(session.SessionId, user.StaffUserId, branchId, now, mustChange), null);
        }
    }

    public async Task<SessionInfo> RefreshAsync(long sessionId, long staffUserId, int branchId, CancellationToken cancellationToken)
    {
        var session = await _db.Set<UserSession>()
            .AsNoTracking()
            .SingleAsync(row => row.SessionId == sessionId && row.StaffUserId == staffUserId, cancellationToken)
            .ConfigureAwait(false);
        var user = await _db.Set<StaffUser>()
            .AsNoTracking()
            .SingleAsync(row => row.StaffUserId == staffUserId, cancellationToken)
            .ConfigureAwait(false);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var mustChange = user.ChangeKeyAtNextLogOn == true
            || SecretKeyRules.IsExpired(user.SecretKeyChangedOn, user.SecretKeyExpiryDays, now);

        return new SessionInfo(sessionId, staffUserId, branchId, session.LoginAt, mustChange);
    }

    public async Task LogoutAsync(long sessionId, CancellationToken cancellationToken)
    {
        _activity.Record(EventCodes.Logout, nameof(UserSession), sessionId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await SessionStore.CloseInContextAsync(
                _db, _lookups, _timeProvider, sessionId, LookupCodes.LogoutReasons.UserInitiated, cancellationToken)
            .ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _sessions.Invalidate(sessionId);
    }

    public async Task<AuthResult> ChangeSecretKeyAsync(
        long sessionId,
        long staffUserId,
        int branchId,
        string currentSecretKey,
        string newSecretKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currentSecretKey);
        ArgumentNullException.ThrowIfNull(newSecretKey);

        var user = await _db.Set<StaffUser>()
            .SingleAsync(row => row.StaffUserId == staffUserId, cancellationToken)
            .ConfigureAwait(false);

        if (user.SecretKeyHash is null
            || _hasher.VerifyHashedPassword(user, user.SecretKeyHash, currentSecretKey) == PasswordVerificationResult.Failed)
        {
            return AuthResult.Fail(ProblemCodes.CurrentSecretKeyWrong);
        }

        var failures = SecretKeyRules.Check(newSecretKey, user.StaffCode, _security.PasswordMinLength).ToList();
        if (string.Equals(currentSecretKey, newSecretKey, StringComparison.Ordinal))
        {
            failures.Add("same-as-current");
        }

        if (failures.Count > 0)
        {
            return new AuthResult(null, ProblemCodes.SecretKeyRules, RuleFailures: failures);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        user.SecretKeyHash = _hasher.HashPassword(user, newSecretKey);
        user.SecretKeyChangedOn = now;
        user.ChangeKeyAtNextLogOn = false;

        _activity.Record(EventCodes.SecretKeyChanged, nameof(StaffUser), staffUserId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var loginAt = await _db.Set<UserSession>()
            .AsNoTracking()
            .Where(row => row.SessionId == sessionId)
            .Select(row => row.LoginAt)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        return new AuthResult(new SessionInfo(sessionId, staffUserId, branchId, loginAt, MustChangeSecretKey: false), null);
    }

    public async Task<StaffProfile> GetProfileAsync(long staffUserId, int effectiveBranchId, CancellationToken cancellationToken)
    {
        var user = await _db.Set<StaffUser>()
            .AsNoTracking()
            .SingleAsync(row => row.StaffUserId == staffUserId, cancellationToken)
            .ConfigureAwait(false);

        var assignedBranchIds = await _db.Set<StaffUserRoleGroup>()
            .AsNoTracking()
            .Where(row => row.StaffUserId == staffUserId && (!row.IsSupervisionRequired || row.SupervisedByUserId != null))
            .Select(row => row.BranchId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var visible = user.CanSwitchBranch == true
            ? assignedBranchIds.Append(user.BranchId).Distinct().ToList()
            : [user.BranchId];

        var branches = await _db.Set<Branch>()
            .AsNoTracking()
            .Where(row => visible.Contains(row.BranchId))
            .OrderBy(row => row.BranchCode)
            .Select(row => new BranchSummary(row.BranchId, row.BranchCode, row.BranchName))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var permissions = await _permissions.ResolveAsync(staffUserId, effectiveBranchId, cancellationToken).ConfigureAwait(false);

        return new StaffProfile(
            user.StaffUserId,
            user.StaffCode,
            user.FullName,
            user.BranchId,
            effectiveBranchId,
            user.CanSwitchBranch == true,
            branches,
            permissions.Merged());
    }

    /// <summary>
    /// Unknown, disabled and password-less staff codes still pay for one hash check, so the response time does not
    /// reveal which staff codes exist (A6-02: login errors indistinguishable).
    /// </summary>
    private PasswordVerificationResult RejectAtEqualCost(string secretKey)
    {
        var dummy = Volatile.Read(ref s_timingHash);
        if (dummy is null)
        {
            dummy = _hasher.HashPassword(TimingUser, Guid.NewGuid().ToString("N"));
            Volatile.Write(ref s_timingHash, dummy);
        }

        _ = _hasher.VerifyHashedPassword(TimingUser, dummy, secretKey);
        return PasswordVerificationResult.Failed;
    }

    private async Task<TimeSpan?> LockoutRemainingAsync(string code, DateTime now, CancellationToken cancellationToken)
    {
        var loginId = await _lookups.GetEventTypeIdAsync(_db, EventCodes.Login, cancellationToken).ConfigureAwait(false);
        var failedId = await _lookups.GetEventTypeIdAsync(_db, EventCodes.LoginFailed, cancellationToken).ConfigureAwait(false);
        var windowStart = now.AddMinutes(-_security.LockoutWindowMinutes);

        var lastSuccess = await _db.Set<UserActivityLog>()
            .AsNoTracking()
            .Where(row => row.EventTypeId == loginId && row.EntityId == code)
            .MaxAsync(row => (DateTime?)row.EventTimestamp, cancellationToken)
            .ConfigureAwait(false);

        var from = lastSuccess is not null && lastSuccess > windowStart ? lastSuccess.Value : windowStart;

        var failures = await _db.Set<UserActivityLog>()
            .AsNoTracking()
            .Where(row => row.EventTypeId == failedId && row.EntityId == code && row.EventTimestamp > from)
            .Select(row => row.EventTimestamp)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return LockoutPolicy.RetryAfter(
            failures,
            now,
            _security.LockoutThreshold,
            TimeSpan.FromMinutes(_security.LockoutDurationMinutes));
    }

    private static string? Truncate(string? value, int length) =>
        value is null ? null : value.Length <= length ? value : value[..length];
}
