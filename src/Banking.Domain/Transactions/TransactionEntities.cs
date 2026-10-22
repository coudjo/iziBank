using Banking.Domain.Accounts;
using Banking.Domain.Auditing;
using Banking.Domain.Institution;
using Banking.Domain.Ledger;
using Banking.Domain.Security;

namespace Banking.Domain.Transactions;

/// <summary>Plan B 2.1: the side of a ledger line.</summary>
public static class EntrySides
{
    public const string Credit = "C";
    public const string Debit = "D";
}

/// <summary>Plan B 2.1: where a posting request stands.</summary>
public static class PostingRequestStatuses
{
    public const string Pending = "PENDING";
    public const string Posted = "POSTED";
    public const string Rejected = "REJECTED";
    public const string Cancelled = "CANCELLED";
    public const string Expired = "EXPIRED";

    public static readonly IReadOnlyList<string> All = [Pending, Posted, Rejected, Cancelled, Expired];
}

/// <summary>What a posting is (plan B section 7). System codes are seeded; the institution may add its own later.</summary>
public sealed class TransactionCode : AuditableEntity
{
    public int TransactionCodeId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>One of <see cref="TransactionCategories"/>.</summary>
    public string Category { get; set; } = string.Empty;

    public string? DefaultNarration { get; set; }

    public bool IsSystemCode { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Plan B 2.1: the maker-checker record for every money movement. Mutable; the ledger rows are written only when it is
/// posted (ADR B-1).
/// </summary>
public sealed class PostingRequest : AuditableEntity
{
    public long PostingRequestId { get; set; }

    public string Reference { get; set; } = string.Empty;

    public int BranchId { get; set; }

    public DateOnly WorkingDate { get; set; }

    public int TransactionCodeId { get; set; }

    public decimal Amount { get; set; }

    /// <summary>The validated request, shown to the approver field by field.</summary>
    public string PayloadJson { get; set; } = string.Empty;

    public string Status { get; set; } = PostingRequestStatuses.Pending;

    /// <summary>Set when posted. The foreign key is created with the ledger tables (they are not in EF migrations).</summary>
    public long? PostedTransactionId { get; set; }

    public long RequestedByUserId { get; set; }

    public DateTime RequestedOn { get; set; }

    public long? SupervisedByUserId { get; set; }

    public DateTime? SupervisedOn { get; set; }

    public string? DecisionReason { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public Branch? Branch { get; set; }

    public TransactionCode? TransactionCode { get; set; }

    public StaffUser? RequestedByUser { get; set; }

    public StaffUser? SupervisedByUser { get; set; }
}

/// <summary>Plan B 2.1: one posting, the journal header. Append-only ledger table.</summary>
public sealed class PostedTransaction : ILedgerRecord
{
    public long PostedTransactionId { get; set; }

    public string Reference { get; set; } = string.Empty;

    public long? PostingRequestId { get; set; }

    public int BranchId { get; set; }

    public DateOnly EntryDate { get; set; }

    public int TransactionCodeId { get; set; }

    public string? Narration { get; set; }

    public long? ReversesTransactionId { get; set; }

    public long? OperatorStaffUserId { get; set; }

    public long? SupervisorStaffUserId { get; set; }

    public DateTime PostedOn { get; set; }

    public Branch? Branch { get; set; }

    public TransactionCode? TransactionCode { get; set; }

    public PostingRequest? PostingRequest { get; set; }

    public ICollection<AccountLedgerEntry> AccountEntries { get; } = new List<AccountLedgerEntry>();

    public ICollection<LedgerEntry> LedgerEntries { get; } = new List<LedgerEntry>();
}

/// <summary>Plan B 2.1: a line on a customer account (the sub-ledger). Append-only ledger table.</summary>
public sealed class AccountLedgerEntry : ILedgerRecord
{
    public long AccountLedgerEntryId { get; set; }

    public long PostedTransactionId { get; set; }

    public long AccountId { get; set; }

    /// <summary>The account's branch.</summary>
    public int BranchId { get; set; }

    public DateOnly EntryDate { get; set; }

    /// <summary><see cref="EntrySides.Credit"/> or <see cref="EntrySides.Debit"/>.</summary>
    public string CrDb { get; set; } = string.Empty;

    /// <summary>Always positive.</summary>
    public decimal Amount { get; set; }

    /// <summary>The account's clear balance after this line, written under the balance lock.</summary>
    public decimal BalanceAfter { get; set; }

    public int TransactionCodeId { get; set; }

    public PostedTransaction? Transaction { get; set; }

    public Account? Account { get; set; }
}

/// <summary>Plan B 2.1: a general ledger line. Every branch balances on its own (ADR B-3). Append-only ledger table.</summary>
public sealed class LedgerEntry : ILedgerRecord
{
    public long LedgerEntryId { get; set; }

    public long PostedTransactionId { get; set; }

    public long LedgerAccountId { get; set; }

    /// <summary>The branch the line belongs to.</summary>
    public int BranchId { get; set; }

    public DateOnly EntryDate { get; set; }

    public string CrDb { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public PostedTransaction? Transaction { get; set; }

    public LedgerAccount? LedgerAccount { get; set; }
}

/// <summary>Plan B 2.1: a ledger account's closing balance in one branch on one business day, written by end of day.</summary>
public sealed class LedgerAccountDailyBalance
{
    public long LedgerAccountDailyBalanceId { get; set; }

    public long LedgerAccountId { get; set; }

    public int BranchId { get; set; }

    public DateOnly BalanceDate { get; set; }

    public decimal ClosingBalance { get; set; }

    public LedgerAccount? LedgerAccount { get; set; }

    public Branch? Branch { get; set; }
}
