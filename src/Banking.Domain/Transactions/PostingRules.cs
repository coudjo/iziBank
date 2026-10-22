using Banking.Domain.Accounts;
using Banking.Domain.Lookups;

namespace Banking.Domain.Transactions;

/// <summary>A line on a customer account. The engine adds the matching control-account line itself.</summary>
public sealed record AccountLine(long AccountId, string CrDb, decimal Amount);

/// <summary>A general ledger line other than a product control account (till, vault, inter-branch, income...).</summary>
public sealed record LedgerLine(long LedgerAccountId, int BranchId, string CrDb, decimal Amount);

/// <summary>What the posting checks need to know about one account, read under its balance lock.</summary>
public sealed record AccountPostingState(
    bool Closed,
    bool Approved,
    bool Frozen,
    bool DoNotAllowCredit,
    bool DoNotAllowDebit,
    decimal? CreditLimit,
    decimal? DebitLimit,
    bool Dormant,
    decimal AvailableBalance,
    decimal MinimumBalance);

/// <summary>Why a posting cannot touch an account (plan B 5.3). The API maps each to a problem code.</summary>
public enum PostingRefusal
{
    None,
    Closed,
    NotApproved,
    Frozen,
    CreditBlocked,
    DebitBlocked,
    CreditLimit,
    DebitLimit,
    Dormant,
    InsufficientFunds
}

/// <summary>Plan B 5.2 and 5.3: the pure posting rules. Infrastructure supplies the state and does the writing.</summary>
public static class PostingRules
{
    /// <summary>Largest single amount the API accepts (well inside decimal(19,4)).</summary>
    public const decimal MaxAmount = 999_999_999_999.99m;

    /// <summary>Positive, at most 2 decimal places, within range (plan B 0.4, 5.6).</summary>
    public static bool IsValidAmount(decimal amount) =>
        amount > 0m && amount <= MaxAmount && decimal.Round(amount, 2) == amount;

    public static bool IsSide(string? side) => side is EntrySides.Credit or EntrySides.Debit;

    public static string Opposite(string side) => side == EntrySides.Credit ? EntrySides.Debit : EntrySides.Credit;

    /// <summary>
    /// The first reason the account cannot take these totals, checked in plan order. Credits: closed, not approved,
    /// frozen, credits blocked, credit limit. Debits: the same, then debit limit, dormant, and the available balance
    /// staying at or above the product minimum.
    /// </summary>
    public static PostingRefusal Check(AccountPostingState state, decimal totalCredit, decimal totalDebit)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Closed)
        {
            return PostingRefusal.Closed;
        }

        if (!state.Approved)
        {
            return PostingRefusal.NotApproved;
        }

        if (state.Frozen)
        {
            return PostingRefusal.Frozen;
        }

        if (totalCredit > 0m)
        {
            if (state.DoNotAllowCredit)
            {
                return PostingRefusal.CreditBlocked;
            }

            if (state.CreditLimit is { } creditLimit && totalCredit > creditLimit)
            {
                return PostingRefusal.CreditLimit;
            }
        }

        if (totalDebit > 0m)
        {
            if (state.DoNotAllowDebit)
            {
                return PostingRefusal.DebitBlocked;
            }

            if (state.DebitLimit is { } debitLimit && totalDebit > debitLimit)
            {
                return PostingRefusal.DebitLimit;
            }

            if (state.Dormant)
            {
                return PostingRefusal.Dormant;
            }

            // Credits in the same posting count: a transfer in and out of one account nets.
            if (state.AvailableBalance + totalCredit - totalDebit < state.MinimumBalance)
            {
                return PostingRefusal.InsufficientFunds;
            }
        }

        return PostingRefusal.None;
    }

    /// <summary>Branches whose debits and credits differ (plan B 0.1). Empty when the posting balances.</summary>
    public static IReadOnlyList<int> UnbalancedBranches(IEnumerable<LedgerLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return lines
            .GroupBy(static line => line.BranchId)
            .Where(static branch => branch.Sum(static line => line.CrDb == EntrySides.Debit ? line.Amount : -line.Amount) != 0m)
            .Select(static branch => branch.Key)
            .OrderBy(static id => id)
            .ToList();
    }

    /// <summary>Posts one line to the balance: clear balance moves, derived balances recompute. Returns the new clear balance.</summary>
    public static decimal Post(AccountBalance balance, string side, decimal amount)
    {
        ArgumentNullException.ThrowIfNull(balance);
        balance.ClearBalance += side == EntrySides.Credit ? amount : -amount;
        AccountBalanceCalculator.Recompute(balance);
        return balance.ClearBalance;
    }

    /// <summary>A posting waiting for approval holds its amount in the unsupervised columns (handoff 3).</summary>
    public static void Hold(AccountBalance balance, string side, decimal amount)
    {
        ArgumentNullException.ThrowIfNull(balance);
        if (side == EntrySides.Credit)
        {
            balance.UnsupervisedCredit += amount;
        }
        else
        {
            balance.UnsupervisedDebit += amount;
        }

        AccountBalanceCalculator.Recompute(balance);
    }

    /// <summary>Undoes <see cref="Hold"/> when the request is approved, rejected or cancelled.</summary>
    public static void Release(AccountBalance balance, string side, decimal amount)
    {
        ArgumentNullException.ThrowIfNull(balance);
        if (side == EntrySides.Credit)
        {
            balance.UnsupervisedCredit -= amount;
        }
        else
        {
            balance.UnsupervisedDebit -= amount;
        }

        AccountBalanceCalculator.Recompute(balance);
    }
}

/// <summary>One holder as the mandate rules need it.</summary>
public sealed record MandateHolder(long PartyId, string RoleCode, string PartyTypeCode);

/// <summary>Plan B 6.3 and addendum 6.9a: who signs on an account, and whether a set of signers is enough.</summary>
public static class MandateRules
{
    /// <summary>
    /// Signatories always sign. When the primary holder is an individual, the primary and joint holders sign too; for a
    /// business or group only the signatories do.
    /// </summary>
    public static IReadOnlySet<long> Signers(IEnumerable<MandateHolder> holders)
    {
        ArgumentNullException.ThrowIfNull(holders);
        var list = holders.ToList();
        var individualPrimary = list.Exists(static holder =>
            holder.RoleCode == LookupCodes.AccountHolderRoles.Primary && holder.PartyTypeCode == LookupCodes.PartyTypes.Individual);

        return list
            .Where(holder => holder.RoleCode == LookupCodes.AccountHolderRoles.Signatory
                             || (individualPrimary && holder.RoleCode is LookupCodes.AccountHolderRoles.Primary or LookupCodes.AccountHolderRoles.Joint))
            .Select(static holder => holder.PartyId)
            .ToHashSet();
    }

    /// <summary>Every confirmed signer must be a signer; all-to-sign needs every signer, the other modes one.</summary>
    public static bool IsSatisfied(string? operatingModeCode, IReadOnlySet<long> signers, IReadOnlyCollection<long> confirmed)
    {
        ArgumentNullException.ThrowIfNull(signers);
        ArgumentNullException.ThrowIfNull(confirmed);
        if (confirmed.Count == 0 || confirmed.Any(id => !signers.Contains(id)))
        {
            return false;
        }

        return operatingModeCode != LookupCodes.OperatingModes.AllToSign || signers.All(id => confirmed.Contains(id));
    }
}

/// <summary>Which permission approves a posting of each code (plan B section 3).</summary>
public static class PostingPermissions
{
    public static (string ModuleCode, string SubModuleCode) For(string transactionCode) => transactionCode switch
    {
        TransactionCodes.CashDeposit or TransactionCodes.CashWithdrawal => (ModuleCatalog.Transactions, "CASH"),
        TransactionCodes.Transfer or TransactionCodes.TransferAcrossBranches => (ModuleCatalog.Transactions, "TRANSFER"),
        TransactionCodes.Reversal => (ModuleCatalog.Transactions, "REVERSAL"),
        TransactionCodes.Journal => (ModuleCatalog.Ledger, "JOURNAL"),
        TransactionCodes.TillBuy or TransactionCodes.TillSell => (ModuleCatalog.Transactions, "VAULT"),
        TransactionCodes.TillShortage or TransactionCodes.TillOverage => (ModuleCatalog.Transactions, "TILL"),
        _ => (ModuleCatalog.Ledger, "JOURNAL")
    };
}
