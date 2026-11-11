using Banking.Domain.Lookups;

namespace Banking.Domain.Interest;

/// <summary>B5-04: capitalization periods, rounding and withholding tax (plan 6.9, owner answers Q6 and Q10).</summary>
public static class CapitalizationRules
{
    /// <summary>
    /// The last day of the calendar period that contains <paramref name="day"/>, for a frequency code. Periods are
    /// calendar-aligned: monthly ends each month, quarterly ends in March, June, September and December, and so on.
    /// Returns null for an unknown code, which means the product does not capitalize.
    /// </summary>
    public static DateOnly? PeriodEnd(string? frequencyCode, DateOnly day)
    {
        var months = frequencyCode switch
        {
            LookupCodes.CapitalizationFrequencies.Monthly => 1,
            LookupCodes.CapitalizationFrequencies.Quarterly => 3,
            LookupCodes.CapitalizationFrequencies.HalfYearly => 6,
            LookupCodes.CapitalizationFrequencies.Yearly => 12,
            _ => 0
        };
        if (months == 0)
        {
            return null;
        }

        var endMonth = (day.Month - 1) / months * months + months;
        return new DateOnly(day.Year, endMonth, DateTime.DaysInMonth(day.Year, endMonth));
    }

    /// <summary>Rounds money to 2 dp by the institution's <c>Interest:Rounding</c> mode (Q6).</summary>
    public static decimal Round(decimal amount, string rounding) => rounding switch
    {
        "HALF_EVEN" => Math.Round(amount, 2, MidpointRounding.ToEven),
        "DOWN" => Math.Truncate(amount * 100m) / 100m,
        _ => Math.Round(amount, 2, MidpointRounding.AwayFromZero)
    };

    /// <summary>The withholding tax on a gross amount, at a percent rate, rounded the same way.</summary>
    public static decimal WithholdingTax(decimal gross, decimal ratePercent, string rounding) =>
        ratePercent <= 0m || gross <= 0m ? 0m : Round(gross * ratePercent / 100m, rounding);
}
