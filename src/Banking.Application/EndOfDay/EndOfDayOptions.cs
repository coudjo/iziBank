namespace Banking.Application.EndOfDay;

/// <summary>
/// B6: the scheduled end-of-day run. Off unless configured: set <c>EndOfDay:Enabled</c> to true in the environment's settings.
/// <see cref="RunAtUtc"/> is the time of day (UTC, "HH:mm") from which each branch's working date is run, once.
/// </summary>
public sealed class EndOfDayOptions
{
    public const string SectionName = "EndOfDay";

    public bool Enabled { get; init; }

    public string RunAtUtc { get; init; } = "23:30";
}
