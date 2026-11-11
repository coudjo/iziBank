using Banking.Domain.Interest;
using Banking.Domain.Lookups;

namespace Banking.Domain.Tests.Interest;

/// <summary>B5-04: calendar-aligned periods, rounding by the institution's mode, and withholding tax.</summary>
public sealed class CapitalizationRulesTests
{
    [Theory]
    [InlineData(LookupCodes.CapitalizationFrequencies.Monthly, "2026-02-10", "2026-02-28")]
    [InlineData(LookupCodes.CapitalizationFrequencies.Monthly, "2028-02-10", "2028-02-29")]
    [InlineData(LookupCodes.CapitalizationFrequencies.Quarterly, "2026-05-15", "2026-06-30")]
    [InlineData(LookupCodes.CapitalizationFrequencies.Quarterly, "2026-01-01", "2026-03-31")]
    [InlineData(LookupCodes.CapitalizationFrequencies.HalfYearly, "2026-03-01", "2026-06-30")]
    [InlineData(LookupCodes.CapitalizationFrequencies.HalfYearly, "2026-07-01", "2026-12-31")]
    [InlineData(LookupCodes.CapitalizationFrequencies.Yearly, "2026-07-01", "2026-12-31")]
    public void A_period_ends_on_the_last_day_of_its_calendar_block(string frequency, string day, string expected)
    {
        CapitalizationRules.PeriodEnd(frequency, DateOnly.Parse(day, System.Globalization.CultureInfo.InvariantCulture))
            .ShouldBe(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void An_unknown_or_missing_frequency_does_not_capitalize()
    {
        CapitalizationRules.PeriodEnd(null, new DateOnly(2026, 5, 1)).ShouldBeNull();
        CapitalizationRules.PeriodEnd("WEEKLY", new DateOnly(2026, 5, 1)).ShouldBeNull();
    }

    [Theory]
    [InlineData(0.125, "HALF_AWAY_FROM_ZERO", 0.13)]
    [InlineData(-0.125, "HALF_AWAY_FROM_ZERO", -0.13)]
    [InlineData(0.125, "HALF_EVEN", 0.12)]
    [InlineData(0.135, "HALF_EVEN", 0.14)]
    [InlineData(0.129, "DOWN", 0.12)]
    [InlineData(0.126, "HALF_AWAY_FROM_ZERO", 0.13)]
    public void Money_rounds_to_two_places_by_the_configured_mode(double amount, string rounding, double expected)
    {
        CapitalizationRules.Round((decimal)amount, rounding).ShouldBe((decimal)expected);
    }

    [Fact]
    public void Withholding_tax_is_taken_from_the_gross_and_nothing_is_withheld_at_rate_zero()
    {
        CapitalizationRules.WithholdingTax(100m, 10m, "HALF_AWAY_FROM_ZERO").ShouldBe(10m);
        CapitalizationRules.WithholdingTax(33.33m, 15m, "HALF_AWAY_FROM_ZERO").ShouldBe(5m);
        CapitalizationRules.WithholdingTax(100m, 0m, "HALF_AWAY_FROM_ZERO").ShouldBe(0m);
        CapitalizationRules.WithholdingTax(0m, 10m, "HALF_AWAY_FROM_ZERO").ShouldBe(0m);
    }
}
