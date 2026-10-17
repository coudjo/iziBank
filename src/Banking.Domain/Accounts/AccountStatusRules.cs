namespace Banking.Domain.Accounts;

public static class AccountStatuses
{
    public const string PendingSupervision = "PENDING_SUPERVISION";
    public const string Closed = "CLOSED";
    public const string Frozen = "FROZEN";
    public const string Dormant = "DORMANT";
    public const string Active = "ACTIVE";

    public static readonly IReadOnlyList<string> All = [PendingSupervision, Closed, Frozen, Dormant, Active];
}

/// <summary>Plan 6.10: the account status shown in responses is derived, first match in this order.</summary>
public static class AccountStatusRules
{
    public static string Derive(bool pendingSupervision, bool closed, bool frozen, bool dormant) =>
        pendingSupervision ? AccountStatuses.PendingSupervision
        : closed ? AccountStatuses.Closed
        : frozen ? AccountStatuses.Frozen
        : dormant ? AccountStatuses.Dormant
        : AccountStatuses.Active;
}
