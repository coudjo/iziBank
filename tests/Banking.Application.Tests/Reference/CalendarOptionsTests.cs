using Banking.Application.Reference;

namespace Banking.Application.Tests.Reference;

public sealed class CalendarOptionsTests
{
    [Fact]
    public void Saturday_and_Sunday_are_the_default_weekend()
    {
        new CalendarOptions().ParseWeekendDays().ShouldBe([DayOfWeek.Saturday, DayOfWeek.Sunday], ignoreOrder: true);
    }

    [Fact]
    public void Configured_days_replace_the_default()
    {
        var options = new CalendarOptions { WeekendDays = ["friday", " Saturday "] };
        options.ParseWeekendDays().ShouldBe([DayOfWeek.Friday, DayOfWeek.Saturday], ignoreOrder: true);
    }

    [Theory]
    [InlineData("Funday")]
    [InlineData("3")]
    public void An_unknown_day_stops_start_up(string day)
    {
        var options = new CalendarOptions { WeekendDays = [day] };
        Should.Throw<InvalidOperationException>(() => options.ParseWeekendDays());
    }
}
