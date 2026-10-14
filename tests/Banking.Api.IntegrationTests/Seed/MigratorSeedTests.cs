using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Domain.Institution;
using Banking.Domain.Lookups;
using Banking.Domain.Security;
using Banking.Infrastructure.Persistence;
using Banking.Migrator;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Banking.Api.IntegrationTests.Seed;

[Collection(SqlServerCollection.Name)]
public sealed class MigratorSeedTests
{
    private readonly MsSqlContainerFixture _fixture;

    public MigratorSeedTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Seed_is_idempotent_and_creates_admin_from_environment_values()
    {
        var connectionString = await _fixture.CreateDatabaseAsync($"iziBank_seed_{Guid.NewGuid():N}");
        var settings = BootstrapSettings.Create(
            connectionString,
            "ADMIN",
            "ReplaceWithAStrongSecret1",
            "001",
            "Head Office");

        await using (var context = CreateContext(connectionString))
        {
            await DatabaseInitializer.ApplyAsync(context, settings);
        }

        await using (var context = CreateContext(connectionString))
        {
            await AssertSeededAsync(context, settings);
        }

        var first = await CaptureSnapshotAsync(connectionString);

        await using (var context = CreateContext(connectionString))
        {
            await DatabaseInitializer.ApplyAsync(context, settings);
        }

        var second = await CaptureSnapshotAsync(connectionString);
        second.ShouldBe(first);
    }

    [Fact]
    public async Task Migrator_host_creates_admin_from_configuration()
    {
        var connectionString = await _fixture.CreateDatabaseAsync($"iziBank_host_{Guid.NewGuid():N}");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Banking"] = connectionString,
                ["BOOTSTRAP_ADMIN_CODE"] = "HQADMIN",
                ["BOOTSTRAP_ADMIN_SECRET"] = "ReplaceWithAStrongSecret1",
                ["BOOTSTRAP_BRANCH_CODE"] = "HQ1",
                ["BOOTSTRAP_BRANCH_NAME"] = "Headquarters"
            })
            .Build();

        var exitCode = await MigratorHost.RunAsync([], configuration);
        exitCode.ShouldBe(0);

        await using var context = CreateContext(connectionString);
        var admin = await context.Set<StaffUser>().SingleAsync(user => user.StaffCode == "HQADMIN");
        admin.ChangeKeyAtNextLogOn.ShouldBe(true);
        admin.CanChangeSecretKey.ShouldBe(true);
        admin.IsDisabled.ShouldBeFalse();

        var hasher = new PasswordHasher<StaffUser>();
        hasher.VerifyHashedPassword(admin, admin.SecretKeyHash!, "ReplaceWithAStrongSecret1")
            .ShouldBe(PasswordVerificationResult.Success);
    }

    private static async Task AssertSeededAsync(BankingDbContext context, BootstrapSettings settings)
    {
        (await context.Set<RegulatoryCategoryType>().CountAsync()).ShouldBe(LookupCodes.RegulatoryCategories.All.Count);
        (await context.Set<PartyType>().CountAsync()).ShouldBe(3);
        (await context.Set<IdentificationDocumentType>().CountAsync()).ShouldBe(8);
        (await context.Set<EventType>().CountAsync()).ShouldBe(EventCodes.All.Count);
        (await context.Set<Module>().CountAsync()).ShouldBe(5);
        (await context.Set<SubModule>().CountAsync()).ShouldBe(19);

        var currency = await context.Set<Currency>().SingleAsync(row => row.IsLocalCurrency);
        currency.CurrencyCode.ShouldBe(CurrencyCodes.GhanaCedi);

        (await context.Set<InstitutionProfile>().CountAsync()).ShouldBe(1);
        (await context.Set<InstitutionProfile>().SingleAsync()).InstitutionProfileId.ShouldBe(1);

        var branch = await context.Set<Branch>().SingleAsync(row => row.BranchCode == settings.BranchCode);
        branch.BranchName.ShouldBe(settings.BranchName);

        var systemUser = await context.Set<StaffUser>().SingleAsync(user => user.StaffCode == StaffCodes.System);
        systemUser.IsDisabled.ShouldBeTrue();
        systemUser.SecretKeyHash.ShouldBeNull();
        systemUser.CreatedByUserId.ShouldBeNull();

        var admin = await context.Set<StaffUser>().SingleAsync(user => user.StaffCode == settings.AdminCode);
        admin.ChangeKeyAtNextLogOn.ShouldBe(true);
        admin.CanChangeSecretKey.ShouldBe(true);
        admin.CanSwitchBranch.ShouldBe(true);
        admin.IsDisabled.ShouldBeFalse();
        admin.CreatedByUserId.ShouldBeNull();
        admin.SecretKeyHash.ShouldNotBeNullOrEmpty();

        var hasher = new PasswordHasher<StaffUser>();
        hasher.VerifyHashedPassword(admin, admin.SecretKeyHash, settings.AdminSecret)
            .ShouldBe(PasswordVerificationResult.Success);

        var groups = await context.Set<RoleGroup>().Include(group => group.Permissions).ToListAsync();
        groups.Select(group => group.RoleGroupCode).ShouldBe(
            RoleGroupCatalog.All.Select(definition => definition.Code), ignoreOrder: true);
        groups.ShouldAllBe(group => group.IsSystemGroup && group.IsActive && !group.IsSupervisionRequired);
        foreach (var definition in RoleGroupCatalog.All)
        {
            groups.Single(group => group.RoleGroupCode == definition.Code)
                .Permissions.Count.ShouldBe(definition.Permissions.Count);
        }

        var assignments = await context.Set<StaffUserRoleGroup>()
            .Where(row => row.StaffUserId == admin.StaffUserId)
            .Include(row => row.RoleGroup)
            .ToListAsync();
        assignments.Select(row => row.RoleGroup!.RoleGroupCode).ShouldBe(
            RoleGroupCatalog.BootstrapAdministratorGroups, ignoreOrder: true);
        assignments.ShouldAllBe(row =>
            row.BranchId == branch.BranchId && !row.IsSupervisionRequired && row.CreatedByUserId == null);
    }

    private static async Task<string> CaptureSnapshotAsync(string connectionString)
    {
        await using var context = CreateContext(connectionString);

        var lookups = string.Join('|', (await context.Set<RegulatoryCategoryType>().AsNoTracking().ToListAsync())
            .Concat<Lookup>(await context.Set<BusinessDayStatusType>().AsNoTracking().ToListAsync())
            .Concat(await context.Set<LogoutReasonType>().AsNoTracking().ToListAsync())
            .Concat(await context.Set<PartyType>().AsNoTracking().ToListAsync())
            .Concat(await context.Set<KYCStatusType>().AsNoTracking().ToListAsync())
            .Concat(await context.Set<GenderType>().AsNoTracking().ToListAsync())
            .Concat(await context.Set<IdentificationDocumentType>().AsNoTracking().ToListAsync())
            .Concat(await context.Set<BusinessCategoryType>().AsNoTracking().ToListAsync())
            .Concat(await context.Set<GroupMemberRoleType>().AsNoTracking().ToListAsync())
            .Concat(await context.Set<ProductType>().AsNoTracking().ToListAsync())
            .Concat(await context.Set<RateType>().AsNoTracking().ToListAsync())
            .Concat(await context.Set<OperatingModeType>().AsNoTracking().ToListAsync())
            .Concat(await context.Set<OperatingInstructionType>().AsNoTracking().ToListAsync())
            .Concat(await context.Set<AccountHolderRoleType>().AsNoTracking().ToListAsync())
            .Concat(await context.Set<CloseReasonType>().AsNoTracking().ToListAsync())
            .Concat(await context.Set<FreezeReleaseActionType>().AsNoTracking().ToListAsync())
            .Concat(await context.Set<LedgerAccountType>().AsNoTracking().ToListAsync())
            .OrderBy(lookup => lookup.GetType().Name, StringComparer.Ordinal)
            .ThenBy(lookup => lookup.Code, StringComparer.Ordinal)
            .Select(lookup => $"{lookup.GetType().Name}:{lookup.Code}={lookup.Name}:{lookup.IsActive}"));

        var events = string.Join('|', (await context.Set<EventType>().AsNoTracking().ToListAsync())
            .OrderBy(eventType => eventType.EventCode, StringComparer.Ordinal)
            .Select(eventType => $"{eventType.EventCode}={eventType.EventName}"));

        var modules = string.Join('|', (await context.Set<Module>().AsNoTracking().ToListAsync())
            .OrderBy(module => module.ModuleCode, StringComparer.Ordinal)
            .Select(module => $"{module.ModuleCode}={module.ModuleName}:{module.HasDataEntry}"));

        var subModules = string.Join('|', (await context.Set<SubModule>().AsNoTracking().ToListAsync())
            .OrderBy(subModule => subModule.SubModuleCode, StringComparer.Ordinal)
            .Select(subModule => $"{subModule.ModuleId}:{subModule.SubModuleCode}={subModule.SubModuleName}"));

        var staff = string.Join('|', (await context.Set<StaffUser>().AsNoTracking().ToListAsync())
            .OrderBy(user => user.StaffCode, StringComparer.Ordinal)
            .Select(user =>
                $"{user.StaffCode}:{user.IsDisabled}:{user.SecretKeyHash}:{user.ChangeKeyAtNextLogOn}:{user.ModifiedOn:O}"));

        var roleGroups = string.Join('|', (await context.Set<RoleGroup>().AsNoTracking().ToListAsync())
            .OrderBy(group => group.RoleGroupCode, StringComparer.Ordinal)
            .Select(group => $"{group.RoleGroupCode}:{group.RoleGroupName}:{group.IsActive}:{group.ModifiedOn:O}"));

        var permissions = string.Join('|', (await context.Set<RoleGroupPermission>().AsNoTracking().ToListAsync())
            .OrderBy(permission => permission.RoleGroupId)
            .ThenBy(permission => permission.ModuleId)
            .ThenBy(permission => permission.SubModuleId)
            .Select(permission =>
                $"{permission.RoleGroupId}:{permission.ModuleId}:{permission.SubModuleId}:{permission.AllowView}{permission.AllowAdd}{permission.AllowEdit}{permission.AllowDelete}{permission.AllowSupervise}"));

        var assignmentRows = string.Join('|', (await context.Set<StaffUserRoleGroup>().AsNoTracking().ToListAsync())
            .OrderBy(row => row.StaffUserId)
            .ThenBy(row => row.RoleGroupId)
            .Select(row => $"{row.StaffUserId}:{row.RoleGroupId}:{row.BranchId}"));

        var currency = await context.Set<Currency>().AsNoTracking().SingleAsync(row => row.IsLocalCurrency);
        var institution = await context.Set<InstitutionProfile>().AsNoTracking().SingleAsync();
        var branch = await context.Set<Branch>().AsNoTracking().SingleAsync();

        return string.Join(
            Environment.NewLine,
            lookups,
            events,
            modules,
            subModules,
            staff,
            roleGroups,
            permissions,
            assignmentRows,
            $"{currency.CurrencyCode}:{currency.IsLocalCurrency}:{currency.ModifiedOn:O}",
            $"{institution.InstitutionProfileId}:{institution.InstitutionName}:{institution.ModifiedOn:O}",
            $"{branch.BranchCode}:{branch.BranchName}:{branch.ModifiedOn:O}",
            $"staff:{(await context.Set<StaffUser>().CountAsync())}",
            $"roleGroups:{(await context.Set<RoleGroup>().CountAsync())}",
            $"permissions:{(await context.Set<RoleGroupPermission>().CountAsync())}",
            $"assignments:{(await context.Set<StaffUserRoleGroup>().CountAsync())}");
    }

    private static BankingDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<BankingDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new BankingDbContext(options);
    }
}
