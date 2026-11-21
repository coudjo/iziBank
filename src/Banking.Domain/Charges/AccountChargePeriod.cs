using Banking.Domain.Lookups;

namespace Banking.Domain.Charges;

/// <summary>
/// A periodic (monthly maintenance) fee posted for one account and one calendar month (plan B 6.8, B5-05). A row is
/// written only with its posting, so a month that could not be charged yet (no balance under a policy that takes
/// nothing) has no row and is retried by the next run. The unique index on account and period start stops a second charge.
/// </summary>
public sealed class AccountChargePeriod
{
    public long AccountChargePeriodId { get; set; }

    public long AccountId { get; set; }

    public long ProductChargeId { get; set; }

    public DateOnly PeriodStart { get; set; }

    public DateOnly PeriodEnd { get; set; }

    /// <summary>The fee the product sets for the period, before the balance is applied.</summary>
    public decimal FeeAmount { get; set; }

    /// <summary>What was debited now. Below the fee under COLLECT_AVAILABLE or DEFER when the balance falls short.</summary>
    public decimal ChargedAmount { get; set; }

    public long PostedTransactionId { get; set; }

    public DateTime CreatedOn { get; set; }
}

/// <summary>B5-05: how a periodic fee is worked out and when its period ends.</summary>
public static class PeriodicFeeRules
{
    /// <summary>The last day of the calendar month that contains the day.</summary>
    public static DateOnly PeriodEndOf(DateOnly day) => new(day.Year, day.Month, DateTime.DaysInMonth(day.Year, day.Month));

    /// <summary>
    /// The flat fee of a periodic charge: the first band's amount, clamped by its minimum and maximum, rounded half away
    /// from zero to two decimals. Null when the charge has no bands or is percent-based, which a periodic fee does not
    /// support (there is no transaction amount to take a percentage of).
    /// </summary>
    public static decimal? FlatFee(IReadOnlyList<ChargeBand> bands)
    {
        ArgumentNullException.ThrowIfNull(bands);
        if (bands.Count == 0 || bands[0].BasisCode != LookupCodes.ChargeBases.Flat)
        {
            return null;
        }

        var band = bands[0];
        var fee = band.AmountOrPercentage;
        if (band.MinimumCharge is { } minimum && fee < minimum)
        {
            fee = minimum;
        }

        if (band.MaximumCharge is { } maximum && fee > maximum)
        {
            fee = maximum;
        }

        return Math.Round(fee, 2, MidpointRounding.AwayFromZero);
    }
}
