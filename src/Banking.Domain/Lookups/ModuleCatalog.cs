namespace Banking.Domain.Lookups;

public static class ModuleCatalog
{
    public const string Security = "SEC";
    public const string ReferenceData = "REF";
    public const string Party = "PTY";
    public const string Accounts = "ACC";
    public const string Ledger = "LED";
    public const string Transactions = "TXN";

    public static readonly IReadOnlyList<ModuleDefinition> All =
    [
        new(Security, "Security", HasDataEntry: true,
        [
            new("USERS", "Users"),
            new("PERMISSIONS", "Permissions"),
            new("SESSIONS", "Sessions"),
            new("AUDIT", "Audit")
        ]),
        new(ReferenceData, "Reference data", HasDataEntry: true,
        [
            new("INSTITUTION", "Institution"),
            new("BRANCHES", "Branches"),
            new("CURRENCIES", "Currencies"),
            new("CALENDAR", "Calendar"),
            new("LOOKUPS", "Lookups"),
            new("EOD", "End of day")
        ]),
        new(Party, "Party and KYC", HasDataEntry: true,
        [
            new("PARTY", "Party"),
            new("KYC", "KYC"),
            new("DOCUMENTS", "Documents"),
            new("GROUPS", "Groups"),
            new("CREDIT_OFFICERS", "Credit officers")
        ]),
        new(Accounts, "Accounts", HasDataEntry: true,
        [
            new("PRODUCT", "Product"),
            new("ACCOUNT", "Account"),
            new("CONDITION", "Condition"),
            new("CLASSIFICATION", "Classification"),
            new("CHARGES", "Charges")
        ]),
        new(Ledger, "Ledger", HasDataEntry: true,
        [
            new("CHART", "Chart"),
            new("JOURNAL", "Journals"),
            new("SETTINGS", "Ledger settings"),
            new("REPORTS", "Ledger reports")
        ]),
        new(Transactions, "Transactions", HasDataEntry: true,
        [
            new("CASH", "Cash"),
            new("TRANSFER", "Transfers"),
            new("TILL", "Till"),
            new("VAULT", "Vault"),
            new("REVERSAL", "Reversals")
        ])
    ];
}

public sealed record ModuleDefinition(
    string Code,
    string Name,
    bool HasDataEntry,
    IReadOnlyList<SubModuleDefinition> SubModules);

public sealed record SubModuleDefinition(string Code, string Name);
