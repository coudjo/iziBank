using Banking.Domain.Supervision;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Banking.Infrastructure.Persistence.Configurations;

internal sealed class ChangeRequestConfiguration : IEntityTypeConfiguration<ChangeRequest>
{
    public void Configure(EntityTypeBuilder<ChangeRequest> builder)
    {
        builder.ToTable(nameof(ChangeRequest));
        builder.HasKey(entity => entity.ChangeRequestId);
        builder.Property(entity => entity.EntityType).IsVarchar(50);
        builder.Property(entity => entity.EntityId).IsVarchar(50);
        builder.Property(entity => entity.ChangeType).IsVarchar(30);
        builder.Property(entity => entity.Status).IsVarchar(10);
        builder.Property(entity => entity.BeforeJson).IsNvarcharMax();
        builder.Property(entity => entity.PayloadJson).IsUnicode(true).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(entity => entity.DecisionNote).IsOptionalNvarchar(300);

        builder.HasIndex(entity => new { entity.Status, entity.RequestedOn })
            .HasDatabaseName("IX_ChangeRequest_Status_RequestedOn");
        builder.HasIndex(entity => new { entity.EntityType, entity.EntityId })
            .IsUnique()
            .HasFilter("[Status] = 'PENDING'")
            .HasDatabaseName("UX_ChangeRequest_OnePendingPerEntity");

        builder.HasOne(entity => entity.RequestedByUser)
            .WithMany()
            .HasForeignKey(entity => entity.RequestedByUserId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.DecidedByUser)
            .WithMany()
            .HasForeignKey(entity => entity.DecidedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
