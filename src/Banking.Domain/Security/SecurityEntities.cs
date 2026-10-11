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

    public ICollection<UserPermission> Permissions { get; } = new List<UserPermission>();
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

public sealed class UserPermission : AuditableEntity
{
    public long UserPermissionId { get; set; }

    public long StaffUserId { get; set; }

    public int BranchId { get; set; }

    public int ModuleId { get; set; }

    public int? SubModuleId { get; set; }

    public bool? AllowAdd { get; set; }

    public bool? AllowEdit { get; set; }

    public bool? AllowDelete { get; set; }

    public bool? CanSupervise { get; set; }

    public decimal? CreditLimit { get; set; }

    public decimal? DebitLimit { get; set; }

    public decimal? CreditSupervisionLimit { get; set; }

    public decimal? DebitSupervisionLimit { get; set; }

    public StaffUser? StaffUser { get; set; }

    public Branch? Branch { get; set; }

    public Module? Module { get; set; }

    public SubModule? SubModule { get; set; }
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
