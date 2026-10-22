using Banking.Domain.Lookups;

namespace Banking.Domain.Security;

/// <summary>
/// Seeded role groups (addendum 2.5). Each is a starting matrix that administrators edit after install.
/// A null sub-module code means the whole module.
/// </summary>
public static class RoleGroupCatalog
{
    public const string Teller = "TELLER";
    public const string FieldOfficer = "FIELD_OFFICER";
    public const string KycOfficer = "KYC_OFFICER";
    public const string Supervisor = "SUPERVISOR";
    public const string Administrator = "ADMINISTRATOR";
    public const string Auditor = "AUDITOR";
    public const string Accountant = "ACCOUNTANT";

    private const string View = "V";
    private const string Add = "A";
    private const string Edit = "E";
    private const string Delete = "D";
    private const string Supervise = "S";

    public static readonly IReadOnlyList<RoleGroupDefinition> All =
    [
        new(Teller, "Teller", "Counter staff: look up and register customers, open accounts, take deposits and pay withdrawals.",
        [
            Grant(ModuleCatalog.Party, "PARTY", View, Add),
            Grant(ModuleCatalog.Accounts, "PRODUCT", View),
            Grant(ModuleCatalog.Accounts, "ACCOUNT", View, Add, Edit),
            Grant(ModuleCatalog.Accounts, "CONDITION", View),
            Grant(ModuleCatalog.Ledger, "CHART", View),
            Grant(ModuleCatalog.Transactions, "CASH", View, Add),
            Grant(ModuleCatalog.Transactions, "TRANSFER", View, Add),
            Grant(ModuleCatalog.Transactions, "TILL", View, Add)
        ]),
        new(FieldOfficer, "Field officer", "Registers customers and collects documents in the field.",
        [
            Grant(ModuleCatalog.Party, "PARTY", View, Add, Edit),
            Grant(ModuleCatalog.Party, "DOCUMENTS", View, Add, Edit),
            Grant(ModuleCatalog.Party, "GROUPS", View, Add, Edit),
            Grant(ModuleCatalog.Party, "CREDIT_OFFICERS", View)
        ]),
        new(KycOfficer, "KYC officer", "Verifies customers and approves their records in one step.",
        [
            Grant(ModuleCatalog.Party, null, View),
            Grant(ModuleCatalog.Party, "DOCUMENTS", View, Edit),
            Grant(ModuleCatalog.Party, "PARTY", View, Supervise),
            Grant(ModuleCatalog.Party, "KYC", View, Supervise)
        ]),
        new(Supervisor, "Supervisor", "Approves customer, account, condition and chart changes, postings over limits, vault movements and reversals; runs end of day.",
        [
            Grant(ModuleCatalog.Party, null, View, Add, Edit, Supervise),
            Grant(ModuleCatalog.Accounts, null, View, Add, Edit, Supervise),
            Grant(ModuleCatalog.Accounts, "ACCOUNT", View, Delete),
            Grant(ModuleCatalog.Accounts, "CONDITION", View, Edit, Supervise),
            Grant(ModuleCatalog.ReferenceData, "CALENDAR", View, Edit, Supervise),
            Grant(ModuleCatalog.ReferenceData, "EOD", View, Add),
            Grant(ModuleCatalog.Ledger, "CHART", View, Add, Supervise),
            Grant(ModuleCatalog.Ledger, "JOURNAL", View, Add, Supervise),
            Grant(ModuleCatalog.Ledger, "REPORTS", View),
            Grant(ModuleCatalog.Transactions, null, View, Add, Supervise)
        ]),
        new(Administrator, "Administrator", "Manages users, role groups, reference data and products.",
        [
            Grant(ModuleCatalog.Security, null, View, Add, Edit, Delete),
            Grant(ModuleCatalog.Security, "PERMISSIONS", Supervise),
            Grant(ModuleCatalog.ReferenceData, null, View, Add, Edit, Delete),
            Grant(ModuleCatalog.Accounts, "PRODUCT", View, Add, Edit, Delete, Supervise),
            Grant(ModuleCatalog.Ledger, "CHART", View, Add, Edit, Delete, Supervise),
            Grant(ModuleCatalog.Ledger, "SETTINGS", View, Edit),
            Grant(ModuleCatalog.Accounts, "CHARGES", View, Edit)
        ]),
        new(Auditor, "Auditor", "Read-only access to audit, sessions and users.",
        [
            Grant(ModuleCatalog.Security, "AUDIT", View),
            Grant(ModuleCatalog.Security, "SESSIONS", View),
            Grant(ModuleCatalog.Security, "USERS", View),
            Grant(ModuleCatalog.Ledger, "REPORTS", View)
        ]),
        new(Accountant, "Accountant", "Keeps the books: journals, ledger settings, charge rules and ledger reports.",
        [
            Grant(ModuleCatalog.Ledger, "CHART", View),
            Grant(ModuleCatalog.Ledger, "JOURNAL", View, Add),
            Grant(ModuleCatalog.Ledger, "SETTINGS", View, Edit, Supervise),
            Grant(ModuleCatalog.Ledger, "REPORTS", View),
            Grant(ModuleCatalog.Accounts, "PRODUCT", View),
            Grant(ModuleCatalog.Accounts, "CHARGES", View, Edit, Supervise)
        ])
    ];

    /// <summary>Groups the bootstrap administrator holds so the institution can start and never locks out.</summary>
    public static readonly IReadOnlyList<string> BootstrapAdministratorGroups = [Administrator, Supervisor];

    private static PermissionGrant Grant(string module, string? subModule, params string[] flags)
    {
        var all = string.Concat(flags);
        return new PermissionGrant(
            module,
            subModule,
            AllowView: all.Length > 0,
            AllowAdd: all.Contains('A', StringComparison.Ordinal),
            AllowEdit: all.Contains('E', StringComparison.Ordinal),
            AllowDelete: all.Contains('D', StringComparison.Ordinal),
            AllowSupervise: all.Contains('S', StringComparison.Ordinal));
    }
}

public sealed record RoleGroupDefinition(
    string Code,
    string Name,
    string Description,
    IReadOnlyList<PermissionGrant> Permissions);

public sealed record PermissionGrant(
    string ModuleCode,
    string? SubModuleCode,
    bool AllowView,
    bool AllowAdd,
    bool AllowEdit,
    bool AllowDelete,
    bool AllowSupervise);
