using Banking.Domain.Lookups;

namespace Banking.Domain.Charges;

/// <summary>Plan B 2.4: which charge categories a condition belongs to.</summary>
public static class ChargeConditionRules
{
    /// <summary>The category a condition charges under, or null for an unknown condition.</summary>
    public static string? CategoryFor(string conditionCode) => conditionCode switch
    {
        LookupCodes.ChargeConditions.PerCashWithdrawal or LookupCodes.ChargeConditions.PerTransferOut => LookupCodes.ChargeCategories.Transaction,
        LookupCodes.ChargeConditions.MonthlyMaintenance => LookupCodes.ChargeCategories.Periodic,
        _ => null
    };
}

/// <summary>The fields of one charge band, by code, as they arrive from the API.</summary>
public sealed record ChargeTierInput(string? BasisCode, decimal? TransactionAmount, decimal? AmountOrPercentage, decimal? MinimumCharge, decimal? MaximumCharge);

/// <summary>
/// Plan B 2.4 and B0-03 acceptance: bands are numbered in order, each with a valid basis and amount, minimum not above
/// maximum, upper limits strictly increasing, and only the last band open-ended. Returns one message per problem; an
/// empty list means the bands are valid.
/// </summary>
public static class ChargeTierRules
{
    public static IReadOnlyList<string> Validate(IReadOnlyList<ChargeTierInput> tiers)
    {
        ArgumentNullException.ThrowIfNull(tiers);
        var problems = new List<string>();
        if (tiers.Count == 0)
        {
            problems.Add("Add at least one band.");
            return problems;
        }

        decimal? previous = null;
        for (var index = 0; index < tiers.Count; index++)
        {
            var tier = tiers[index];
            var label = "Band " + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + ": ";
            var last = index == tiers.Count - 1;

            if (tier.BasisCode is not (LookupCodes.ChargeBases.Flat or LookupCodes.ChargeBases.Percent))
            {
                problems.Add(label + "choose a flat amount or a percentage.");
            }

            if (tier.AmountOrPercentage is not { } amount || amount <= 0m)
            {
                problems.Add(label + "enter an amount above zero.");
            }
            else if (tier.BasisCode == LookupCodes.ChargeBases.Percent && amount > 100m)
            {
                problems.Add(label + "a percentage cannot be above 100.");
            }

            if (tier.MinimumCharge is { } minimum && minimum < 0m)
            {
                problems.Add(label + "the minimum cannot be negative.");
            }

            if (tier.MaximumCharge is { } maximum && maximum < 0m)
            {
                problems.Add(label + "the maximum cannot be negative.");
            }

            if (tier.MinimumCharge is { } low && tier.MaximumCharge is { } high && high < low)
            {
                problems.Add(label + "the maximum cannot be below the minimum.");
            }

            if (last)
            {
                if (tier.TransactionAmount is not null)
                {
                    problems.Add(label + "the last band has no upper limit; leave its transaction amount empty.");
                }
            }
            else
            {
                if (tier.TransactionAmount is not { } limit || limit <= 0m)
                {
                    problems.Add(label + "enter the transaction amount this band goes up to.");
                }
                else
                {
                    if (previous is { } before && limit <= before)
                    {
                        problems.Add(label + "its transaction amount must be above the band before it.");
                    }

                    previous = limit;
                }
            }
        }

        return problems;
    }
}
