using Banking.Domain.Institution;
using Banking.Domain.Security;
using Banking.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Banking.Migrator.Seeding;

internal static class BootstrapSeeder
{
    public static async Task SeedAsync(
        BankingDbContext context,
        BootstrapSettings settings,
        IPasswordHasher<StaffUser> passwordHasher,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(passwordHasher);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var createdOn = timeProvider.GetUtcNow().UtcDateTime;

        await SeedLocalCurrencyAsync(context, createdOn, cancellationToken).ConfigureAwait(false);
        await SeedInstitutionProfileAsync(context, createdOn, cancellationToken).ConfigureAwait(false);
        var branchId = await SeedBranchAsync(context, settings, createdOn, cancellationToken).ConfigureAwait(false);

        if (context.ChangeTracker.HasChanges())
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            branchId = await context.Set<Branch>()
                .AsNoTracking()
                .Where(branch => branch.BranchCode == settings.BranchCode)
                .Select(branch => branch.BranchId)
                .SingleAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        await SeedSystemUserAsync(context, branchId, createdOn, cancellationToken).ConfigureAwait(false);
        var adminId = await SeedAdminUserAsync(context, settings, passwordHasher, branchId, createdOn, cancellationToken)
            .ConfigureAwait(false);

        if (context.ChangeTracker.HasChanges())
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            adminId = await context.Set<StaffUser>()
                .AsNoTracking()
                .Where(user => user.StaffCode == settings.AdminCode)
                .Select(user => user.StaffUserId)
                .SingleAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        await SeedAdminRoleGroupsAsync(context, adminId, branchId, createdOn, cancellationToken).ConfigureAwait(false);

        if (context.ChangeTracker.HasChanges())
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task SeedLocalCurrencyAsync(
        BankingDbContext context,
        DateTime createdOn,
        CancellationToken cancellationToken)
    {
        var hasLocal = await context.Set<Currency>()
            .AsNoTracking()
            .AnyAsync(currency => currency.IsLocalCurrency, cancellationToken)
            .ConfigureAwait(false);
        if (hasLocal)
        {
            return;
        }

        var ghanaCedi = await context.Set<Currency>()
            .FirstOrDefaultAsync(
                currency => currency.CurrencyCode == CurrencyCodes.GhanaCedi,
                cancellationToken)
            .ConfigureAwait(false);

        if (ghanaCedi is null)
        {
            context.Set<Currency>().Add(new Currency
            {
                CurrencyCode = CurrencyCodes.GhanaCedi,
                CurrencyName = CurrencyCodes.GhanaCediName,
                ExchangeRate = 1m,
                IsLocalCurrency = true,
                CreatedOn = createdOn
            });
            return;
        }

        ghanaCedi.IsLocalCurrency = true;
    }

    private static async Task SeedInstitutionProfileAsync(
        BankingDbContext context,
        DateTime createdOn,
        CancellationToken cancellationToken)
    {
        var exists = await context.Set<InstitutionProfile>()
            .AsNoTracking()
            .AnyAsync(cancellationToken)
            .ConfigureAwait(false);
        if (exists)
        {
            return;
        }

        context.Set<InstitutionProfile>().Add(new InstitutionProfile
        {
            InstitutionName = string.Empty,
            CreatedOn = createdOn
        });
    }

    private static async Task<int> SeedBranchAsync(
        BankingDbContext context,
        BootstrapSettings settings,
        DateTime createdOn,
        CancellationToken cancellationToken)
    {
        var existingId = await context.Set<Branch>()
            .AsNoTracking()
            .Where(branch => branch.BranchCode == settings.BranchCode)
            .Select(branch => (int?)branch.BranchId)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (existingId is not null)
        {
            return existingId.Value;
        }

        context.Set<Branch>().Add(new Branch
        {
            BranchCode = settings.BranchCode,
            BranchName = settings.BranchName,
            IsActive = true,
            CreatedOn = createdOn
        });

        return 0;
    }

    private static async Task SeedSystemUserAsync(
        BankingDbContext context,
        int branchId,
        DateTime createdOn,
        CancellationToken cancellationToken)
    {
        var exists = await context.Set<StaffUser>()
            .AsNoTracking()
            .AnyAsync(user => user.StaffCode == StaffCodes.System, cancellationToken)
            .ConfigureAwait(false);
        if (exists)
        {
            return;
        }

        context.Set<StaffUser>().Add(new StaffUser
        {
            BranchId = branchId,
            StaffCode = StaffCodes.System,
            FullName = StaffCodes.SystemFullName,
            IsDisabled = true,
            IsTeller = false,
            CreatedOn = createdOn
        });
    }

    private static async Task<long> SeedAdminUserAsync(
        BankingDbContext context,
        BootstrapSettings settings,
        IPasswordHasher<StaffUser> passwordHasher,
        int branchId,
        DateTime createdOn,
        CancellationToken cancellationToken)
    {
        var existingId = await context.Set<StaffUser>()
            .AsNoTracking()
            .Where(user => user.StaffCode == settings.AdminCode)
            .Select(user => (long?)user.StaffUserId)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (existingId is not null)
        {
            return existingId.Value;
        }

        var admin = new StaffUser
        {
            BranchId = branchId,
            StaffCode = settings.AdminCode,
            FullName = settings.AdminCode,
            IsDisabled = false,
            IsTeller = false,
            CanChangeSecretKey = true,
            ChangeKeyAtNextLogOn = true,
            CanSwitchBranch = true,
            CreatedOn = createdOn
        };

        admin.SecretKeyHash = passwordHasher.HashPassword(admin, settings.AdminSecret);
        context.Set<StaffUser>().Add(admin);
        return 0;
    }

    /// <summary>
    /// The bootstrap administrator holds ADMINISTRATOR and SUPERVISOR in the bootstrap branch, so the
    /// institution starts with SEC/USERS, SEC/PERMISSIONS and PTY/KYC covered (last-administrator guard).
    /// These assignments are approved at seed time; later assignment changes need supervision.
    /// </summary>
    private static async Task SeedAdminRoleGroupsAsync(
        BankingDbContext context,
        long adminId,
        int branchId,
        DateTime createdOn,
        CancellationToken cancellationToken)
    {
        var codes = RoleGroupCatalog.BootstrapAdministratorGroups.ToArray();
        var groups = await context.Set<RoleGroup>()
            .AsNoTracking()
            .Where(group => codes.Contains(group.RoleGroupCode))
            .Select(group => new { group.RoleGroupId, group.RoleGroupCode })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var assigned = (await context.Set<StaffUserRoleGroup>()
                .AsNoTracking()
                .Where(row => row.StaffUserId == adminId && row.BranchId == branchId)
                .Select(row => row.RoleGroupId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToHashSet();

        foreach (var group in groups.Where(group => !assigned.Contains(group.RoleGroupId)))
        {
            context.Set<StaffUserRoleGroup>().Add(new StaffUserRoleGroup
            {
                StaffUserId = adminId,
                RoleGroupId = group.RoleGroupId,
                BranchId = branchId,
                IsSupervisionRequired = false,
                CreatedOn = createdOn
            });
        }
    }
}
