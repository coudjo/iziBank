using Banking.Domain.Charges;
using Banking.Domain.Lookups;

namespace Banking.Domain.Tests.Charges;

/// <summary>B5-02: which shortfall policy applies, and what a charge comes to when the balance is short.</summary>
public sealed class ChargeShortfallRulesTests
{
    [Fact]
    public void A_product_policy_overrides_the_institution_setting() =>
        ChargeShortfallRules.Resolve(LookupCodes.ChargeShortfallPolicies.CollectAvailable, LookupCodes.ChargeShortfallPolicies.Defer)
            .ShouldBe(LookupCodes.ChargeShortfallPolicies.CollectAvailable);

    [Fact]
    public void Without_a_product_policy_the_institution_setting_applies() =>
        ChargeShortfallRules.Resolve(null, LookupCodes.ChargeShortfallPolicies.ChargeFull).ShouldBe(LookupCodes.ChargeShortfallPolicies.ChargeFull);

    [Theory]
    [InlineData("NOT_A_POLICY", null)]
    [InlineData(null, "BAD")]
    [InlineData(null, null)]
    public void An_unknown_or_missing_policy_falls_back_to_defer(string? product, string? institution) =>
        ChargeShortfallRules.Resolve(product, institution).ShouldBe(LookupCodes.ChargeShortfallPolicies.Defer);

    [Theory]
    [InlineData(LookupCodes.ChargeShortfallPolicies.CollectAvailable)]
    [InlineData(LookupCodes.ChargeShortfallPolicies.ChargeFull)]
    [InlineData(LookupCodes.ChargeShortfallPolicies.Defer)]
    public void A_fee_the_balance_covers_is_charged_in_full_under_every_policy(string policy) =>
        ChargeShortfallRules.Apply(policy, 10m, 50m).ShouldBe(new ShortfallOutcome(10m, 0m, 0m));

    [Fact]
    public void Charge_full_takes_the_whole_fee_even_when_the_balance_cannot_cover_it() =>
        ChargeShortfallRules.Apply(LookupCodes.ChargeShortfallPolicies.ChargeFull, 10m, -5m).ShouldBe(new ShortfallOutcome(10m, 0m, 0m));

    [Fact]
    public void Collect_available_takes_what_is_there_and_writes_off_the_rest()
    {
        ChargeShortfallRules.Apply(LookupCodes.ChargeShortfallPolicies.CollectAvailable, 10m, 4m).ShouldBe(new ShortfallOutcome(4m, 0m, 6m));
        ChargeShortfallRules.Apply(LookupCodes.ChargeShortfallPolicies.CollectAvailable, 10m, -3m).ShouldBe(new ShortfallOutcome(0m, 0m, 10m));
    }

    [Fact]
    public void Defer_takes_what_is_there_and_keeps_the_rest_as_an_arrear() =>
        ChargeShortfallRules.Apply(LookupCodes.ChargeShortfallPolicies.Defer, 10m, 4m).ShouldBe(new ShortfallOutcome(4m, 6m, 0m));

    [Fact]
    public void No_fee_moves_nothing() =>
        ChargeShortfallRules.Apply(LookupCodes.ChargeShortfallPolicies.Defer, 0m, 50m).ShouldBe(new ShortfallOutcome(0m, 0m, 0m));
}
