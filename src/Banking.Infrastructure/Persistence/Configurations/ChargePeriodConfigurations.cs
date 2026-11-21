using Banking.Domain.Accounts;
using Banking.Domain.Charges;
using Banking.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Banking.Infrastructure.Persistence.Configurations;

internal sealed class AccountChargePeriodConfiguration : IEntityTypeConfiguration<AccountChargePeriod>
{
    public void Configure(EntityTypeBuilder<AccountChargePeriod> builder)
    {
        builder.ToTable(nameof(AccountChargePeriod));
        builder.HasKey(entity => entity.AccountChargePeriodId);
        builder.Property(entity => entity.AccountChargePeriodId).UseIdentityColumn();
        builder.Property(entity => entity.FeeAmount).HasPrecision(19, 2);
        builder.Property(entity => entity.ChargedAmount).HasPrecision(19, 2);
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_AccountChargePeriod_Amounts",
            "[PeriodStart] <= [PeriodEnd] AND [FeeAmount] > 0 AND [ChargedAmount] >= 0 AND [ChargedAmount] <= [FeeAmount]"));
        // One periodic fee per account and period start: a repeated run cannot charge the same month twice.
        builder.HasIndex(entity => new { entity.AccountId, entity.PeriodStart })
            .IsUnique()
            .HasDatabaseName("UX_AccountChargePeriod_Account_Start");
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(entity => entity.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductCharge>()
            .WithMany()
            .HasForeignKey(entity => entity.ProductChargeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PostedTransaction>()
            .WithMany()
            .HasForeignKey(entity => entity.PostedTransactionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
