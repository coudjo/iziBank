using Banking.Domain.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Banking.Infrastructure.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable(nameof(Product));
        builder.HasKey(entity => entity.ProductId);
        builder.Property(entity => entity.ProductCode).IsVarchar(10);
        builder.HasIndex(entity => entity.ProductCode).IsUnique();
        builder.Property(entity => entity.ProductName).IsVarchar(100);
        builder.Property(entity => entity.AccountPrefix).IsVarchar(6);
        builder.Property(entity => entity.CreditInterestRate).HasPrecision(9, 4);
        builder.Property(entity => entity.DebitInterestRate).HasPrecision(9, 4);
        builder.Property(entity => entity.IsActive).HasDefaultValue(true);
        builder.Property(entity => entity.IsSupervisionRequired).HasDefaultValue(true);

        builder.HasOne(entity => entity.Branch)
            .WithMany()
            .HasForeignKey(entity => entity.BranchId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.ProductType)
            .WithMany()
            .HasForeignKey(entity => entity.ProductTypeId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.Currency)
            .WithMany()
            .HasForeignKey(entity => entity.CurrencyId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.LedgerControlAccount)
            .WithMany()
            .HasForeignKey(entity => entity.LedgerControlAccountId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.SupervisedByUser)
            .WithMany()
            .HasForeignKey(entity => entity.SupervisedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class ProductInterestRateConfiguration : IEntityTypeConfiguration<ProductInterestRate>
{
    public void Configure(EntityTypeBuilder<ProductInterestRate> builder)
    {
        builder.ToTable(nameof(ProductInterestRate), table =>
        {
            table.HasCheckConstraint(
                "CK_ProductInterestRate_EffectiveDates",
                "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
            table.HasCheckConstraint(
                "CK_ProductInterestRate_AmountBand",
                "[MinimumAmount] IS NULL OR [MaximumAmount] IS NULL OR [MinimumAmount] <= [MaximumAmount]");
        });

        builder.HasKey(entity => entity.ProductInterestRateId);
        builder.Property(entity => entity.InterestRate).HasPrecision(9, 4);
        builder.HasIndex(entity => new { entity.ProductId, entity.RateTypeId, entity.EffectiveFrom })
            .HasDatabaseName("IX_ProductInterestRate_ProductId_RateTypeId_EffectiveFrom");

        builder.HasOne(entity => entity.Product)
            .WithMany(product => product.InterestRates)
            .HasForeignKey(entity => entity.ProductId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.RateType)
            .WithMany()
            .HasForeignKey(entity => entity.RateTypeId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
