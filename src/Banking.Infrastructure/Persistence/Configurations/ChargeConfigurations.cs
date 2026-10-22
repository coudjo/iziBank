using Banking.Domain.Charges;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Banking.Infrastructure.Persistence.Configurations;

internal sealed class ChargeCodeConfiguration : IEntityTypeConfiguration<ChargeCode>
{
    public void Configure(EntityTypeBuilder<ChargeCode> builder)
    {
        builder.ToTable(nameof(ChargeCode));
        builder.HasKey(entity => entity.ChargeId);
        builder.Property(entity => entity.Code).IsVarchar(10);
        builder.HasIndex(entity => entity.Code).IsUnique().HasDatabaseName("UX_ChargeCode_Code");
        builder.Property(entity => entity.Description).IsVarchar(100);
        builder.Property(entity => entity.IsActive).HasDefaultValue(true);

        builder.HasOne(entity => entity.ChargeCategory)
            .WithMany()
            .HasForeignKey(entity => entity.ChargeCategoryId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class ProductChargeConfiguration : IEntityTypeConfiguration<ProductCharge>
{
    public void Configure(EntityTypeBuilder<ProductCharge> builder)
    {
        builder.ToTable(nameof(ProductCharge), table =>
        {
            table.HasCheckConstraint("CK_ProductCharge_EffectiveDates", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
            table.HasCheckConstraint("CK_ProductCharge_FreeTransactions", "[FreeTransactions] IS NULL OR [FreeTransactions] >= 0");
        });
        builder.HasKey(entity => entity.ProductChargeId);
        builder.Property(entity => entity.IsSupervisionRequired).HasDefaultValue(true);

        // One charge per product, charge and condition for each start date.
        builder.HasIndex(entity => new { entity.ProductId, entity.ChargeId, entity.ChargeConditionId, entity.EffectiveFrom })
            .IsUnique()
            .HasDatabaseName("UX_ProductCharge_Product_Charge_Condition_From");

        builder.HasOne(entity => entity.Product)
            .WithMany()
            .HasForeignKey(entity => entity.ProductId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.ChargeCode)
            .WithMany()
            .HasForeignKey(entity => entity.ChargeId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.ChargeCondition)
            .WithMany()
            .HasForeignKey(entity => entity.ChargeConditionId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.ShortfallPolicy)
            .WithMany()
            .HasForeignKey(entity => entity.ShortfallPolicyId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasMany(entity => entity.Tiers)
            .WithOne(tier => tier.ProductCharge)
            .HasForeignKey(tier => tier.ProductChargeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ProductChargeTierConfiguration : IEntityTypeConfiguration<ProductChargeTier>
{
    public void Configure(EntityTypeBuilder<ProductChargeTier> builder)
    {
        builder.ToTable(nameof(ProductChargeTier), table =>
        {
            table.HasCheckConstraint("CK_ProductChargeTier_Amount", "[AmountOrPercentage] > 0");
            table.HasCheckConstraint("CK_ProductChargeTier_MinMax", "[MinimumCharge] IS NULL OR [MaximumCharge] IS NULL OR [MaximumCharge] >= [MinimumCharge]");
        });
        builder.HasKey(entity => entity.ProductChargeTierId);
        builder.HasIndex(entity => new { entity.ProductChargeId, entity.TierNumber })
            .IsUnique()
            .HasDatabaseName("UX_ProductChargeTier_Charge_Tier");

        builder.HasOne(entity => entity.ChargeBasis)
            .WithMany()
            .HasForeignKey(entity => entity.ChargeBasisId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
