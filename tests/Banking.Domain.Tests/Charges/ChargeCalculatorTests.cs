using Banking.Domain.Charges;
using Banking.Domain.Lookups;

namespace Banking.Domain.Tests.Charges;

/// <summary>B5-01: band selection, flat and percent amounts, minimum and maximum, rounding and free allowances.</summary>
public sealed class ChargeCalculatorTests
{
    [Fact]
    public void The_first_band_whose_limit_covers_the_amount_applies_and_the_open_band_takes_the_rest()
    {
        var bands = new[]
        {
            new ChargeBand(LookupCodes.ChargeBases.Flat, 1000m, 5m, null, null),
            new ChargeBand(LookupCodes.ChargeBases.Flat, null, 10m, null, null)
        };

        ChargeCalculator.Calculate(bands, 500m).ShouldBe(5m);
        ChargeCalculator.Calculate(bands, 1000m).ShouldBe(5m);
        ChargeCalculator.Calculate(bands, 1000.01m).ShouldBe(10m);
    }

    [Fact]
    public void A_percent_band_is_clamped_to_its_minimum_and_maximum()
    {
        var bands = new[] { new ChargeBand(LookupCodes.ChargeBases.Percent, null, 1.5m, 2m, 50m) };

        ChargeCalculator.Calculate(bands, 100m).ShouldBe(2m);
        ChargeCalculator.Calculate(bands, 2000m).ShouldBe(30m);
        ChargeCalculator.Calculate(bands, 10000m).ShouldBe(50m);
    }

    [Fact]
    public void Fees_round_half_away_from_zero_to_two_decimals()
    {
        var bands = new[] { new ChargeBand(LookupCodes.ChargeBases.Percent, null, 1.25m, null, null) };

        ChargeCalculator.Calculate(bands, 10m).ShouldBe(0.13m);
    }

    [Fact]
    public void No_bands_or_no_amount_charges_nothing()
    {
        ChargeCalculator.Calculate([], 100m).ShouldBe(0m);
        ChargeCalculator.Calculate([new ChargeBand(LookupCodes.ChargeBases.Flat, null, 5m, null, null)], 0m).ShouldBe(0m);
    }

    [Theory]
    [InlineData(2, 0, true)]
    [InlineData(2, 1, true)]
    [InlineData(2, 2, false)]
    [InlineData(0, 0, false)]
    [InlineData(null, 0, false)]
    public void A_free_allowance_waives_only_the_transactions_within_it(short? free, int used, bool waived) =>
        ChargeCalculator.IsWaived(free, used).ShouldBe(waived);

    [Theory]
    [InlineData("CASH_WITHDRAWAL", LookupCodes.ChargeConditions.PerCashWithdrawal)]
    [InlineData("TRANSFER", LookupCodes.ChargeConditions.PerTransferOut)]
    public void Each_quote_kind_is_charged_under_its_condition(string kind, string condition) =>
        ChargeQuoteKinds.ConditionFor(kind).ShouldBe(condition);

    [Fact]
    public void An_unknown_quote_kind_has_no_condition() =>
        ChargeQuoteKinds.ConditionFor("SALARY").ShouldBeNull();
}
