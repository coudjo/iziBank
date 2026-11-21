using Banking.Domain.Charges;
using Banking.Domain.Lookups;

namespace Banking.Domain.Tests.Charges;

/// <summary>B5-05: the flat periodic fee and the calendar month it is charged for.</summary>
public sealed class PeriodicFeeRulesTests
{
    [Fact]
    public void A_flat_periodic_fee_is_the_first_band_amount()
    {
        PeriodicFeeRules.FlatFee([new ChargeBand(LookupCodes.ChargeBases.Flat, null, 50m, null, null)]).ShouldBe(50m);
    }

    [Fact]
    public void The_minimum_and_maximum_clamp_the_fee()
    {
        PeriodicFeeRules.FlatFee([new ChargeBand(LookupCodes.ChargeBases.Flat, null, 5m, 10m, null)]).ShouldBe(10m);
        PeriodicFeeRules.FlatFee([new ChargeBand(LookupCodes.ChargeBases.Flat, null, 50m, null, 30m)]).ShouldBe(30m);
    }

    [Fact]
    public void A_percent_or_empty_periodic_fee_is_not_supported()
    {
        PeriodicFeeRules.FlatFee([new ChargeBand(LookupCodes.ChargeBases.Percent, null, 2m, null, null)]).ShouldBeNull();
        PeriodicFeeRules.FlatFee([]).ShouldBeNull();
    }

    [Theory]
    [InlineData(2026, 2, 10, 2026, 2, 28)]
    [InlineData(2028, 2, 10, 2028, 2, 29)]
    [InlineData(2026, 12, 1, 2026, 12, 31)]
    public void A_period_ends_on_the_last_day_of_its_month(int year, int month, int day, int endYear, int endMonth, int endDay)
    {
        PeriodicFeeRules.PeriodEndOf(new DateOnly(year, month, day)).ShouldBe(new DateOnly(endYear, endMonth, endDay));
    }
}
