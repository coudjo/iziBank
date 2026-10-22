using Banking.Domain.Auditing;
using Banking.Domain.Security;

namespace Banking.Domain.Supervision;

/// <summary>
/// A proposed change that takes effect only when a different supervisor approves it (addendum R10, ADR S1-d).
/// The live tables are untouched until approval, so the permission check never sees a pending change.
/// </summary>
public sealed class ChangeRequest : AuditableEntity
{
    public long ChangeRequestId { get; set; }

    public string EntityType { get; set; } = string.Empty;

    /// <summary>The target record. For a create it is a key that blocks a second pending create of the same thing.</summary>
    public string EntityId { get; set; } = string.Empty;

    public string ChangeType { get; set; } = string.Empty;

    public string Status { get; set; } = ChangeRequestStatuses.Pending;

    /// <summary>The state before the change, captured when it was requested (null for a create).</summary>
    public string? BeforeJson { get; set; }

    /// <summary>The proposed state.</summary>
    public string PayloadJson { get; set; } = string.Empty;

    public long RequestedByUserId { get; set; }

    public DateTime RequestedOn { get; set; }

    public long? DecidedByUserId { get; set; }

    public DateTime? DecidedOn { get; set; }

    public string? DecisionNote { get; set; }

    public StaffUser? RequestedByUser { get; set; }

    public StaffUser? DecidedByUser { get; set; }
}

public static class ChangeRequestStatuses
{
    public const string Pending = "PENDING";
    public const string Approved = "APPROVED";
    public const string Rejected = "REJECTED";
}

public static class ChangeRequestTypes
{
    public const string RoleGroupCreate = "ROLE_GROUP_CREATE";
    public const string RoleGroupUpdate = "ROLE_GROUP_UPDATE";
    public const string RoleGroupDeactivate = "ROLE_GROUP_DEACTIVATE";
    public const string StaffRoleGroupsReplace = "STAFF_ROLE_GROUPS_REPLACE";
    public const string LedgerAccountRename = "LEDGER_ACCOUNT_RENAME";
    public const string ProductUpdate = "PRODUCT_UPDATE";
    public const string RoleGroupLimitsReplace = "ROLE_GROUP_LIMITS";
    public const string BranchLedgerSettingsReplace = "BRANCH_LEDGER_SETTINGS";
    public const string ProductLedgerSettingsReplace = "PRODUCT_LEDGER_SETTINGS";
}

public static class ChangeRequestEntities
{
    public const string RoleGroup = "RoleGroup";
    public const string StaffBranchAssignment = "StaffBranchAssignment";
    public const string LedgerAccount = "LedgerAccount";
    public const string Product = "Product";
    public const string BranchLedgerSettings = "BranchLedgerSettings";
    public const string ProductLedgerSettings = "ProductLedgerSettings";

    /// <summary>Change requests handled by the security approval screens (SEC/PERMISSIONS).</summary>
    public static readonly IReadOnlyList<string> Security = [RoleGroup, StaffBranchAssignment];
}
