using Banking.Domain.Charges;
using Banking.Domain.Lookups;

namespace Banking.Domain.Tests.Charges;

public sealed class ChargeRulesTests
{
    private static ChargeTierInput Flat(decimal? upTo, decimal amount, decimal? min = null, decimal? max = null) =>
        new(LookupCodes.ChargeBases.Flat, upTo, amount, min, max);

    private static ChargeTierInput Percent(decimal? upTo, decimal amount, decimal? min = null, decimal? max = null) =>
        new(LookupCodes.ChargeBases.Percent, upTo, amount, min, max);

    [Fact]
    public void Each_condition_belongs_to_one_category()
    {
        ChargeConditionRules.CategoryFor(LookupCodes.ChargeConditions.PerCashWithdrawal).ShouldBe(LookupCodes.ChargeCategories.Transaction);
        ChargeConditionRules.CategoryFor(LookupCodes.ChargeConditions.PerTransferOut).ShouldBe(LookupCodes.ChargeCategories.Transaction);
        ChargeConditionRules.CategoryFor(LookupCodes.ChargeConditions.MonthlyMaintenance).ShouldBe(LookupCodes.ChargeCategories.Periodic);
        ChargeConditionRules.CategoryFor("SOMETHING_ELSE").ShouldBeNull();
    }

    [Fact]
    public void A_single_open_band_is_valid()
    {
        ChargeTierRules.Validate([Flat(null, 5m)]).ShouldBeEmpty();
    }

    [Fact]
    public void Bands_with_rising_limits_and_an_open_last_band_are_valid()
    {
        ChargeTierRules.Validate(
        [
            Flat(1_000m, 2m, min: 1m, max: 2m),
            Percent(10_000m, 0.5m, min: 2m, max: 50m),
            Percent(null, 0.25m, min: 50m)
        ]).ShouldBeEmpty();
    }

    [Fact]
    public void No_bands_is_refused()
    {
        ChargeTierRules.Validate([]).ShouldContain("Add at least one band.");
    }

    [Fact]
    public void Limits_must_rise_and_only_the_last_band_may_be_open()
    {
        var problems = ChargeTierRules.Validate(
        [
            Flat(5_000m, 2m),
            Flat(1_000m, 3m),
            Flat(9_000m, 4m)
        ]);

        problems.Any(problem => problem.StartsWith("Band 2: its transaction amount must be above", StringComparison.Ordinal)).ShouldBeTrue();
        problems.Any(problem => problem.StartsWith("Band 3: the last band has no upper limit", StringComparison.Ordinal)).ShouldBeTrue();
    }

    [Fact]
    public void A_middle_band_needs_its_limit_and_a_percentage_cannot_exceed_100()
    {
        var problems = ChargeTierRules.Validate(
        [
            Percent(null, 1m),
            Percent(null, 120m)
        ]);

        problems.Any(problem => problem.StartsWith("Band 1: enter the transaction amount", StringComparison.Ordinal)).ShouldBeTrue();
        problems.Any(problem => problem.StartsWith("Band 2: a percentage cannot be above 100", StringComparison.Ordinal)).ShouldBeTrue();
    }

    [Fact]
    public void Minimum_above_maximum_and_negative_values_are_refused()
    {
        var problems = ChargeTierRules.Validate(
        [
            new ChargeTierInput("BOGUS", null, 0m, -1m, -2m),
            Flat(null, 3m, min: 10m, max: 5m)
        ]);

        problems.Any(problem => problem.StartsWith("Band 1: choose a flat amount or a percentage", StringComparison.Ordinal)).ShouldBeTrue();
        problems.Any(problem => problem.StartsWith("Band 1: enter an amount above zero", StringComparison.Ordinal)).ShouldBeTrue();
        problems.Any(problem => problem.StartsWith("Band 1: the minimum cannot be negative", StringComparison.Ordinal)).ShouldBeTrue();
        problems.Any(problem => problem.StartsWith("Band 1: the maximum cannot be negative", StringComparison.Ordinal)).ShouldBeTrue();
        problems.Any(problem => problem.StartsWith("Band 2: the maximum cannot be below the minimum", StringComparison.Ordinal)).ShouldBeTrue();
    }
}
