using Banking.Domain.Lookups;

namespace Banking.Domain.Charges;

/// <summary>One band of a charge as it applies: the upper limit of the transaction amount it covers (null for the open last band).</summary>
public sealed record ChargeBand(string BasisCode, decimal? UpperLimit, decimal AmountOrPercentage, decimal? MinimumCharge, decimal? MaximumCharge);

/// <summary>Plan B 2.4 and B5-01: what a per-transaction charge comes to, and when a free-transaction allowance waives it.</summary>
public static class ChargeCalculator
{
    /// <summary>
    /// The first band whose upper limit covers the amount, or the open last band. A flat band charges its amount; a
    /// percent band charges that share of the transaction amount. Minimum and maximum clamp the result, which is then
    /// rounded half away from zero to two decimals (B-15, plan B 2.4).
    /// </summary>
    public static decimal Calculate(IReadOnlyList<ChargeBand> bands, decimal amount)
    {
        ArgumentNullException.ThrowIfNull(bands);
        if (bands.Count == 0 || amount <= 0m)
        {
            return 0m;
        }

        var band = bands.FirstOrDefault(row => row.UpperLimit is null || amount <= row.UpperLimit.Value) ?? bands[^1];
        var fee = band.BasisCode == LookupCodes.ChargeBases.Percent ? amount * band.AmountOrPercentage / 100m : band.AmountOrPercentage;
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

    /// <summary>A free-transaction allowance covers the first N transactions of the calendar month. None or zero means no allowance.</summary>
    public static bool IsWaived(short? freeTransactions, int transactionsThisMonth) =>
        freeTransactions is > 0 && transactionsThisMonth < freeTransactions.Value;
}

/// <summary>The transaction kinds a charge quote can be asked for, each charged under one condition.</summary>
public static class ChargeQuoteKinds
{
    public const string CashWithdrawal = "CASH_WITHDRAWAL";
    public const string Transfer = "TRANSFER";

    /// <summary>The charge condition for a quote kind, or null for an unknown kind.</summary>
    public static string? ConditionFor(string? kind) => kind switch
    {
        CashWithdrawal => LookupCodes.ChargeConditions.PerCashWithdrawal,
        Transfer => LookupCodes.ChargeConditions.PerTransferOut,
        _ => null
    };
}
