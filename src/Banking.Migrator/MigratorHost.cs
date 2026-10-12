using Banking.Domain.Security;
using Banking.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Banking.Migrator;

internal static class MigratorHost
{
    public static async Task<int> RunAsync(string[] args, IConfiguration? configuration = null)
    {
        _ = args;

        using var loggerFactory = LoggerFactory.Create(static builder => builder.AddSimpleConsole());
        var logger = loggerFactory.CreateLogger("Migrator");

        try
        {
            configuration ??= BuildConfiguration();
            var settings = BootstrapSettings.From(configuration);
            var options = new DbContextOptionsBuilder<BankingDbContext>()
                .UseSqlServer(settings.ConnectionString)
                .Options;

            await using var context = new BankingDbContext(options);
            await DatabaseInitializer.ApplyAsync(
                    context,
                    settings,
                    new PasswordHasher<StaffUser>(),
                    TimeProvider.System)
                .ConfigureAwait(false);

            MigratorLog.Applied(logger);
            return 0;
        }
        catch (Exception exception)
        {
            MigratorLog.Failed(logger, exception);
            return 1;
        }
    }

    private static IConfiguration BuildConfiguration()
    {
        EnvFile.LoadFromAncestors(Directory.GetCurrentDirectory());

        return new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();
    }
}
