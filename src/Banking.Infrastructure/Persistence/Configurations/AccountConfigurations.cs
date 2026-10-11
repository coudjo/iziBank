using Banking.Domain.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Banking.Infrastructure.Persistence.Configurations;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable(nameof(Account), table =>
            table.HasCheckConstraint(
                "CK_Account_CloseFields",
                "([ClosedOn] IS NULL AND [ClosedByUserId] IS NULL AND [CloseReasonId] IS NULL) OR ([ClosedOn] IS NOT NULL AND [ClosedByUserId] IS NOT NULL AND [CloseReasonId] IS NOT NULL)"));

        builder.HasKey(entity => entity.AccountId);
        builder.Property(entity => entity.AccountNumber).IsVarchar(20);
        builder.HasIndex(entity => entity.AccountNumber).IsUnique();
        builder.Property(entity => entity.AccountName).IsNvarchar(150);
        builder.Property(entity => entity.IsDormant).HasDefaultValue(false);
        builder.Property(entity => entity.IsSupervisionRequired).HasDefaultValue(true);
        builder.Property(entity => entity.RowVersion).IsRowVersion();
        builder.HasIndex(entity => new { entity.BranchId, entity.ProductId })
            .HasDatabaseName("IX_Account_BranchId_ProductId");
        builder.HasIndex(entity => entity.ProductId);

        builder.HasOne(entity => entity.Branch)
            .WithMany()
            .HasForeignKey(entity => entity.BranchId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.Product)
            .WithMany()
            .HasForeignKey(entity => entity.ProductId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.OpenedByUser)
            .WithMany()
            .HasForeignKey(entity => entity.OpenedByUserId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.OperatingMode)
            .WithMany()
            .HasForeignKey(entity => entity.OperatingModeId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.ClosedByUser)
            .WithMany()
            .HasForeignKey(entity => entity.ClosedByUserId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.CloseReason)
            .WithMany()
            .HasForeignKey(entity => entity.CloseReasonId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.SupervisedByUser)
            .WithMany()
            .HasForeignKey(entity => entity.SupervisedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class AccountHolderConfiguration : IEntityTypeConfiguration<AccountHolder>
{
    public void Configure(EntityTypeBuilder<AccountHolder> builder)
    {
        builder.ToTable(nameof(AccountHolder));
        builder.HasKey(entity => entity.AccountHolderId);
        builder.HasIndex(entity => new { entity.AccountId, entity.PartyId })
            .IsUnique()
            .HasDatabaseName("UX_AccountHolder_AccountId_PartyId");
        builder.HasIndex(entity => entity.PartyId);
        builder.HasIndex(entity => new { entity.AccountId, entity.AccountHolderRoleId })
            .HasDatabaseName("IX_AccountHolder_AccountId_Role");

        builder.HasOne(entity => entity.Account)
            .WithMany(account => account.Holders)
            .HasForeignKey(entity => entity.AccountId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.Party)
            .WithMany()
            .HasForeignKey(entity => entity.PartyId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.Role)
            .WithMany()
            .HasForeignKey(entity => entity.AccountHolderRoleId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.OperatingInstruction)
            .WithMany()
            .HasForeignKey(entity => entity.OperatingInstructionId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class AccountBalanceConfiguration : IEntityTypeConfiguration<AccountBalance>
{
    public void Configure(EntityTypeBuilder<AccountBalance> builder)
    {
        builder.ToTable(nameof(AccountBalance));
        builder.HasKey(entity => entity.AccountId);
        builder.Property(entity => entity.OpeningBalance).HasDefaultValue(0m);
        builder.Property(entity => entity.ClearBalance).HasDefaultValue(0m);
        builder.Property(entity => entity.FreezedAmount).HasDefaultValue(0m);
        builder.Property(entity => entity.UnsupervisedCredit).HasDefaultValue(0m);
        builder.Property(entity => entity.UnsupervisedDebit).HasDefaultValue(0m);
        builder.Property(entity => entity.OverdrawLimit).HasDefaultValue(0m);
        builder.Property(entity => entity.AvailableBalance).HasDefaultValue(0m);
        builder.Property(entity => entity.TotalBalance).HasDefaultValue(0m);
        builder.Property(entity => entity.RowVersion).IsRowVersion();

        builder.HasOne(entity => entity.Account)
            .WithOne(account => account.Balance)
            .HasForeignKey<AccountBalance>(entity => entity.AccountId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class AccountBalanceDailySnapshotConfiguration : IEntityTypeConfiguration<AccountBalanceDailySnapshot>
{
    public void Configure(EntityTypeBuilder<AccountBalanceDailySnapshot> builder)
    {
        builder.ToTable(nameof(AccountBalanceDailySnapshot));
        builder.HasKey(entity => entity.SnapshotId);
        builder.HasIndex(entity => new { entity.AccountId, entity.SnapshotDate })
            .IsUnique()
            .HasDatabaseName("UX_AccountBalanceDailySnapshot_AccountId_SnapshotDate");

        builder.HasOne(entity => entity.Account)
            .WithMany()
            .HasForeignKey(entity => entity.AccountId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class AccountClassificationConfiguration : IEntityTypeConfiguration<AccountClassification>
{
    public void Configure(EntityTypeBuilder<AccountClassification> builder)
    {
        builder.ToTable(nameof(AccountClassification));
        builder.HasKey(entity => entity.AccountClassificationId);
        builder.Property(entity => entity.ClassificationCode).IsVarchar(10);
        builder.HasIndex(entity => entity.ClassificationCode).IsUnique();
        builder.Property(entity => entity.ClassificationName).IsVarchar(100);
    }
}

internal sealed class AccountSubClassificationConfiguration : IEntityTypeConfiguration<AccountSubClassification>
{
    public void Configure(EntityTypeBuilder<AccountSubClassification> builder)
    {
        builder.ToTable(nameof(AccountSubClassification));
        builder.HasKey(entity => entity.AccountSubClassificationId);
        builder.Property(entity => entity.SubClassificationCode).IsVarchar(10);
        builder.HasIndex(entity => entity.SubClassificationCode).IsUnique();
        builder.Property(entity => entity.SubClassificationName).IsVarchar(100);

        builder.HasOne(entity => entity.Classification)
            .WithMany(classification => classification.SubClassifications)
            .HasForeignKey(entity => entity.AccountClassificationId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class AccountClassificationAssignmentConfiguration : IEntityTypeConfiguration<AccountClassificationAssignment>
{
    public void Configure(EntityTypeBuilder<AccountClassificationAssignment> builder)
    {
        builder.ToTable(nameof(AccountClassificationAssignment));
        builder.HasKey(entity => entity.AssignmentId);
        builder.HasIndex(entity => new { entity.AccountId, entity.AccountClassificationId })
            .IsUnique()
            .HasDatabaseName("UX_AccountClassificationAssignment_Account_Classification");

        builder.HasOne(entity => entity.Account)
            .WithMany(account => account.Classifications)
            .HasForeignKey(entity => entity.AccountId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.Classification)
            .WithMany()
            .HasForeignKey(entity => entity.AccountClassificationId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.SubClassification)
            .WithMany()
            .HasForeignKey(entity => entity.AccountSubClassificationId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class AccountConditionConfiguration : IEntityTypeConfiguration<AccountCondition>
{
    public void Configure(EntityTypeBuilder<AccountCondition> builder)
    {
        builder.ToTable(nameof(AccountCondition));
        builder.HasKey(entity => entity.AccountId);
        builder.Property(entity => entity.IsFreezed).HasDefaultValue(false);
        builder.Property(entity => entity.DoNotAllowCredit).HasDefaultValue(false);
        builder.Property(entity => entity.DoNotAllowDebit).HasDefaultValue(false);
        builder.Property(entity => entity.SuperviseAllTransactions).HasDefaultValue(false);
        builder.Property(entity => entity.DoNotApplyTransactionCharges).HasDefaultValue(false);

        builder.HasOne(entity => entity.Account)
            .WithOne(account => account.Condition)
            .HasForeignKey<AccountCondition>(entity => entity.AccountId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class AccountReminderConfiguration : IEntityTypeConfiguration<AccountReminder>
{
    public void Configure(EntityTypeBuilder<AccountReminder> builder)
    {
        builder.ToTable(nameof(AccountReminder));
        builder.HasKey(entity => entity.AccountReminderId);
        builder.Property(entity => entity.ReminderText).IsVarchar(200);

        builder.HasOne(entity => entity.Account)
            .WithMany(account => account.Reminders)
            .HasForeignKey(entity => entity.AccountId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class DormantAccountHistoryConfiguration : IEntityTypeConfiguration<DormantAccountHistory>
{
    public void Configure(EntityTypeBuilder<DormantAccountHistory> builder)
    {
        builder.ToTable(nameof(DormantAccountHistory));
        builder.HasKey(entity => entity.DormantHistoryId);

        builder.HasOne(entity => entity.Account)
            .WithMany()
            .HasForeignKey(entity => entity.AccountId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.ActivatedByUser)
            .WithMany()
            .HasForeignKey(entity => entity.ActivatedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class FreezeReleaseAccountBalanceConfiguration : IEntityTypeConfiguration<FreezeReleaseAccountBalance>
{
    public void Configure(EntityTypeBuilder<FreezeReleaseAccountBalance> builder)
    {
        builder.ToTable(nameof(FreezeReleaseAccountBalance), table =>
            table.HasCheckConstraint(
                "CK_FreezeReleaseAccountBalance_Amount",
                "[Amount] IS NULL OR [Amount] > 0"));

        builder.HasKey(entity => entity.FreezeReleaseId);
        builder.Property(entity => entity.Remarks).IsOptionalVarchar(200);
        builder.HasIndex(entity => new { entity.AccountId, entity.FreezeReleaseId })
            .HasDatabaseName("IX_FreezeReleaseAccountBalance_AccountId_FreezeReleaseId");

        builder.HasOne(entity => entity.Account)
            .WithMany(account => account.FreezeReleases)
            .HasForeignKey(entity => entity.AccountId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.Action)
            .WithMany()
            .HasForeignKey(entity => entity.FreezeReleaseActionId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
