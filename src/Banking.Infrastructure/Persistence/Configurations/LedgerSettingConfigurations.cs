using Banking.Domain.Ledger;
using Banking.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Banking.Infrastructure.Persistence.Configurations;

internal sealed class SystemLedgerSettingConfiguration : IEntityTypeConfiguration<SystemLedgerSetting>
{
    public void Configure(EntityTypeBuilder<SystemLedgerSetting> builder)
    {
        builder.ToTable(nameof(SystemLedgerSetting));
        builder.HasKey(entity => entity.SystemLedgerSettingId);
        builder.Property(entity => entity.SettingCode).IsVarchar(LedgerSettingCodes.MaxCodeLength);
        builder.HasIndex(entity => new { entity.BranchId, entity.SettingCode })
            .IsUnique()
            .HasDatabaseName("UX_SystemLedgerSetting_Branch_Code");

        builder.HasOne(entity => entity.Branch)
            .WithMany()
            .HasForeignKey(entity => entity.BranchId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.LedgerAccount)
            .WithMany()
            .HasForeignKey(entity => entity.LedgerAccountId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class ProductLedgerSettingConfiguration : IEntityTypeConfiguration<ProductLedgerSetting>
{
    public void Configure(EntityTypeBuilder<ProductLedgerSetting> builder)
    {
        builder.ToTable(nameof(ProductLedgerSetting));
        builder.HasKey(entity => entity.ProductLedgerSettingId);
        builder.Property(entity => entity.SettingCode).IsVarchar(LedgerSettingCodes.MaxCodeLength);
        builder.HasIndex(entity => new { entity.ProductId, entity.SettingCode })
            .IsUnique()
            .HasDatabaseName("UX_ProductLedgerSetting_Product_Code");

        builder.HasOne(entity => entity.Product)
            .WithMany()
            .HasForeignKey(entity => entity.ProductId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.LedgerAccount)
            .WithMany()
            .HasForeignKey(entity => entity.LedgerAccountId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class RoleGroupTransactionLimitConfiguration : IEntityTypeConfiguration<RoleGroupTransactionLimit>
{
    public void Configure(EntityTypeBuilder<RoleGroupTransactionLimit> builder)
    {
        builder.ToTable(nameof(RoleGroupTransactionLimit), table =>
            table.HasCheckConstraint("CK_RoleGroupTransactionLimit_MaxAmount", "[MaxAmount] > 0"));
        builder.HasKey(entity => entity.RoleGroupTransactionLimitId);
        builder.Property(entity => entity.LimitCode).IsVarchar(30);
        builder.HasIndex(entity => new { entity.RoleGroupId, entity.LimitCode })
            .IsUnique()
            .HasDatabaseName("UX_RoleGroupTransactionLimit_Group_Code");

        builder.HasOne(entity => entity.RoleGroup)
            .WithMany(group => group.TransactionLimits)
            .HasForeignKey(entity => entity.RoleGroupId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
