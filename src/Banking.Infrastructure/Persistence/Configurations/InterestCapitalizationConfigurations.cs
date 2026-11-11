using Banking.Domain.Accounts;
using Banking.Domain.Interest;
using Banking.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Banking.Infrastructure.Persistence.Configurations;

internal sealed class AccountInterestCapitalizationConfiguration : IEntityTypeConfiguration<AccountInterestCapitalization>
{
    public void Configure(EntityTypeBuilder<AccountInterestCapitalization> builder)
    {
        builder.ToTable(nameof(AccountInterestCapitalization));
        builder.HasKey(entity => entity.AccountInterestCapitalizationId);
        builder.Property(entity => entity.AccountInterestCapitalizationId).UseIdentityColumn();
        builder.Property(entity => entity.Amount).HasPrecision(19, 2);
        builder.Property(entity => entity.TaxAmount).HasPrecision(19, 2);
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_AccountInterestCapitalization_Amounts",
            "[FromDate] <= [ToDate] AND [Amount] >= 0 AND [TaxAmount] >= 0 AND [TaxAmount] <= [Amount]"));
        // One capitalization per account and period start: a repeated run cannot credit the same period twice.
        builder.HasIndex(entity => new { entity.AccountId, entity.FromDate })
            .IsUnique()
            .HasDatabaseName("UX_AccountInterestCapitalization_Account_From");
        builder.HasIndex(entity => new { entity.AccountId, entity.ToDate })
            .HasDatabaseName("IX_AccountInterestCapitalization_Account_To");
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(entity => entity.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PostedTransaction>()
            .WithMany()
            .HasForeignKey(entity => entity.PostedTransactionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
