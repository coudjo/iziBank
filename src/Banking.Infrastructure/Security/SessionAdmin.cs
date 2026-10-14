using Banking.Application.Security;
using Banking.Domain.Lookups;
using Banking.Domain.Security;
using Banking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Banking.Infrastructure.Security;

public sealed record SessionRow(
    long SessionId,
    long? StaffUserId,
    string? StaffCode,
    string? FullName,
    DateTime LoginAt,
    DateTime? LogoutAt,
    string? LogoutReason,
    string? IpAddress,
    string? DeviceInfo);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

/// <summary>Session list, forced logout and the idle sweeper (plan 5.5).</summary>
public sealed class SessionAdmin
{
    private readonly BankingDbContext _db;
    private readonly LookupIdCache _lookups;
    private readonly TimeProvider _timeProvider;
    private readonly SessionStore _sessions;
    private readonly JwtOptions _jwt;

    public SessionAdmin(
        BankingDbContext db,
        LookupIdCache lookups,
        TimeProvider timeProvider,
        SessionStore sessions,
        IOptions<JwtOptions> jwt)
    {
        ArgumentNullException.ThrowIfNull(jwt);
        _db = db;
        _lookups = lookups;
        _timeProvider = timeProvider;
        _sessions = sessions;
        _jwt = jwt.Value;
    }

    public async Task<PagedResult<SessionRow>> ListAsync(bool openOnly, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = _db.Set<UserSession>().AsNoTracking().Where(row => row.StaffUserId != null);
        if (openOnly)
        {
            query = query.Where(row => row.LogoutAt == null);
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query
            .OrderByDescending(row => row.LoginAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new SessionRow(
                row.SessionId,
                row.StaffUserId,
                row.StaffUser == null ? null : row.StaffUser.StaffCode,
                row.StaffUser == null ? null : row.StaffUser.FullName,
                row.LoginAt,
                row.LogoutAt,
                row.LogoutReason == null ? null : row.LogoutReason.Code,
                row.IPAddress,
                row.DeviceInfo))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<SessionRow>(items, page, pageSize, total);
    }

    /// <summary>Returns false when the session does not exist or is already closed.</summary>
    public async Task<bool> ForceEndAsync(long sessionId, CancellationToken cancellationToken)
    {
        var closed = await SessionStore.CloseInContextAsync(
                _db, _lookups, _timeProvider, sessionId, LookupCodes.LogoutReasons.ForcedByAdmin, cancellationToken)
            .ConfigureAwait(false);

        if (!closed)
        {
            return false;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _sessions.Invalidate(sessionId);
        return true;
    }

    /// <summary>
    /// Closes open sessions idle for longer than the access-token lifetime (IDLE_TIMEOUT) or older than the maximum
    /// session length (MAX_SESSION_EXCEEDED). Idle time is measured from the latest activity row of the session.
    /// </summary>
    public async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var idleCutoff = now.AddMinutes(-_jwt.AccessTokenMinutes);
        var maxCutoff = now.AddHours(-_jwt.MaxSessionHours);

        var candidates = await _db.Set<UserSession>()
            .AsNoTracking()
            .Where(row => row.LogoutAt == null && row.StaffUserId != null)
            .Select(row => new
            {
                row.SessionId,
                row.LoginAt,
                LastActivity = _db.Set<UserActivityLog>()
                    .Where(activity => activity.SessionId == row.SessionId)
                    .Max(activity => (DateTime?)activity.EventTimestamp)
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var closed = new List<long>();
        foreach (var candidate in candidates)
        {
            string? reason = null;
            if (candidate.LoginAt <= maxCutoff)
            {
                reason = LookupCodes.LogoutReasons.MaxSessionExceeded;
            }
            else if ((candidate.LastActivity ?? candidate.LoginAt) <= idleCutoff)
            {
                reason = LookupCodes.LogoutReasons.IdleTimeout;
            }

            if (reason is not null
                && await SessionStore.CloseInContextAsync(_db, _lookups, _timeProvider, candidate.SessionId, reason, cancellationToken)
                    .ConfigureAwait(false))
            {
                closed.Add(candidate.SessionId);
            }
        }

        if (closed.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            closed.ForEach(_sessions.Invalidate);
        }

        return closed.Count;
    }
}
