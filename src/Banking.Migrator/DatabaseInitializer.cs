using Banking.Domain.Security;
using Banking.Infrastructure.Persistence;
using Banking.Migrator.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Banking.Migrator;

internal static class DatabaseInitializer
{
    public static async Task ApplyAsync(
        BankingDbContext context,
        BootstrapSettings settings,
        IPasswordHasher<StaffUser>? passwordHasher = null,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(settings);

        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        await ReferenceDataSeeder.SeedAsync(context, timeProvider ?? TimeProvider.System, cancellationToken)
            .ConfigureAwait(false);
        await BootstrapSeeder.SeedAsync(
                context,
                settings,
                passwordHasher ?? new PasswordHasher<StaffUser>(),
                timeProvider ?? TimeProvider.System,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
