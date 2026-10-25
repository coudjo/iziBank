namespace Banking.Domain.Charges;

/// <summary>
/// A charge the account could not pay when it was due (DEFER, plan B 2.4 and 6.8). Collected oldest first from later
/// credits, in the same posting as the credit. Updated only under the account's balance lock.
/// </summary>
public sealed class AccountChargeArrear
{
    public long AccountChargeArrearId { get; set; }

    public long AccountId { get; set; }

    public long ProductChargeId { get; set; }

    /// <summary>The posted transaction that raised the arrear.</summary>
    public long RaisedTransactionId { get; set; }

    public DateOnly RaisedOn { get; set; }

    public decimal Amount { get; set; }

    /// <summary>What is still owed. Never below zero; never above <see cref="Amount"/>.</summary>
    public decimal OutstandingAmount { get; set; }

    public byte[] RowVersion { get; set; } = [];
}
