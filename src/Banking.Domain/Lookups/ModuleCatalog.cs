namespace Banking.Domain.Lookups;

public static class ModuleCatalog
{
    public const string Security = "SEC";
    public const string ReferenceData = "REF";
    public const string Party = "PTY";
    public const string Accounts = "ACC";
    public const string Ledger = "LED";

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
            new("LOOKUPS", "Lookups")
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
            new("CLASSIFICATION", "Classification")
        ]),
        new(Ledger, "Ledger", HasDataEntry: true,
        [
            new("CHART", "Chart")
        ])
    ];
}

public sealed record ModuleDefinition(
    string Code,
    string Name,
    bool HasDataEntry,
    IReadOnlyList<SubModuleDefinition> SubModules);

public sealed record SubModuleDefinition(string Code, string Name);
