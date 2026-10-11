using Banking.Domain.Auditing;
using Banking.Domain.Institution;
using Banking.Domain.Lookups;
using Banking.Domain.Security;

namespace Banking.Domain.Ledger;

public sealed class LedgerAccount : AuditableEntity, ISupervisable
{
    public long LedgerAccountId { get; set; }

    public int BranchId { get; set; }

    public string LedgerAccountCode { get; set; } = string.Empty;

    public string AccountName { get; set; } = string.Empty;

    public int? LedgerAccountTypeId { get; set; }

    public int? CurrencyId { get; set; }

    public decimal Balance { get; set; }

    public bool IsSupervisionRequired { get; set; } = true;

    public long? SupervisedByUserId { get; set; }

    public DateTime? SupervisedOn { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public Branch? Branch { get; set; }

    public LedgerAccountType? LedgerAccountType { get; set; }

    public Currency? Currency { get; set; }

    public StaffUser? SupervisedByUser { get; set; }
}
