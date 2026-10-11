using Banking.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Banking.Infrastructure.Persistence.Configurations;

internal sealed class ModuleConfiguration : IEntityTypeConfiguration<Module>
{
    public void Configure(EntityTypeBuilder<Module> builder)
    {
        builder.ToTable(nameof(Module));
        builder.HasKey(entity => entity.ModuleId);
        builder.Property(entity => entity.ModuleCode).IsVarchar(20);
        builder.HasIndex(entity => entity.ModuleCode).IsUnique();
        builder.Property(entity => entity.ModuleName).IsVarchar(50);
        builder.Property(entity => entity.HasDataEntry).HasDefaultValue(false);
        builder.Property(entity => entity.IsActive).HasDefaultValue(true);
    }
}

internal sealed class SubModuleConfiguration : IEntityTypeConfiguration<SubModule>
{
    public void Configure(EntityTypeBuilder<SubModule> builder)
    {
        builder.ToTable(nameof(SubModule));
        builder.HasKey(entity => entity.SubModuleId);
        builder.Property(entity => entity.SubModuleCode).IsVarchar(20);
        builder.HasIndex(entity => entity.SubModuleCode).IsUnique();
        builder.Property(entity => entity.SubModuleName).IsVarchar(50);
        builder.Property(entity => entity.IsActive).HasDefaultValue(true);

        builder.HasOne(entity => entity.Module)
            .WithMany(module => module.SubModules)
            .HasForeignKey(entity => entity.ModuleId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class StaffUserConfiguration : IEntityTypeConfiguration<StaffUser>
{
    public void Configure(EntityTypeBuilder<StaffUser> builder)
    {
        builder.ToTable(nameof(StaffUser));
        builder.HasKey(entity => entity.StaffUserId);
        builder.Property(entity => entity.StaffCode).IsVarchar(20);
        builder.HasIndex(entity => entity.StaffCode).IsUnique();
        builder.Property(entity => entity.FullName).IsNvarchar(100);
        builder.Property(entity => entity.IsTeller).HasDefaultValue(false);
        builder.Property(entity => entity.IsDisabled).HasDefaultValue(false);
        builder.Property(entity => entity.SecretKeyHash).IsOptionalVarchar(255);
        builder.Property(entity => entity.RowVersion).IsRowVersion();

        builder.HasOne(entity => entity.Branch)
            .WithMany(branch => branch.StaffUsers)
            .HasForeignKey(entity => entity.BranchId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.TellerGLAccount)
            .WithMany()
            .HasForeignKey(entity => entity.TellerGLAccountId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class CustomerDigitalAccessConfiguration : IEntityTypeConfiguration<CustomerDigitalAccess>
{
    public void Configure(EntityTypeBuilder<CustomerDigitalAccess> builder)
    {
        builder.ToTable(nameof(CustomerDigitalAccess));
        builder.HasKey(entity => entity.CustomerDigitalAccessId);
        builder.Property(entity => entity.AccessCode).IsVarchar(20);
        builder.HasIndex(entity => entity.AccessCode).IsUnique();
        builder.Property(entity => entity.SecretKeyHash).IsOptionalVarchar(255);
        builder.Property(entity => entity.IsDisabled).HasDefaultValue(false);

        builder.HasOne(entity => entity.Party)
            .WithMany()
            .HasForeignKey(entity => entity.PartyId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class UserPermissionConfiguration : IEntityTypeConfiguration<UserPermission>
{
    public void Configure(EntityTypeBuilder<UserPermission> builder)
    {
        builder.ToTable(nameof(UserPermission));
        builder.HasKey(entity => entity.UserPermissionId);
        builder.HasIndex(entity => new { entity.StaffUserId, entity.BranchId, entity.ModuleId, entity.SubModuleId })
            .IsUnique()
            .HasDatabaseName("UX_UserPermission_Staff_Branch_Module_SubModule");

        builder.HasOne(entity => entity.StaffUser)
            .WithMany(user => user.Permissions)
            .HasForeignKey(entity => entity.StaffUserId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.Branch)
            .WithMany()
            .HasForeignKey(entity => entity.BranchId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.Module)
            .WithMany()
            .HasForeignKey(entity => entity.ModuleId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.SubModule)
            .WithMany()
            .HasForeignKey(entity => entity.SubModuleId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable(nameof(UserSession), table =>
            table.HasCheckConstraint(
                "CK_UserSession_ExactlyOnePrincipal",
                "([StaffUserId] IS NOT NULL AND [CustomerDigitalAccessId] IS NULL) OR ([StaffUserId] IS NULL AND [CustomerDigitalAccessId] IS NOT NULL)"));

        builder.HasKey(entity => entity.SessionId);
        builder.Property(entity => entity.IPAddress).IsOptionalVarchar(45);
        builder.Property(entity => entity.DeviceInfo).IsOptionalVarchar(200);
        builder.HasIndex(entity => entity.StaffUserId)
            .HasFilter("[LogoutAt] IS NULL")
            .HasDatabaseName("IX_UserSession_StaffUserId_Open");

        builder.HasOne(entity => entity.StaffUser)
            .WithMany()
            .HasForeignKey(entity => entity.StaffUserId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.CustomerDigitalAccess)
            .WithMany()
            .HasForeignKey(entity => entity.CustomerDigitalAccessId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.LogoutReason)
            .WithMany()
            .HasForeignKey(entity => entity.LogoutReasonId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class EventTypeConfiguration : IEntityTypeConfiguration<EventType>
{
    public void Configure(EntityTypeBuilder<EventType> builder)
    {
        builder.ToTable(nameof(EventType));
        builder.HasKey(entity => entity.EventTypeId);
        builder.Property(entity => entity.EventCode).IsVarchar(30);
        builder.HasIndex(entity => entity.EventCode).IsUnique();
        builder.Property(entity => entity.EventName).IsVarchar(100);
    }
}

internal sealed class UserActivityLogConfiguration : IEntityTypeConfiguration<UserActivityLog>
{
    public void Configure(EntityTypeBuilder<UserActivityLog> builder)
    {
        builder.ToTable(nameof(UserActivityLog), table => table.ExcludeFromMigrations());
        builder.HasKey(entity => entity.EventId);
        builder.Property(entity => entity.EntityType).IsOptionalVarchar(50);
        builder.Property(entity => entity.EntityId).IsOptionalVarchar(50);
        builder.Property(entity => entity.Outcome).IsVarchar(10);
        builder.Property(entity => entity.RequestDetailsJson).IsNvarcharMax();

        builder.HasIndex(entity => new { entity.SessionId, entity.EventTimestamp })
            .HasDatabaseName("IX_UserActivityLog_SessionId_EventTimestamp");
        builder.HasIndex(entity => new { entity.EntityType, entity.EntityId, entity.EventTimestamp })
            .HasDatabaseName("IX_UserActivityLog_EntityType_EntityId_EventTimestamp");
        builder.HasIndex(entity => new { entity.EventTypeId, entity.EntityId, entity.EventTimestamp })
            .HasDatabaseName("IX_UserActivityLog_EventTypeId_EntityId_EventTimestamp");

        builder.HasOne(entity => entity.Session)
            .WithMany()
            .HasForeignKey(entity => entity.SessionId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.Branch)
            .WithMany()
            .HasForeignKey(entity => entity.BranchId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.EventType)
            .WithMany()
            .HasForeignKey(entity => entity.EventTypeId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class DataChangeAuditLogConfiguration : IEntityTypeConfiguration<DataChangeAuditLog>
{
    public void Configure(EntityTypeBuilder<DataChangeAuditLog> builder)
    {
        builder.ToTable(nameof(DataChangeAuditLog), table => table.ExcludeFromMigrations());
        builder.HasKey(entity => entity.AuditId);
        builder.Property(entity => entity.TableName).IsVarchar(100);
        builder.Property(entity => entity.RecordId).IsOptionalVarchar(50);
        builder.Property(entity => entity.ActionType).IsVarchar(10);
        builder.Property(entity => entity.OldValueJson).IsNvarcharMax();
        builder.Property(entity => entity.NewValueJson).IsNvarcharMax();

        builder.HasIndex(entity => new { entity.TableName, entity.RecordId, entity.LoggedOn })
            .HasDatabaseName("IX_DataChangeAuditLog_TableName_RecordId_LoggedOn");
        builder.HasIndex(entity => new { entity.StaffUserId, entity.LoggedOn })
            .HasDatabaseName("IX_DataChangeAuditLog_StaffUserId_LoggedOn");

        builder.HasOne(entity => entity.StaffUser)
            .WithMany()
            .HasForeignKey(entity => entity.StaffUserId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.Branch)
            .WithMany()
            .HasForeignKey(entity => entity.BranchId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
