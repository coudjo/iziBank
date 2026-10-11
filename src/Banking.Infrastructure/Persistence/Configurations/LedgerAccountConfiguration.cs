using Banking.Domain.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Banking.Infrastructure.Persistence.Configurations;

internal sealed class LedgerAccountConfiguration : IEntityTypeConfiguration<LedgerAccount>
{
    public void Configure(EntityTypeBuilder<LedgerAccount> builder)
    {
        builder.ToTable(nameof(LedgerAccount));
        builder.HasKey(entity => entity.LedgerAccountId);
        builder.Property(entity => entity.LedgerAccountCode).IsVarchar(20);
        builder.HasIndex(entity => entity.LedgerAccountCode).IsUnique();
        builder.Property(entity => entity.AccountName).IsVarchar(100);
        builder.Property(entity => entity.Balance).HasDefaultValue(0m);
        builder.Property(entity => entity.IsSupervisionRequired).HasDefaultValue(true);
        builder.Property(entity => entity.RowVersion).IsRowVersion();

        builder.HasOne(entity => entity.Branch)
            .WithMany()
            .HasForeignKey(entity => entity.BranchId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.LedgerAccountType)
            .WithMany()
            .HasForeignKey(entity => entity.LedgerAccountTypeId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.Currency)
            .WithMany()
            .HasForeignKey(entity => entity.CurrencyId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.SupervisedByUser)
            .WithMany()
            .HasForeignKey(entity => entity.SupervisedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
