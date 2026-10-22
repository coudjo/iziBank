namespace Banking.Application.Transactions;

/// <summary>Configuration section <c>Transactions</c> (plan B section 4).</summary>
public sealed class TransactionOptions
{
    public const string SectionName = "Transactions";

    /// <summary>
    /// At or above this amount, a cash deposit by someone other than the holder, or a cash payment to someone other than
    /// the holder, needs that person's ID type and number (Q3). Null until the institution sets it.
    /// </summary>
    public decimal? CashTransactionReportThreshold { get; set; }

    /// <summary>How far back a posted transaction can be reversed.</summary>
    public int ReversalDays { get; set; } = 30;
}
