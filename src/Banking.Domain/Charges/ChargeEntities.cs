using Banking.Domain.Auditing;
using Banking.Domain.Lookups;
using Banking.Domain.Products;

namespace Banking.Domain.Charges;

/// <summary>
/// A charge the institution can levy (plan B 2.4). Reference data: set up by an administrator, not supervised. How
/// much a product charges, and when, is <see cref="ProductCharge"/>, which is supervised.
/// </summary>
public sealed class ChargeCode : AuditableEntity
{
    public int ChargeId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public int ChargeCategoryId { get; set; }

    public bool IsActive { get; set; } = true;

    public ChargeCategoryType? ChargeCategory { get; set; }
}

/// <summary>
/// A product's charge for one condition, effective over a date range (plan B 2.4). Supervised: a new one waits for a
/// different supervisor, and approval ends an earlier approved one of the same charge and condition the day before.
/// </summary>
public sealed class ProductCharge : AuditableEntity, ISupervisable
{
    public long ProductChargeId { get; set; }

    public int ProductId { get; set; }

    public int ChargeId { get; set; }

    public int ChargeConditionId { get; set; }

    /// <summary>Transactions per calendar month that are not charged. Null means none are free.</summary>
    public short? FreeTransactions { get; set; }

    /// <summary>Null means the institution's <c>Charges:ShortfallPolicy</c> applies.</summary>
    public int? ShortfallPolicyId { get; set; }

    public DateOnly EffectiveFrom { get; set; }

    public DateOnly? EffectiveTo { get; set; }

    public bool IsSupervisionRequired { get; set; } = true;

    public long? SupervisedByUserId { get; set; }

    public DateTime? SupervisedOn { get; set; }

    public Product? Product { get; set; }

    public ChargeCode? ChargeCode { get; set; }

    public ChargeConditionType? ChargeCondition { get; set; }

    public ChargeShortfallPolicyType? ShortfallPolicy { get; set; }

    public ICollection<ProductChargeTier> Tiers { get; set; } = [];
}

/// <summary>
/// One band of a charge (plan B 2.4). Bands are ordered by <see cref="TierNumber"/>. <see cref="TransactionAmount"/> is
/// the band's inclusive upper limit on the transaction amount; the last band has none and takes everything above.
/// </summary>
public sealed class ProductChargeTier
{
    public long ProductChargeTierId { get; set; }

    public long ProductChargeId { get; set; }

    public short TierNumber { get; set; }

    public int ChargeBasisId { get; set; }

    public decimal? TransactionAmount { get; set; }

    /// <summary>A flat amount, or a percentage (0 to 100) of the transaction amount.</summary>
    public decimal AmountOrPercentage { get; set; }

    public decimal? MinimumCharge { get; set; }

    public decimal? MaximumCharge { get; set; }

    public ProductCharge? ProductCharge { get; set; }

    public ChargeBasisType? ChargeBasis { get; set; }
}
