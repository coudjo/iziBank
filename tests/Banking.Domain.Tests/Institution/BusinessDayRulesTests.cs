using Banking.Domain.Institution;

namespace Banking.Domain.Tests.Institution;

/// <summary>A3-02 (plan 6.12): weekend, holiday, previous-day-open and date-order rules.</summary>
public sealed class BusinessDayRulesTests
{
    private static readonly HashSet<DayOfWeek> Weekend = [DayOfWeek.Saturday, DayOfWeek.Sunday];
    private static readonly DateOnly Wednesday = new(2026, 10, 14);
    private static readonly DateOnly Friday = new(2026, 10, 16);
    private static readonly DateOnly Saturday = new(2026, 10, 17);
    private static readonly DateOnly Monday = new(2026, 10, 19);

    [Fact]
    public void The_first_day_of_a_branch_opens_on_a_working_day()
    {
        BusinessDayRules.CheckOpen(Wednesday, latest: null, Weekend, isHoliday: false).ShouldBe(OpenDayRefusal.None);
    }

    [Fact]
    public void A_weekend_day_is_refused()
    {
        BusinessDayRules.CheckOpen(Saturday, new LatestBusinessDay(Friday, IsClosed: true), Weekend, isHoliday: false)
            .ShouldBe(OpenDayRefusal.NonWorkingDay);
    }

    [Fact]
    public void A_holiday_is_refused()
    {
        BusinessDayRules.CheckOpen(Monday, new LatestBusinessDay(Friday, IsClosed: true), Weekend, isHoliday: true)
            .ShouldBe(OpenDayRefusal.NonWorkingDay);
    }

    [Fact]
    public void The_next_day_waits_until_the_previous_one_is_closed()
    {
        BusinessDayRules.CheckOpen(Monday, new LatestBusinessDay(Friday, IsClosed: false), Weekend, isHoliday: false)
            .ShouldBe(OpenDayRefusal.PreviousDayNotClosed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void The_date_must_come_after_the_last_day(int offsetDays)
    {
        BusinessDayRules.CheckOpen(Friday.AddDays(offsetDays), new LatestBusinessDay(Friday, IsClosed: true), Weekend, isHoliday: false)
            .ShouldBe(OpenDayRefusal.DateNotAfterPrevious);
    }

    [Fact]
    public void Days_may_be_skipped_after_a_closed_day()
    {
        BusinessDayRules.CheckOpen(Monday, new LatestBusinessDay(Wednesday, IsClosed: true), Weekend, isHoliday: false)
            .ShouldBe(OpenDayRefusal.None);
    }

    [Fact]
    public void A_configured_weekend_is_respected()
    {
        HashSet<DayOfWeek> fridayWeekend = [DayOfWeek.Friday];
        BusinessDayRules.IsWorkingDay(Friday, fridayWeekend, isHoliday: false).ShouldBeFalse();
        BusinessDayRules.IsWorkingDay(Saturday, fridayWeekend, isHoliday: false).ShouldBeTrue();
    }
}
