using Banking.Domain.Lookups;

namespace Banking.Domain.Charges;

/// <summary>What a charge comes to when the balance may not cover all of it (plan B 6.8, B5-02).</summary>
/// <param name="Charged">Debited to the account now.</param>
/// <param name="Arrear">Recorded to be collected from later credits (DEFER only).</param>
/// <param name="WrittenOff">Not collected and not kept (COLLECT_AVAILABLE only).</param>
public readonly record struct ShortfallOutcome(decimal Charged, decimal Arrear, decimal WrittenOff);

/// <summary>Plan B 6.8, owner decision Q7: the shortfall policy, and how a charge follows it.</summary>
public static class ChargeShortfallRules
{
    /// <summary>
    /// The policy for a product charge: its own override when set, else the institution's setting, else DEFER.
    /// An unknown code falls back the same way, so a bad setting never silently writes charges off.
    /// </summary>
    public static string Resolve(string? productPolicy, string? institutionPolicy)
    {
        foreach (var candidate in new[] { productPolicy, institutionPolicy })
        {
            if (candidate is not null && LookupCodes.ChargeShortfallPolicies.All.Contains(candidate))
            {
                return candidate;
            }
        }

        return LookupCodes.ChargeShortfallPolicies.Defer;
    }

    /// <summary>
    /// Applies a policy to a fee. <paramref name="available"/> is what the balance can give to the charge now (the
    /// available balance after the principal). CHARGE_FULL takes the whole fee and may take the balance below zero.
    /// COLLECT_AVAILABLE takes what is there and writes off the rest. DEFER takes what is there and keeps the rest as
    /// an arrear.
    /// </summary>
    public static ShortfallOutcome Apply(string policy, decimal fee, decimal available)
    {
        if (fee <= 0m)
        {
            return new ShortfallOutcome(0m, 0m, 0m);
        }

        if (policy == LookupCodes.ChargeShortfallPolicies.ChargeFull)
        {
            return new ShortfallOutcome(fee, 0m, 0m);
        }

        var charged = Math.Clamp(available, 0m, fee);
        var rest = fee - charged;
        return policy == LookupCodes.ChargeShortfallPolicies.CollectAvailable
            ? new ShortfallOutcome(charged, 0m, rest)
            : new ShortfallOutcome(charged, rest, 0m);
    }
}
