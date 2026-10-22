namespace Banking.Domain.Transactions;

/// <summary>Groups of transaction codes, for reports and filters.</summary>
public static class TransactionCategories
{
    public const string Cash = "CASH";
    public const string Transfer = "TRANSFER";
    public const string Correction = "CORRECTION";
    public const string Till = "TILL";
    public const string Charge = "CHARGE";
    public const string Interest = "INTEREST";

    public static readonly IReadOnlyList<string> All = [Cash, Transfer, Correction, Till, Charge, Interest];
}

/// <summary>One seeded system transaction code.</summary>
public sealed record TransactionCodeDefinition(string Code, string Description, string Category);

/// <summary>Plan B section 7: the system transaction codes. Seeded by the Migrator; application code uses these constants.</summary>
public static class TransactionCodes
{
    public const int MaxCodeLength = 20;

    public const string CashDeposit = "CASH_DEP";
    public const string CashWithdrawal = "CASH_WDL";
    public const string Transfer = "TRF";
    public const string TransferAcrossBranches = "TRF_XB";
    public const string Reversal = "REV";
    public const string Journal = "JNL";
    public const string TillBuy = "TILL_BUY";
    public const string TillSell = "TILL_SELL";
    public const string TillShortage = "TILL_SHORT";
    public const string TillOverage = "TILL_OVER";
    public const string TransactionCharge = "CHG_TXN";
    public const string MonthlyFee = "CHG_MTH";
    public const string ChargeArrearCollected = "CHG_ARR";
    public const string InterestAccrual = "INT_ACCR";
    public const string InterestCapitalized = "INT_CAP";
    public const string WithholdingTax = "WHT";

    public static readonly IReadOnlyList<TransactionCodeDefinition> All =
    [
        new(CashDeposit, "Cash deposit", TransactionCategories.Cash),
        new(CashWithdrawal, "Cash withdrawal", TransactionCategories.Cash),
        new(Transfer, "Transfer", TransactionCategories.Transfer),
        new(TransferAcrossBranches, "Transfer between branches", TransactionCategories.Transfer),
        new(Reversal, "Reversal", TransactionCategories.Correction),
        new(Journal, "Journal", TransactionCategories.Correction),
        new(TillBuy, "Cash from the vault", TransactionCategories.Till),
        new(TillSell, "Cash to the vault", TransactionCategories.Till),
        new(TillShortage, "Till shortage", TransactionCategories.Till),
        new(TillOverage, "Till overage", TransactionCategories.Till),
        new(TransactionCharge, "Transaction charge", TransactionCategories.Charge),
        new(MonthlyFee, "Monthly fee", TransactionCategories.Charge),
        new(ChargeArrearCollected, "Charge arrear collected", TransactionCategories.Charge),
        new(InterestAccrual, "Interest accrual", TransactionCategories.Interest),
        new(InterestCapitalized, "Interest credited", TransactionCategories.Interest),
        new(WithholdingTax, "Withholding tax", TransactionCategories.Interest)
    ];
}
