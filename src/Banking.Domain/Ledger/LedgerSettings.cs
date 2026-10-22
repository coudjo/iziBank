using Banking.Domain.Auditing;
using Banking.Domain.Institution;
using Banking.Domain.Lookups;
using Banking.Domain.Products;

namespace Banking.Domain.Ledger;

/// <summary>Which ledger accounts a setting may point at.</summary>
public sealed record LedgerSettingDefinition(string Code, string Description, IReadOnlyList<string> AllowedTypes);

/// <summary>Plan B 2.2: the coded ledger settings. Branch settings are per branch; product settings per product.</summary>
public static class LedgerSettingCodes
{
    public const int MaxCodeLength = 20;

    public const string Vault = "VAULT";
    public const string InterBranch = "INTER_BRANCH";
    public const string CashShort = "CASH_SHORT";
    public const string CashOver = "CASH_OVER";
    public const string WithholdingTaxPayable = "WHT_PAYABLE";
    public const string Suspense = "SUSPENSE";

    public const string InterestExpense = "INTEREST_EXPENSE";
    public const string InterestPayable = "INTEREST_PAYABLE";
    public const string ChargeIncome = "CHARGE_INCOME";

    private static readonly string[] Asset = [LookupCodes.LedgerAccountTypes.Asset];
    private static readonly string[] AssetOrLiability = [LookupCodes.LedgerAccountTypes.Asset, LookupCodes.LedgerAccountTypes.Liability];

    public static readonly IReadOnlyList<LedgerSettingDefinition> Branch =
    [
        new(Vault, "Vault cash", Asset),
        new(InterBranch, "Inter-branch account", AssetOrLiability),
        new(CashShort, "Cash shortages", [LookupCodes.LedgerAccountTypes.Asset, LookupCodes.LedgerAccountTypes.Expense]),
        new(CashOver, "Cash overages", [LookupCodes.LedgerAccountTypes.Liability, LookupCodes.LedgerAccountTypes.Income]),
        new(WithholdingTaxPayable, "Withholding tax payable", [LookupCodes.LedgerAccountTypes.Liability]),
        new(Suspense, "Suspense", AssetOrLiability)
    ];

    public static readonly IReadOnlyList<LedgerSettingDefinition> Product =
    [
        new(InterestExpense, "Interest expense", [LookupCodes.LedgerAccountTypes.Expense]),
        new(InterestPayable, "Interest payable", [LookupCodes.LedgerAccountTypes.Liability]),
        new(ChargeIncome, "Charge income", [LookupCodes.LedgerAccountTypes.Income])
    ];

    /// <summary>Branch settings a branch needs before it can post cash (plan B 2.2).</summary>
    public static readonly IReadOnlyList<string> RequiredForCash = [Vault, CashShort, CashOver];
}

/// <summary>Plan B 2.2: a branch's ledger account for one coded purpose. Written only when a change request is approved.</summary>
public sealed class SystemLedgerSetting : AuditableEntity
{
    public long SystemLedgerSettingId { get; set; }

    public int BranchId { get; set; }

    public string SettingCode { get; set; } = string.Empty;

    public long LedgerAccountId { get; set; }

    public Branch? Branch { get; set; }

    public LedgerAccount? LedgerAccount { get; set; }
}

/// <summary>Plan B 2.2: a product's ledger account for one coded purpose. Written only when a change request is approved.</summary>
public sealed class ProductLedgerSetting : AuditableEntity
{
    public long ProductLedgerSettingId { get; set; }

    public int ProductId { get; set; }

    public string SettingCode { get; set; } = string.Empty;

    public long LedgerAccountId { get; set; }

    public Product? Product { get; set; }

    public LedgerAccount? LedgerAccount { get; set; }
}
