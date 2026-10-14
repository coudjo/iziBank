using System.Globalization;

namespace Banking.Application.Reference;

/// <summary>Configuration section <c>Calendar</c>.</summary>
public sealed class CalendarOptions
{
    public const string SectionName = "Calendar";

    private static readonly DayOfWeek[] DefaultWeekend = [DayOfWeek.Saturday, DayOfWeek.Sunday];

    /// <summary>
    /// English day names that are never working days. Null (not configured) means Saturday and Sunday.
    /// No default is set here because the configuration binder appends to an existing array instead of replacing it.
    /// </summary>
#pragma warning disable CA1819 // Bound from configuration as an array.
    public string[]? WeekendDays { get; set; }
#pragma warning restore CA1819

    public IReadOnlySet<DayOfWeek> ParseWeekendDays()
    {
        if (WeekendDays is null)
        {
            return new HashSet<DayOfWeek>(DefaultWeekend);
        }

        var days = new HashSet<DayOfWeek>();
        foreach (var part in WeekendDays.Select(static value => value.Trim()).Where(static value => value.Length > 0))
        {
            if (!Enum.TryParse<DayOfWeek>(part, ignoreCase: true, out var day) || !Enum.IsDefined(day) || part.All(char.IsAsciiDigit))
            {
                throw new InvalidOperationException(
                    string.Create(CultureInfo.InvariantCulture, $"Calendar:WeekendDays has an unknown day '{part}'."));
            }

            days.Add(day);
        }

        return days;
    }
}
