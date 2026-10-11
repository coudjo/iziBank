using Banking.Domain.Auditing;
using Banking.Domain.Institution;
using Banking.Domain.Ledger;
using Banking.Domain.Lookups;
using Banking.Domain.Security;

namespace Banking.Domain.Products;

public sealed class Product : AuditableEntity, ISupervisable
{
    public int ProductId { get; set; }

    public int BranchId { get; set; }

    public string ProductCode { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public int ProductTypeId { get; set; }

    public string AccountPrefix { get; set; } = string.Empty;

    public int CurrencyId { get; set; }

    public long? LedgerControlAccountId { get; set; }

    public decimal? CreditInterestRate { get; set; }

    public decimal? DebitInterestRate { get; set; }

    public decimal? MinimumBalance { get; set; }

    public short? AccountsPerCustomer { get; set; }

    public bool? IsFreezed { get; set; }

    public bool? IsChequeBookAllowed { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsSupervisionRequired { get; set; } = true;

    public long? SupervisedByUserId { get; set; }

    public DateTime? SupervisedOn { get; set; }

    public Branch? Branch { get; set; }

    public ProductType? ProductType { get; set; }

    public Currency? Currency { get; set; }

    public LedgerAccount? LedgerControlAccount { get; set; }

    public StaffUser? SupervisedByUser { get; set; }

    public ICollection<ProductInterestRate> InterestRates { get; } = new List<ProductInterestRate>();
}

public sealed class ProductInterestRate : AuditableEntity
{
    public long ProductInterestRateId { get; set; }

    public int ProductId { get; set; }

    public int? RateTypeId { get; set; }

    public decimal? MinimumAmount { get; set; }

    public decimal? MaximumAmount { get; set; }

    public decimal InterestRate { get; set; }

    public DateOnly EffectiveFrom { get; set; }

    public DateOnly? EffectiveTo { get; set; }

    public Product? Product { get; set; }

    public RateType? RateType { get; set; }
}
