using Banking.Domain.Auditing;
using Banking.Domain.Institution;
using Banking.Domain.Ledger;
using Banking.Domain.Lookups;
using Banking.Domain.Parties;

namespace Banking.Domain.Security;

public sealed class Module : AuditableEntity
{
    public int ModuleId { get; set; }

    public string ModuleCode { get; set; } = string.Empty;

    public string ModuleName { get; set; } = string.Empty;

    public bool HasDataEntry { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<SubModule> SubModules { get; } = new List<SubModule>();
}

public sealed class SubModule : AuditableEntity
{
    public int SubModuleId { get; set; }

    public int ModuleId { get; set; }

    public string SubModuleCode { get; set; } = string.Empty;

    public string SubModuleName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public Module? Module { get; set; }
}

public sealed class StaffUser : AuditableEntity
{
    public long StaffUserId { get; set; }

    public int BranchId { get; set; }

    public string StaffCode { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public bool IsTeller { get; set; }

    public long? TellerGLAccountId { get; set; }

    public bool IsDisabled { get; set; }

    public string? SecretKeyHash { get; set; }

    public bool? CanChangeSecretKey { get; set; }

    public bool? ChangeKeyAtNextLogOn { get; set; }

    public DateTime? SecretKeyChangedOn { get; set; }

    public short? SecretKeyExpiryDays { get; set; }

    public bool? CanSwitchBranch { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public Branch? Branch { get; set; }

    public LedgerAccount? TellerGLAccount { get; set; }

    public ICollection<StaffUserRoleGroup> RoleGroups { get; } = new List<StaffUserRoleGroup>();
}

public sealed class CustomerDigitalAccess : AuditableEntity
{
    public long CustomerDigitalAccessId { get; set; }

    public long PartyId { get; set; }

    public string AccessCode { get; set; } = string.Empty;

    public string? SecretKeyHash { get; set; }

    public bool IsDisabled { get; set; }

    public Party? Party { get; set; }
}

public sealed class UserSession
{
    public long SessionId { get; set; }

    public long? StaffUserId { get; set; }

    public long? CustomerDigitalAccessId { get; set; }

    public DateTime LoginAt { get; set; }

    public DateTime? LogoutAt { get; set; }

    public int? LogoutReasonId { get; set; }

    public string? IPAddress { get; set; }

    public string? DeviceInfo { get; set; }

    public StaffUser? StaffUser { get; set; }

    public CustomerDigitalAccess? CustomerDigitalAccess { get; set; }

    public LogoutReasonType? LogoutReason { get; set; }
}

public sealed class EventType : AuditableEntity
{
    public int EventTypeId { get; set; }

    public string EventCode { get; set; } = string.Empty;

    public string EventName { get; set; } = string.Empty;
}

public sealed class UserActivityLog : ILedgerRecord
{
    public long EventId { get; set; }

    public long? SessionId { get; set; }

    public int? BranchId { get; set; }

    public int EventTypeId { get; set; }

    public string? EntityType { get; set; }

    public string? EntityId { get; set; }

    public string Outcome { get; set; } = string.Empty;

    public DateTime EventTimestamp { get; set; }

    public string? RequestDetailsJson { get; set; }

    public UserSession? Session { get; set; }

    public Branch? Branch { get; set; }

    public EventType? EventType { get; set; }
}

public sealed class DataChangeAuditLog : ILedgerRecord
{
    public long AuditId { get; set; }

    public long? StaffUserId { get; set; }

    public int? BranchId { get; set; }

    public string TableName { get; set; } = string.Empty;

    public string? RecordId { get; set; }

    public string ActionType { get; set; } = string.Empty;

    public string? OldValueJson { get; set; }

    public string? NewValueJson { get; set; }

    public DateTime LoggedOn { get; set; }

    public StaffUser? StaffUser { get; set; }

    public Branch? Branch { get; set; }
}

/// <summary>A named bundle of permissions. Changes need supervision (R10).</summary>
public sealed class RoleGroup : AuditableEntity, ISupervisable
{
    public int RoleGroupId { get; set; }

    public string RoleGroupCode { get; set; } = string.Empty;

    public string RoleGroupName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsSystemGroup { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsSupervisionRequired { get; set; } = true;

    public long? SupervisedByUserId { get; set; }

    public DateTime? SupervisedOn { get; set; }

    public ICollection<RoleGroupPermission> Permissions { get; } = new List<RoleGroupPermission>();

    public ICollection<StaffUserRoleGroup> Members { get; } = new List<StaffUserRoleGroup>();
}

/// <summary>
/// One row of the permission matrix. A null <see cref="SubModuleId"/> covers the whole module.
/// Permissions are a union across groups; there are no deny rows.
/// </summary>
public sealed class RoleGroupPermission : AuditableEntity
{
    public long RoleGroupPermissionId { get; set; }

    public int RoleGroupId { get; set; }

    public int ModuleId { get; set; }

    public int? SubModuleId { get; set; }

    public bool AllowView { get; set; }

    public bool AllowAdd { get; set; }

    public bool AllowEdit { get; set; }

    public bool AllowDelete { get; set; }

    public bool AllowSupervise { get; set; }

    public RoleGroup? RoleGroup { get; set; }

    public Module? Module { get; set; }

    public SubModule? SubModule { get; set; }
}

/// <summary>A staff member holds a role group in one branch (R2, R11).</summary>
public sealed class StaffUserRoleGroup : AuditableEntity, ISupervisable
{
    public long StaffUserRoleGroupId { get; set; }

    public long StaffUserId { get; set; }

    public int RoleGroupId { get; set; }

    public int BranchId { get; set; }

    public bool IsSupervisionRequired { get; set; } = true;

    public long? SupervisedByUserId { get; set; }

    public DateTime? SupervisedOn { get; set; }

    public StaffUser? StaffUser { get; set; }

    public RoleGroup? RoleGroup { get; set; }

    public Branch? Branch { get; set; }
}

/// <summary>
/// Stored result of an idempotent request (addendum section 4). Not audited; purged after
/// <see cref="ExpiresOn"/>.
/// </summary>
public sealed class IdempotencyKey
{
    public long IdempotencyKeyId { get; set; }

    public string KeyValue { get; set; } = string.Empty;

    public long StaffUserId { get; set; }

    public string Route { get; set; } = string.Empty;

    public string RequestHash { get; set; } = string.Empty;

    public int ResponseStatus { get; set; }

    public string? ResponseBody { get; set; }

    public DateTime CreatedOn { get; set; }

    public DateTime ExpiresOn { get; set; }

    public StaffUser? StaffUser { get; set; }
}
