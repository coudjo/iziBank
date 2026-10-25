namespace Banking.Domain.Accounts;

public static class AccountCloseChecks
{
    public const string ZeroBalance = "ZERO_BALANCE";
    public const string NoHolds = "NO_HOLDS";
    public const string NoPendingItems = "NO_PENDING_ITEMS";
    public const string NoArrears = "NO_ARREARS";

    public static readonly IReadOnlyList<string> All = [ZeroBalance, NoHolds, NoPendingItems, NoArrears];
}

public sealed record AccountCloseCheck(string Code, bool Passed);

/// <summary>
/// Plan 6.10: an account closes only with a zero clear balance, nothing held, and no unsupervised credits or debits.
/// The portal shows the same list before the user confirms (UX 6.8).
/// </summary>
public static class AccountCloseRules
{
    /// <summary>
    /// The close checks. A negative balance fails ZERO_BALANCE; an open arrear (B5-02) fails NO_ARREARS.
    /// </summary>
    public static IReadOnlyList<AccountCloseCheck> Check(AccountBalance balance, decimal outstandingArrears = 0m)
    {
        ArgumentNullException.ThrowIfNull(balance);
        return
        [
            new(AccountCloseChecks.ZeroBalance, balance.ClearBalance == 0m),
            new(AccountCloseChecks.NoHolds, balance.FreezedAmount == 0m),
            new(AccountCloseChecks.NoPendingItems, balance.UnsupervisedCredit == 0m && balance.UnsupervisedDebit == 0m),
            new(AccountCloseChecks.NoArrears, outstandingArrears == 0m)
        ];
    }
}

/// <summary>Plan 6.10 hold and release amounts (A5-06). Freezing and unfreezing carry no amount.</summary>
public static class FreezeActionRules
{
    /// <summary>Applies a hold or a release to the balance and recomputes it. Returns false when a release is too large.</summary>
    public static bool TryApply(AccountBalance balance, bool isHold, decimal amount)
    {
        ArgumentNullException.ThrowIfNull(balance);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);

        if (!isHold && amount > balance.FreezedAmount)
        {
            return false;
        }

        balance.FreezedAmount += isHold ? amount : -amount;
        AccountBalanceCalculator.Recompute(balance);
        return true;
    }
}
