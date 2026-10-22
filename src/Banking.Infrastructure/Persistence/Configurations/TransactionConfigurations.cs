using Banking.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Banking.Infrastructure.Persistence.Configurations;

internal sealed class TransactionCodeConfiguration : IEntityTypeConfiguration<TransactionCode>
{
    public void Configure(EntityTypeBuilder<TransactionCode> builder)
    {
        builder.ToTable(nameof(TransactionCode));
        builder.HasKey(entity => entity.TransactionCodeId);
        builder.Property(entity => entity.Code).IsVarchar(TransactionCodes.MaxCodeLength);
        builder.HasIndex(entity => entity.Code).IsUnique();
        builder.Property(entity => entity.Description).IsVarchar(100);
        builder.Property(entity => entity.Category).IsVarchar(20);
        builder.Property(entity => entity.DefaultNarration).IsOptionalNvarchar(100);
        builder.Property(entity => entity.IsSystemCode).HasDefaultValue(false);
        builder.Property(entity => entity.IsActive).HasDefaultValue(true);
    }
}

internal sealed class PostingRequestConfiguration : IEntityTypeConfiguration<PostingRequest>
{
    public void Configure(EntityTypeBuilder<PostingRequest> builder)
    {
        builder.ToTable(nameof(PostingRequest), table =>
        {
            table.HasCheckConstraint("CK_PostingRequest_Amount", "[Amount] > 0");
            table.HasCheckConstraint(
                "CK_PostingRequest_Status",
                "[Status] IN ('PENDING', 'POSTED', 'REJECTED', 'CANCELLED', 'EXPIRED')");
        });
        builder.HasKey(entity => entity.PostingRequestId);
        builder.Property(entity => entity.Reference).IsVarchar(30);
        builder.HasIndex(entity => entity.Reference).IsUnique();
        builder.Property(entity => entity.PayloadJson).IsUnicode(true).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(entity => entity.Status).IsVarchar(20);
        builder.Property(entity => entity.DecisionReason).IsOptionalNvarchar(300);
        builder.Property(entity => entity.RowVersion).IsRowVersion();

        // Approvals inbox and the end-of-day "nothing pending" check.
        builder.HasIndex(entity => new { entity.BranchId, entity.Status, entity.WorkingDate })
            .HasDatabaseName("IX_PostingRequest_Branch_Status_WorkingDate");
        builder.HasIndex(entity => entity.PostedTransactionId)
            .IsUnique()
            .HasFilter("[PostedTransactionId] IS NOT NULL")
            .HasDatabaseName("UX_PostingRequest_PostedTransactionId");

        builder.HasOne(entity => entity.Branch)
            .WithMany()
            .HasForeignKey(entity => entity.BranchId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.TransactionCode)
            .WithMany()
            .HasForeignKey(entity => entity.TransactionCodeId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.RequestedByUser)
            .WithMany()
            .HasForeignKey(entity => entity.RequestedByUserId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.SupervisedByUser)
            .WithMany()
            .HasForeignKey(entity => entity.SupervisedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

/// <summary>
/// The three posting ledgers are SQL Server append-only ledger tables, created in raw SQL
/// (<c>PostingLedgerSql</c>), so they are excluded from generated migrations, like <c>UserActivityLog</c>.
/// Ledger tables reject MERGE, which EF uses for multi-row inserts with OUTPUT: plain INSERTs only.
/// </summary>
internal sealed class PostedTransactionConfiguration : IEntityTypeConfiguration<PostedTransaction>
{
    public void Configure(EntityTypeBuilder<PostedTransaction> builder)
    {
        builder.ToTable(nameof(PostedTransaction), table =>
        {
            table.ExcludeFromMigrations();
            table.UseSqlOutputClause(false);
        });
        builder.HasKey(entity => entity.PostedTransactionId);
        builder.Property(entity => entity.Reference).IsVarchar(30);
        builder.HasIndex(entity => entity.Reference).IsUnique();
        builder.Property(entity => entity.Narration).IsOptionalNvarchar(200);
        builder.HasIndex(entity => new { entity.BranchId, entity.EntryDate })
            .HasDatabaseName("IX_PostedTransaction_BranchId_EntryDate");

        builder.HasOne(entity => entity.Branch)
            .WithMany()
            .HasForeignKey(entity => entity.BranchId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.TransactionCode)
            .WithMany()
            .HasForeignKey(entity => entity.TransactionCodeId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.PostingRequest)
            .WithMany()
            .HasForeignKey(entity => entity.PostingRequestId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasMany(entity => entity.AccountEntries)
            .WithOne(entry => entry.Transaction)
            .HasForeignKey(entry => entry.PostedTransactionId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasMany(entity => entity.LedgerEntries)
            .WithOne(entry => entry.Transaction)
            .HasForeignKey(entry => entry.PostedTransactionId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class AccountLedgerEntryConfiguration : IEntityTypeConfiguration<AccountLedgerEntry>
{
    public void Configure(EntityTypeBuilder<AccountLedgerEntry> builder)
    {
        builder.ToTable(nameof(AccountLedgerEntry), table =>
        {
            table.ExcludeFromMigrations();
            table.UseSqlOutputClause(false);
        });
        builder.HasKey(entity => entity.AccountLedgerEntryId);
        builder.Property(entity => entity.CrDb).IsUnicode(false).HasMaxLength(1).IsFixedLength().IsRequired();
        builder.HasIndex(entity => new { entity.AccountId, entity.AccountLedgerEntryId })
            .HasDatabaseName("IX_AccountLedgerEntry_AccountId_EntryId");
        builder.HasIndex(entity => new { entity.BranchId, entity.EntryDate })
            .HasDatabaseName("IX_AccountLedgerEntry_BranchId_EntryDate");

        builder.HasOne(entity => entity.Account)
            .WithMany()
            .HasForeignKey(entity => entity.AccountId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable(nameof(LedgerEntry), table =>
        {
            table.ExcludeFromMigrations();
            table.UseSqlOutputClause(false);
        });
        builder.HasKey(entity => entity.LedgerEntryId);
        builder.Property(entity => entity.CrDb).IsUnicode(false).HasMaxLength(1).IsFixedLength().IsRequired();
        builder.HasIndex(entity => new { entity.LedgerAccountId, entity.BranchId, entity.EntryDate })
            .HasDatabaseName("IX_LedgerEntry_Account_Branch_EntryDate");
        builder.HasIndex(entity => new { entity.BranchId, entity.EntryDate })
            .HasDatabaseName("IX_LedgerEntry_BranchId_EntryDate");

        builder.HasOne(entity => entity.LedgerAccount)
            .WithMany()
            .HasForeignKey(entity => entity.LedgerAccountId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class LedgerAccountDailyBalanceConfiguration : IEntityTypeConfiguration<LedgerAccountDailyBalance>
{
    public void Configure(EntityTypeBuilder<LedgerAccountDailyBalance> builder)
    {
        builder.ToTable(nameof(LedgerAccountDailyBalance));
        builder.HasKey(entity => entity.LedgerAccountDailyBalanceId);
        builder.HasIndex(entity => new { entity.LedgerAccountId, entity.BranchId, entity.BalanceDate })
            .IsUnique()
            .HasDatabaseName("UX_LedgerAccountDailyBalance_Account_Branch_Date");

        builder.HasOne(entity => entity.LedgerAccount)
            .WithMany()
            .HasForeignKey(entity => entity.LedgerAccountId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.Branch)
            .WithMany()
            .HasForeignKey(entity => entity.BranchId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class TillSessionConfiguration : IEntityTypeConfiguration<TillSession>
{
    public void Configure(EntityTypeBuilder<TillSession> builder)
    {
        builder.ToTable(nameof(TillSession), table =>
            table.HasCheckConstraint("CK_TillSession_Status", "[Status] IN ('OPEN', 'CLOSING', 'CLOSED')"));
        builder.HasKey(entity => entity.TillSessionId);
        builder.Property(entity => entity.Status).IsVarchar(10);
        builder.Property(entity => entity.RowVersion).IsRowVersion();

        // Plan B 2.3: one open or closing session per teller.
        builder.HasIndex(entity => entity.StaffUserId)
            .IsUnique()
            .HasFilter("[Status] <> 'CLOSED'")
            .HasDatabaseName("UX_TillSession_OneActivePerStaff");
        builder.HasIndex(entity => new { entity.BranchId, entity.WorkingDate })
            .HasDatabaseName("IX_TillSession_Branch_WorkingDate");

        builder.HasOne(entity => entity.StaffUser)
            .WithMany()
            .HasForeignKey(entity => entity.StaffUserId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.Branch)
            .WithMany()
            .HasForeignKey(entity => entity.BranchId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.TellerLedgerAccount)
            .WithMany()
            .HasForeignKey(entity => entity.TellerLedgerAccountId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
