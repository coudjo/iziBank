using Banking.Domain.Accounts;

namespace Banking.Domain.Interest;

/// <summary>
/// B5-03: one day's interest accrual for one account (plan 2.4). Append-only: a day is accrued once and the row is never
/// changed, so the examiner's trail matches what was accrued. Capitalization (B5-04) sums these by date range.
/// </summary>
public sealed class AccountInterestAccrual
{
    public long AccountInterestAccrualId { get; set; }

    public long AccountId { get; set; }

    public DateOnly AccrualDate { get; set; }

    /// <summary>The closing clear balance the accrual was computed on.</summary>
    public decimal Balance { get; set; }

    /// <summary>The annual rate in percent that applied that day (0 when nothing accrued).</summary>
    public decimal Rate { get; set; }

    public decimal Amount { get; set; }

    public DateTime AccruedOn { get; set; }

    public Account? Account { get; set; }
}
