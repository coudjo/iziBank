namespace Banking.Domain.Security;

/// <summary>
/// Lockout is derived, not stored (plan 5.5): enough failed logins inside the window lock the code until
/// <c>duration</c> after the latest failure.
/// </summary>
public static class LockoutPolicy
{
    /// <param name="recentFailuresUtc">Failures inside the window and after the last successful login.</param>
    /// <returns>Time left on the lock, or null when not locked.</returns>
    public static TimeSpan? RetryAfter(
        IReadOnlyCollection<DateTime> recentFailuresUtc,
        DateTime nowUtc,
        int threshold,
        TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(recentFailuresUtc);

        if (threshold <= 0 || recentFailuresUtc.Count < threshold)
        {
            return null;
        }

        var lockedUntil = recentFailuresUtc.Max() + duration;
        return lockedUntil > nowUtc ? lockedUntil - nowUtc : null;
    }
}
