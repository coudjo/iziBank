using Banking.Domain.Interest;
using Banking.Domain.Lookups;

namespace Banking.Domain.Tests.Interest;

/// <summary>B5-03: the daily accrual arithmetic, band selection and day count (plan 6.9).</summary>
public sealed class InterestRulesTests
{
    [Fact]
    public void Act_365_is_the_default_year_and_act_360_is_selectable()
    {
        InterestRules.YearLength(null).ShouldBe(365m);
        InterestRules.YearLength(LookupCodes.DayCounts.Act365).ShouldBe(365m);
        InterestRules.YearLength(LookupCodes.DayCounts.Act360).ShouldBe(360m);
    }

    [Fact]
    public void A_day_accrues_on_the_balance_at_the_rate_over_the_year_to_six_decimals()
    {
        InterestRules.DailyAmount(1000m, 5m, 365m).ShouldBe(0.136986m);
        InterestRules.DailyAmount(1000m, 5m, 360m).ShouldBe(0.138889m);
        InterestRules.DailyAmount(1m, 1m, 365m).ShouldBe(0.000027m);
    }

    [Fact]
    public void Nothing_accrues_on_a_zero_or_negative_balance_or_a_zero_rate()
    {
        InterestRules.DailyAmount(-250m, 5m, 365m).ShouldBe(0m);
        InterestRules.DailyAmount(0m, 5m, 365m).ShouldBe(0m);
        InterestRules.DailyAmount(1000m, 0m, 365m).ShouldBe(0m);
    }

    [Fact]
    public void The_band_covering_the_balance_gives_the_rate_and_open_ends_are_unbounded()
    {
        var bands = new[]
        {
            new InterestBand(null, 999.99m, 1m),
            new InterestBand(1000m, null, 3m)
        };

        InterestRules.BandFor(500m, bands).ShouldNotBeNull().InterestRate.ShouldBe(1m);
        InterestRules.BandFor(1000m, bands).ShouldNotBeNull().InterestRate.ShouldBe(3m);
        InterestRules.BandFor(2_000_000m, bands).ShouldNotBeNull().InterestRate.ShouldBe(3m);
    }

    [Fact]
    public void A_balance_outside_every_band_has_no_rate()
    {
        var bands = new[] { new InterestBand(100m, 500m, 4m) };

        InterestRules.BandFor(50m, bands).ShouldBeNull();
        InterestRules.BandFor(501m, bands).ShouldBeNull();
    }
}
