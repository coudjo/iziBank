using Banking.Domain.Lookups;
using Banking.Domain.Security;
using Banking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Banking.Migrator.Seeding;

internal static class ReferenceDataSeeder
{
    public static async Task SeedAsync(
        BankingDbContext context,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var createdOn = timeProvider.GetUtcNow().UtcDateTime;

        await SeedLookupAsync<RegulatoryCategoryType>(context, LookupCodes.RegulatoryCategories.All, cancellationToken)
            .ConfigureAwait(false);
        await SeedLookupAsync<BusinessDayStatusType>(context, LookupCodes.BusinessDayStatuses.All, cancellationToken)
            .ConfigureAwait(false);
        await SeedLookupAsync<LogoutReasonType>(context, LookupCodes.LogoutReasons.All, cancellationToken)
            .ConfigureAwait(false);
        await SeedLookupAsync<PartyType>(context, LookupCodes.PartyTypes.All, cancellationToken).ConfigureAwait(false);
        await SeedLookupAsync<KYCStatusType>(context, LookupCodes.KycStatuses.All, cancellationToken).ConfigureAwait(false);
        await SeedLookupAsync<GenderType>(context, LookupCodes.Genders.All, cancellationToken).ConfigureAwait(false);
        await SeedLookupAsync<IdentificationDocumentType>(
                context,
                LookupCodes.IdentificationDocumentTypes.All,
                cancellationToken)
            .ConfigureAwait(false);
        await SeedLookupAsync<BusinessCategoryType>(context, LookupCodes.BusinessCategories.All, cancellationToken)
            .ConfigureAwait(false);
        await SeedLookupAsync<GroupMemberRoleType>(context, LookupCodes.GroupMemberRoles.All, cancellationToken)
            .ConfigureAwait(false);
        await SeedLookupAsync<ProductType>(context, LookupCodes.ProductTypes.All, cancellationToken).ConfigureAwait(false);
        await SeedLookupAsync<RateType>(context, LookupCodes.RateTypes.All, cancellationToken).ConfigureAwait(false);
        await SeedLookupAsync<OperatingModeType>(context, LookupCodes.OperatingModes.All, cancellationToken)
            .ConfigureAwait(false);
        await SeedLookupAsync<OperatingInstructionType>(context, LookupCodes.OperatingInstructions.All, cancellationToken)
            .ConfigureAwait(false);
        await SeedLookupAsync<AccountHolderRoleType>(context, LookupCodes.AccountHolderRoles.All, cancellationToken)
            .ConfigureAwait(false);
        await SeedLookupAsync<CloseReasonType>(context, LookupCodes.CloseReasons.All, cancellationToken)
            .ConfigureAwait(false);
        await SeedLookupAsync<FreezeReleaseActionType>(context, LookupCodes.FreezeReleaseActions.All, cancellationToken)
            .ConfigureAwait(false);
        await SeedLookupAsync<LedgerAccountType>(context, LookupCodes.LedgerAccountTypes.All, cancellationToken)
            .ConfigureAwait(false);

        await SeedEventTypesAsync(context, createdOn, cancellationToken).ConfigureAwait(false);
        await SeedModulesAsync(context, createdOn, cancellationToken).ConfigureAwait(false);

        if (context.ChangeTracker.HasChanges())
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        await SeedRoleGroupsAsync(context, createdOn, cancellationToken).ConfigureAwait(false);

        if (context.ChangeTracker.HasChanges())
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Creates missing seeded role groups only. An existing group is never touched, so edits made by
    /// administrators survive a re-run. Seeded rows are approved (no supervision pending).
    /// </summary>
    private static async Task SeedRoleGroupsAsync(
        BankingDbContext context,
        DateTime createdOn,
        CancellationToken cancellationToken)
    {
        var existing = (await context.Set<RoleGroup>()
                .AsNoTracking()
                .Select(group => group.RoleGroupCode)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToHashSet(StringComparer.Ordinal);

        var modules = await context.Set<Module>()
            .AsNoTracking()
            .ToDictionaryAsync(module => module.ModuleCode, module => module.ModuleId, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        var subModules = (await context.Set<SubModule>()
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(subModule => subModule.SubModuleCode, subModule => subModule.SubModuleId, StringComparer.Ordinal);

        foreach (var definition in RoleGroupCatalog.All)
        {
            if (existing.Contains(definition.Code))
            {
                continue;
            }

            var group = new RoleGroup
            {
                RoleGroupCode = definition.Code,
                RoleGroupName = definition.Name,
                Description = definition.Description,
                IsSystemGroup = true,
                IsActive = true,
                IsSupervisionRequired = false,
                CreatedOn = createdOn
            };

            foreach (var grant in definition.Permissions)
            {
                group.Permissions.Add(new RoleGroupPermission
                {
                    ModuleId = modules[grant.ModuleCode],
                    SubModuleId = grant.SubModuleCode is null ? null : subModules[grant.SubModuleCode],
                    AllowView = grant.AllowView,
                    AllowAdd = grant.AllowAdd,
                    AllowEdit = grant.AllowEdit,
                    AllowDelete = grant.AllowDelete,
                    AllowSupervise = grant.AllowSupervise,
                    CreatedOn = createdOn
                });
            }

            context.Set<RoleGroup>().Add(group);
        }
    }

    private static async Task SeedLookupAsync<TLookup>(
        BankingDbContext context,
        IReadOnlyList<string> codes,
        CancellationToken cancellationToken)
        where TLookup : Lookup, new()
    {
        var existing = (await context.Set<TLookup>()
                .AsNoTracking()
                .Select(lookup => lookup.Code)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var code in codes)
        {
            if (existing.Contains(code))
            {
                continue;
            }

            context.Set<TLookup>().Add(new TLookup
            {
                Code = code,
                Name = LookupCodes.ToDisplayName(code),
                IsActive = true
            });
        }
    }

    private static async Task SeedEventTypesAsync(
        BankingDbContext context,
        DateTime createdOn,
        CancellationToken cancellationToken)
    {
        var existing = (await context.Set<EventType>()
                .AsNoTracking()
                .Select(eventType => eventType.EventCode)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var code in EventCodes.All)
        {
            if (existing.Contains(code))
            {
                continue;
            }

            context.Set<EventType>().Add(new EventType
            {
                EventCode = code,
                EventName = LookupCodes.ToDisplayName(code),
                CreatedOn = createdOn
            });
        }
    }

    private static async Task SeedModulesAsync(
        BankingDbContext context,
        DateTime createdOn,
        CancellationToken cancellationToken)
    {
        var existingModules = await context.Set<Module>()
            .AsNoTracking()
            .Select(module => new { module.ModuleId, module.ModuleCode })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var existingSubModules = (await context.Set<SubModule>()
                .AsNoTracking()
                .Select(subModule => subModule.SubModuleCode)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var definition in ModuleCatalog.All)
        {
            var existing = existingModules.Find(module =>
                string.Equals(module.ModuleCode, definition.Code, StringComparison.Ordinal));

            if (existing is null)
            {
                var module = new Module
                {
                    ModuleCode = definition.Code,
                    ModuleName = definition.Name,
                    HasDataEntry = definition.HasDataEntry,
                    IsActive = true,
                    CreatedOn = createdOn
                };

                foreach (var subModule in definition.SubModules)
                {
                    if (existingSubModules.Contains(subModule.Code))
                    {
                        continue;
                    }

                    module.SubModules.Add(new SubModule
                    {
                        SubModuleCode = subModule.Code,
                        SubModuleName = subModule.Name,
                        IsActive = true,
                        CreatedOn = createdOn
                    });
                }

                context.Set<Module>().Add(module);
                continue;
            }

            foreach (var subModule in definition.SubModules)
            {
                if (existingSubModules.Contains(subModule.Code))
                {
                    continue;
                }

                context.Set<SubModule>().Add(new SubModule
                {
                    ModuleId = existing.ModuleId,
                    SubModuleCode = subModule.Code,
                    SubModuleName = subModule.Name,
                    IsActive = true,
                    CreatedOn = createdOn
                });
            }
        }
    }
}
