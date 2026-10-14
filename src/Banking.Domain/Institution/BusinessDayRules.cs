namespace Banking.Domain.Institution;

/// <summary>Why a business day cannot be opened (plan 6.12).</summary>
public enum OpenDayRefusal
{
    None,
    PreviousDayNotClosed,
    DateNotAfterPrevious,
    NonWorkingDay
}

/// <summary>The latest business day of a branch: its date and whether it is closed.</summary>
public readonly record struct LatestBusinessDay(DateOnly WorkingDate, bool IsClosed);

/// <summary>Plan 6.12. Pure rules; the caller supplies the branch's latest day, weekend days and holidays.</summary>
public static class BusinessDayRules
{
    public static bool IsWorkingDay(DateOnly date, IReadOnlySet<DayOfWeek> weekendDays, bool isHoliday)
    {
        ArgumentNullException.ThrowIfNull(weekendDays);
        return !isHoliday && !weekendDays.Contains(date.DayOfWeek);
    }

    /// <summary>
    /// A day opens only when the previous day for the branch is closed (or there is none), the date is after it,
    /// and the date is neither a weekend day nor a defined holiday. Checked in that order.
    /// </summary>
    public static OpenDayRefusal CheckOpen(
        DateOnly date,
        LatestBusinessDay? latest,
        IReadOnlySet<DayOfWeek> weekendDays,
        bool isHoliday)
    {
        if (latest is { IsClosed: false })
        {
            return OpenDayRefusal.PreviousDayNotClosed;
        }

        if (latest is { } previous && date <= previous.WorkingDate)
        {
            return OpenDayRefusal.DateNotAfterPrevious;
        }

        return IsWorkingDay(date, weekendDays, isHoliday) ? OpenDayRefusal.None : OpenDayRefusal.NonWorkingDay;
    }
}
