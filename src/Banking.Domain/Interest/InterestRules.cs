using Banking.Domain.Lookups;

namespace Banking.Domain.Interest;

/// <summary>A credit interest band: the annual rate in percent for balances within the amount range (null = open).</summary>
public readonly record struct InterestBand(decimal? MinimumAmount, decimal? MaximumAmount, decimal InterestRate);

/// <summary>B5-03: the daily interest accrual arithmetic (plan 6.9).</summary>
public static class InterestRules
{
    /// <summary>Accruals are kept to 6 decimals; only capitalization rounds to money (B5-04).</summary>
    public const int AccrualScale = 6;

    /// <summary>The number of days the annual rate is spread over: 365 (ACT_365, the default) or 360 (ACT_360).</summary>
    public static decimal YearLength(string? dayCountCode) =>
        dayCountCode == LookupCodes.DayCounts.Act360 ? 360m : 365m;

    /// <summary>The band whose range covers the balance. Bands of one product and rate type never overlap (ProductRates).</summary>
    public static InterestBand? BandFor(decimal balance, IEnumerable<InterestBand> bands)
    {
        foreach (var band in bands)
        {
            if ((band.MinimumAmount is null || balance >= band.MinimumAmount)
                && (band.MaximumAmount is null || balance <= band.MaximumAmount))
            {
                return band;
            }
        }

        return null;
    }

    /// <summary>One day's accrual: balance × rate / 100 / year length. Nothing accrues on a zero or negative balance.</summary>
    public static decimal DailyAmount(decimal balance, decimal annualRatePercent, decimal yearLength)
    {
        if (balance <= 0m || annualRatePercent <= 0m)
        {
            return 0m;
        }

        return Math.Round(balance * annualRatePercent / 100m / yearLength, AccrualScale, MidpointRounding.AwayFromZero);
    }
}
