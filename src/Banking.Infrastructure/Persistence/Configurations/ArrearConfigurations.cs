using Banking.Domain.Charges;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Banking.Infrastructure.Persistence.Configurations;

internal sealed class AccountChargeArrearConfiguration : IEntityTypeConfiguration<AccountChargeArrear>
{
    public void Configure(EntityTypeBuilder<AccountChargeArrear> builder)
    {
        builder.ToTable(nameof(AccountChargeArrear));
        builder.HasKey(entity => entity.AccountChargeArrearId);
        builder.Property(entity => entity.AccountChargeArrearId).UseIdentityColumn();
        builder.Property(entity => entity.Amount).HasPrecision(19, 4);
        builder.Property(entity => entity.OutstandingAmount).HasPrecision(19, 4);
        builder.Property(entity => entity.RowVersion).IsRowVersion();
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_AccountChargeArrear_Amounts",
            "[Amount] > 0 AND [OutstandingAmount] >= 0 AND [OutstandingAmount] <= [Amount]"));
        builder.HasIndex(entity => new { entity.AccountId, entity.RaisedOn, entity.AccountChargeArrearId })
            .HasDatabaseName("IX_AccountChargeArrear_Account_Raised");
        builder.HasOne<Banking.Domain.Accounts.Account>().WithMany().HasForeignKey(entity => entity.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Banking.Domain.Charges.ProductCharge>().WithMany().HasForeignKey(entity => entity.ProductChargeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Banking.Domain.Transactions.PostedTransaction>().WithMany().HasForeignKey(entity => entity.RaisedTransactionId).OnDelete(DeleteBehavior.Restrict);
    }
}
