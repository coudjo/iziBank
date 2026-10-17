namespace Banking.Domain.Accounts;

/// <summary>
/// Plan 5.8: the system's definition of an account's balances. The posting phase reuses these formulas unchanged.
/// The product minimum balance is enforced when a debit is attempted, not folded in here.
/// </summary>
public static class AccountBalanceCalculator
{
    /// <summary><c>ClearBalance - FreezedAmount - UnsupervisedDebit + OverdrawLimit</c>.</summary>
    public static decimal Available(AccountBalance balance)
    {
        ArgumentNullException.ThrowIfNull(balance);
        return balance.ClearBalance - balance.FreezedAmount - balance.UnsupervisedDebit + balance.OverdrawLimit;
    }

    /// <summary><c>ClearBalance + UnsupervisedCredit - UnsupervisedDebit</c>.</summary>
    public static decimal Total(AccountBalance balance)
    {
        ArgumentNullException.ThrowIfNull(balance);
        return balance.ClearBalance + balance.UnsupervisedCredit - balance.UnsupervisedDebit;
    }

    /// <summary>Sets the stored <c>AvailableBalance</c> and <c>TotalBalance</c> from the other columns.</summary>
    public static void Recompute(AccountBalance balance)
    {
        ArgumentNullException.ThrowIfNull(balance);
        balance.AvailableBalance = Available(balance);
        balance.TotalBalance = Total(balance);
    }
}
