using Banking.Domain.Auditing;
using Banking.Domain.Institution;
using Banking.Domain.Lookups;
using Banking.Domain.Parties;
using Banking.Domain.Products;
using Banking.Domain.Security;

namespace Banking.Domain.Accounts;

public sealed class Account : AuditableEntity, ISupervisable
{
    public long AccountId { get; set; }

    public string AccountNumber { get; set; } = string.Empty;

    public int BranchId { get; set; }

    public int ProductId { get; set; }

    public string AccountName { get; set; } = string.Empty;

    public DateOnly DateOpened { get; set; }

    public long? OpenedByUserId { get; set; }

    public int? OperatingModeId { get; set; }

    public DateTime? ClosedOn { get; set; }

    public long? ClosedByUserId { get; set; }

    public int? CloseReasonId { get; set; }

    public bool IsDormant { get; set; }

    public bool IsSupervisionRequired { get; set; } = true;

    public long? SupervisedByUserId { get; set; }

    public DateTime? SupervisedOn { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public Branch? Branch { get; set; }

    public Product? Product { get; set; }

    public StaffUser? OpenedByUser { get; set; }

    public OperatingModeType? OperatingMode { get; set; }

    public StaffUser? ClosedByUser { get; set; }

    public CloseReasonType? CloseReason { get; set; }

    public StaffUser? SupervisedByUser { get; set; }

    public AccountBalance? Balance { get; set; }

    public AccountCondition? Condition { get; set; }

    public ICollection<AccountHolder> Holders { get; } = new List<AccountHolder>();

    public ICollection<AccountReminder> Reminders { get; } = new List<AccountReminder>();

    public ICollection<AccountClassificationAssignment> Classifications { get; } = new List<AccountClassificationAssignment>();

    public ICollection<FreezeReleaseAccountBalance> FreezeReleases { get; } = new List<FreezeReleaseAccountBalance>();
}

public sealed class AccountHolder : AuditableEntity
{
    public long AccountHolderId { get; set; }

    public long AccountId { get; set; }

    public long PartyId { get; set; }

    public int AccountHolderRoleId { get; set; }

    public int? OperatingInstructionId { get; set; }

    public Account? Account { get; set; }

    public Party? Party { get; set; }

    public AccountHolderRoleType? Role { get; set; }

    public OperatingInstructionType? OperatingInstruction { get; set; }
}

public sealed class AccountBalance
{
    public long AccountId { get; set; }

    public decimal OpeningBalance { get; set; }

    public decimal ClearBalance { get; set; }

    public decimal FreezedAmount { get; set; }

    public decimal UnsupervisedCredit { get; set; }

    public decimal UnsupervisedDebit { get; set; }

    public decimal OverdrawLimit { get; set; }

    public decimal AvailableBalance { get; set; }

    public decimal TotalBalance { get; set; }

    public DateTime? LastCreditTransactionDate { get; set; }

    public DateTime? LastDebitTransactionDate { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public Account? Account { get; set; }
}

public sealed class AccountBalanceDailySnapshot
{
    public long SnapshotId { get; set; }

    public long AccountId { get; set; }

    public DateOnly SnapshotDate { get; set; }

    public decimal ClosingBalance { get; set; }

    public Account? Account { get; set; }
}

public sealed class AccountClassification : AuditableEntity
{
    public int AccountClassificationId { get; set; }

    public string ClassificationCode { get; set; } = string.Empty;

    public string ClassificationName { get; set; } = string.Empty;

    public ICollection<AccountSubClassification> SubClassifications { get; } = new List<AccountSubClassification>();
}

public sealed class AccountSubClassification : AuditableEntity
{
    public int AccountSubClassificationId { get; set; }

    public int AccountClassificationId { get; set; }

    public string SubClassificationCode { get; set; } = string.Empty;

    public string SubClassificationName { get; set; } = string.Empty;

    public AccountClassification? Classification { get; set; }
}

public sealed class AccountClassificationAssignment : AuditableEntity
{
    public long AssignmentId { get; set; }

    public long AccountId { get; set; }

    public int AccountClassificationId { get; set; }

    public int? AccountSubClassificationId { get; set; }

    public Account? Account { get; set; }

    public AccountClassification? Classification { get; set; }

    public AccountSubClassification? SubClassification { get; set; }
}

public sealed class AccountCondition : AuditableEntity
{
    public long AccountId { get; set; }

    public bool IsFreezed { get; set; }

    public bool DoNotAllowCredit { get; set; }

    public bool DoNotAllowDebit { get; set; }

    public decimal? CreditLimit { get; set; }

    public decimal? DebitLimit { get; set; }

    public bool SuperviseAllTransactions { get; set; }

    public bool DoNotApplyTransactionCharges { get; set; }

    public Account? Account { get; set; }
}

public sealed class AccountReminder : AuditableEntity
{
    public long AccountReminderId { get; set; }

    public long AccountId { get; set; }

    public string ReminderText { get; set; } = string.Empty;

    public Account? Account { get; set; }
}

public sealed class DormantAccountHistory : AuditableEntity
{
    public long DormantHistoryId { get; set; }

    public long AccountId { get; set; }

    public long? ActivatedByUserId { get; set; }

    public DateTime DateActivated { get; set; }

    public Account? Account { get; set; }

    public StaffUser? ActivatedByUser { get; set; }
}

public sealed class FreezeReleaseAccountBalance : AuditableEntity
{
    public long FreezeReleaseId { get; set; }

    public long AccountId { get; set; }

    public int FreezeReleaseActionId { get; set; }

    public decimal? Amount { get; set; }

    public string? Remarks { get; set; }

    public Account? Account { get; set; }

    public FreezeReleaseActionType? Action { get; set; }
}
