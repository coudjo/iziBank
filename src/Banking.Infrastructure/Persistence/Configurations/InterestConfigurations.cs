using Banking.Domain.Interest;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Banking.Infrastructure.Persistence.Configurations;

internal sealed class AccountInterestAccrualConfiguration : IEntityTypeConfiguration<AccountInterestAccrual>
{
    public void Configure(EntityTypeBuilder<AccountInterestAccrual> builder)
    {
        builder.ToTable(nameof(AccountInterestAccrual));
        builder.HasKey(entity => entity.AccountInterestAccrualId);
        builder.Property(entity => entity.AccountInterestAccrualId).UseIdentityColumn();
        builder.Property(entity => entity.Balance).HasPrecision(19, 4);
        builder.Property(entity => entity.Rate).HasPrecision(9, 4);
        builder.Property(entity => entity.Amount).HasPrecision(19, 6);
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_AccountInterestAccrual_Amounts",
            "[Rate] >= 0 AND [Amount] >= 0"));
        builder.HasIndex(entity => new { entity.AccountId, entity.AccrualDate })
            .IsUnique()
            .HasDatabaseName("UX_AccountInterestAccrual_Account_Date");
        builder.HasOne(entity => entity.Account)
            .WithMany()
            .HasForeignKey(entity => entity.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
