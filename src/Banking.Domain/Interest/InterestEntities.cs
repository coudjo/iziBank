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

/// <summary>
/// B5-04 (plan 2.4): one capitalized period for one account. Append-only. The accruals in [FromDate, ToDate] are summed
/// and rounded to <see cref="Amount"/>; <see cref="TaxAmount"/> is withheld from it. The posting carries the money.
/// </summary>
public sealed class AccountInterestCapitalization
{
    public long AccountInterestCapitalizationId { get; set; }

    public long AccountId { get; set; }

    public DateOnly FromDate { get; set; }

    public DateOnly ToDate { get; set; }

    /// <summary>The gross interest credited to the account, rounded to 2 dp.</summary>
    public decimal Amount { get; set; }

    /// <summary>The tax withheld from the gross, 0 when the product does not withhold.</summary>
    public decimal TaxAmount { get; set; }

    public long PostedTransactionId { get; set; }

    public DateTime CreatedOn { get; set; }
}
