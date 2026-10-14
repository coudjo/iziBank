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
        builder.HasAlternateKey(entity => new { entity.ModuleId, entity.SubModuleId })
            .HasName("AK_SubModule_Module_SubModule");
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

internal sealed class RoleGroupConfiguration : IEntityTypeConfiguration<RoleGroup>
{
    public void Configure(EntityTypeBuilder<RoleGroup> builder)
    {
        builder.ToTable(nameof(RoleGroup));
        builder.HasKey(entity => entity.RoleGroupId);
        builder.Property(entity => entity.RoleGroupCode).IsVarchar(30);
        builder.HasIndex(entity => entity.RoleGroupCode).IsUnique();
        builder.Property(entity => entity.RoleGroupName).IsNvarchar(100);
        builder.Property(entity => entity.Description).IsOptionalNvarchar(300);
        builder.Property(entity => entity.IsSystemGroup).HasDefaultValue(false);
        builder.Property(entity => entity.IsActive).HasDefaultValue(true);
        builder.Property(entity => entity.IsSupervisionRequired).HasDefaultValue(true);

        builder.HasOne<StaffUser>()
            .WithMany()
            .HasForeignKey(entity => entity.SupervisedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class RoleGroupPermissionConfiguration : IEntityTypeConfiguration<RoleGroupPermission>
{
    public void Configure(EntityTypeBuilder<RoleGroupPermission> builder)
    {
        builder.ToTable(nameof(RoleGroupPermission), table =>
            table.HasCheckConstraint(
                "CK_RoleGroupPermission_ViewImplied",
                "[AllowView] = 1 OR ([AllowAdd] = 0 AND [AllowEdit] = 0 AND [AllowDelete] = 0 AND [AllowSupervise] = 0)"));

        builder.HasKey(entity => entity.RoleGroupPermissionId);
        builder.Property(entity => entity.AllowView).HasDefaultValue(false);
        builder.Property(entity => entity.AllowAdd).HasDefaultValue(false);
        builder.Property(entity => entity.AllowEdit).HasDefaultValue(false);
        builder.Property(entity => entity.AllowDelete).HasDefaultValue(false);
        builder.Property(entity => entity.AllowSupervise).HasDefaultValue(false);

        builder.HasIndex(entity => new { entity.RoleGroupId, entity.ModuleId, entity.SubModuleId })
            .IsUnique()
            .HasDatabaseName("UX_RoleGroupPermission_Group_Module_SubModule");

        builder.HasOne(entity => entity.RoleGroup)
            .WithMany(group => group.Permissions)
            .HasForeignKey(entity => entity.RoleGroupId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.Module)
            .WithMany()
            .HasForeignKey(entity => entity.ModuleId)
            .OnDelete(DeleteBehavior.NoAction);

        // A sub-module must belong to the stated module.
        builder.HasOne(entity => entity.SubModule)
            .WithMany()
            .HasForeignKey(entity => new { entity.ModuleId, entity.SubModuleId })
            .HasPrincipalKey(subModule => new { subModule.ModuleId, subModule.SubModuleId })
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class StaffUserRoleGroupConfiguration : IEntityTypeConfiguration<StaffUserRoleGroup>
{
    public void Configure(EntityTypeBuilder<StaffUserRoleGroup> builder)
    {
        builder.ToTable(nameof(StaffUserRoleGroup));
        builder.HasKey(entity => entity.StaffUserRoleGroupId);
        builder.Property(entity => entity.IsSupervisionRequired).HasDefaultValue(true);

        builder.HasIndex(entity => new { entity.StaffUserId, entity.RoleGroupId, entity.BranchId })
            .IsUnique()
            .HasDatabaseName("UX_StaffUserRoleGroup_Staff_Group_Branch");
        builder.HasIndex(entity => new { entity.BranchId, entity.RoleGroupId })
            .HasDatabaseName("IX_StaffUserRoleGroup_Branch_Group");

        builder.HasOne(entity => entity.StaffUser)
            .WithMany(user => user.RoleGroups)
            .HasForeignKey(entity => entity.StaffUserId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.RoleGroup)
            .WithMany(group => group.Members)
            .HasForeignKey(entity => entity.RoleGroupId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.Branch)
            .WithMany()
            .HasForeignKey(entity => entity.BranchId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<StaffUser>()
            .WithMany()
            .HasForeignKey(entity => entity.SupervisedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class IdempotencyKeyConfiguration : IEntityTypeConfiguration<IdempotencyKey>
{
    public void Configure(EntityTypeBuilder<IdempotencyKey> builder)
    {
        builder.ToTable(nameof(IdempotencyKey));
        builder.HasKey(entity => entity.IdempotencyKeyId);
        builder.Property(entity => entity.KeyValue).IsVarchar(100);
        builder.Property(entity => entity.Route).IsVarchar(200);
        builder.Property(entity => entity.RequestHash).IsVarchar(64);
        builder.Property(entity => entity.ResponseBody).IsNvarcharMax();
        builder.Property(entity => entity.CreatedOn).HasDefaultValueSql(BankingModelConventions.CreatedOnDefaultSql);

        builder.HasIndex(entity => new { entity.StaffUserId, entity.KeyValue })
            .IsUnique()
            .HasDatabaseName("UX_IdempotencyKey_Staff_Key");
        builder.HasIndex(entity => entity.ExpiresOn)
            .HasDatabaseName("IX_IdempotencyKey_ExpiresOn");

        builder.HasOne(entity => entity.StaffUser)
            .WithMany()
            .HasForeignKey(entity => entity.StaffUserId)
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
