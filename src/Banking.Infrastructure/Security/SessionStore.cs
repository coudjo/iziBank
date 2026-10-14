using Banking.Application.Errors;
using Banking.Application.Security;
using Banking.Domain.Lookups;
using Banking.Domain.Security;
using Banking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Banking.Infrastructure.Security;

public sealed record SessionCheck(bool IsOpen, string? ProblemCode)
{
    public static readonly SessionCheck Open = new(true, null);
}

/// <summary>
/// Per-request session check (plan 5.5). The open/closed state is cached for at most
/// <see cref="SecurityOptions.CacheSeconds"/>, so a forced logout takes effect within that window.
/// Logout, supersede and force in this process clear the cache entry immediately.
/// </summary>
public sealed class SessionStore
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IMemoryCache _cache;
    private readonly TimeProvider _timeProvider;
    private readonly SecurityOptions _security;
    private readonly JwtOptions _jwt;

    public SessionStore(
        IServiceScopeFactory scopes,
        IMemoryCache cache,
        TimeProvider timeProvider,
        IOptions<SecurityOptions> security,
        IOptions<JwtOptions> jwt)
    {
        ArgumentNullException.ThrowIfNull(security);
        ArgumentNullException.ThrowIfNull(jwt);
        _scopes = scopes;
        _cache = cache;
        _timeProvider = timeProvider;
        _security = security.Value;
        _jwt = jwt.Value;
    }

    public static string ProblemFor(string? logoutReasonCode) => logoutReasonCode switch
    {
        LookupCodes.LogoutReasons.SupersededByNewLogin => ProblemCodes.SessionSuperseded,
        LookupCodes.LogoutReasons.IdleTimeout => ProblemCodes.SessionTimedOut,
        LookupCodes.LogoutReasons.MaxSessionExceeded => ProblemCodes.SessionMaxExceeded,
        LookupCodes.LogoutReasons.ForcedByAdmin => ProblemCodes.SessionForced,
        _ => ProblemCodes.SessionEnded
    };

    public async Task<SessionCheck> CheckAsync(long sessionId, long staffUserId, CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (snapshot is null || snapshot.StaffUserId != staffUserId)
        {
            return new SessionCheck(false, ProblemCodes.SessionEnded);
        }

        if (snapshot.LogoutAt is not null)
        {
            return new SessionCheck(false, ProblemFor(snapshot.LogoutReasonCode));
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (snapshot.LoginAt.AddHours(_jwt.MaxSessionHours) <= now)
        {
            await CloseAsync(sessionId, LookupCodes.LogoutReasons.MaxSessionExceeded, cancellationToken).ConfigureAwait(false);
            return new SessionCheck(false, ProblemCodes.SessionMaxExceeded);
        }

        return SessionCheck.Open;
    }

    public void Invalidate(long sessionId) => _cache.Remove(Key(sessionId));

    /// <summary>Closes one open session in its own scope. Returns false when it was already closed.</summary>
    public async Task<bool> CloseAsync(long sessionId, string reasonCode, CancellationToken cancellationToken)
    {
        var scope = _scopes.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var db = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        var lookups = scope.ServiceProvider.GetRequiredService<LookupIdCache>();

        var closed = await CloseInContextAsync(db, lookups, _timeProvider, sessionId, reasonCode, cancellationToken)
            .ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Invalidate(sessionId);
        return closed;
    }

    /// <summary>Marks the session closed in <paramref name="db"/> without saving.</summary>
    public static async Task<bool> CloseInContextAsync(
        BankingDbContext db,
        LookupIdCache lookups,
        TimeProvider timeProvider,
        long sessionId,
        string reasonCode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(lookups);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var session = await db.Set<UserSession>()
            .SingleOrDefaultAsync(row => row.SessionId == sessionId, cancellationToken)
            .ConfigureAwait(false);

        if (session is null || session.LogoutAt is not null)
        {
            return false;
        }

        session.LogoutAt = timeProvider.GetUtcNow().UtcDateTime;
        session.LogoutReasonId = await lookups.GetIdAsync<LogoutReasonType>(db, reasonCode, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    private async Task<SessionSnapshot?> GetSnapshotAsync(long sessionId, CancellationToken cancellationToken)
    {
        var key = Key(sessionId);
        if (_cache.TryGetValue(key, out SessionSnapshot? cached))
        {
            return cached;
        }

        var scope = _scopes.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var db = scope.ServiceProvider.GetRequiredService<BankingDbContext>();

        var snapshot = await db.Set<UserSession>()
            .AsNoTracking()
            .Where(row => row.SessionId == sessionId)
            .Select(row => new SessionSnapshot(
                row.StaffUserId,
                row.LoginAt,
                row.LogoutAt,
                row.LogoutReason == null ? null : row.LogoutReason.Code))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        _cache.Set(key, snapshot, TimeSpan.FromSeconds(_security.CacheSeconds));
        return snapshot;
    }

    private static string Key(long sessionId) => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"session:{sessionId}");

    private sealed record SessionSnapshot(long? StaffUserId, DateTime LoginAt, DateTime? LogoutAt, string? LogoutReasonCode);
}
