using System.Globalization;
using Banking.Domain.Security;
using Banking.Infrastructure.Persistence;
using Banking.Migrator.Sample;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Banking.Migrator;

/// <summary>
/// Applies migrations and seeds reference data. Two development-only commands add sample data afterwards:
/// <c>demo</c> (a small named data set, A6-05) and <c>perf [parties] [accounts]</c> (bulk data for measuring, A6-01).
/// Both refuse to run unless <c>DOTNET_ENVIRONMENT</c> (or <c>ASPNETCORE_ENVIRONMENT</c>) is Development.
/// </summary>
internal static class MigratorHost
{
    public const int Succeeded = 0;
    public const int FailedCode = 1;
    public const int Refused = 2;

    public const string DemoCommand = "demo";
    public const string PerformanceCommand = "perf";
    public const string DemoStaffSecretKey = "DEMO_STAFF_SECRET";

    public static async Task<int> RunAsync(string[] args, IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        using var loggerFactory = LoggerFactory.Create(static builder => builder.AddSimpleConsole());
        var logger = loggerFactory.CreateLogger("Migrator");

        try
        {
            configuration ??= BuildConfiguration();
            var command = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : string.Empty;
            if (command is not ("" or DemoCommand or PerformanceCommand))
            {
                MigratorLog.UnknownCommand(logger, command);
                return Refused;
            }

            // Refuse before touching the database.
            if (command.Length > 0)
            {
                var environment = SampleDataGuard.EnvironmentOf(configuration);
                if (!SampleDataGuard.IsAllowed(environment))
                {
                    MigratorLog.SampleRefused(logger, command, environment ?? "not set");
                    return Refused;
                }
            }

            var demoSecret = configuration[DemoStaffSecretKey] ?? string.Empty;
            if (command == DemoCommand && DemoSecretProblem(demoSecret) is { } secretProblem)
            {
                MigratorLog.InvalidArguments(logger, secretProblem);
                return Refused;
            }

            var counts = (Parties: PerformanceDataGenerator.DefaultParties, Accounts: PerformanceDataGenerator.DefaultAccounts);
            if (command == PerformanceCommand)
            {
                if (!TryReadCounts(args, out counts))
                {
                    MigratorLog.InvalidArguments(logger, "Usage: perf [parties] [accounts], with whole numbers above zero.");
                    return Refused;
                }
            }

            var settings = BootstrapSettings.From(configuration);
            var options = new DbContextOptionsBuilder<BankingDbContext>()
                .UseSqlServer(settings.ConnectionString, static sql => sql.CommandTimeout(300))
                .Options;

            await using var context = new BankingDbContext(options);
            var hasher = new PasswordHasher<StaffUser>();
            await DatabaseInitializer.ApplyAsync(context, settings, hasher, TimeProvider.System).ConfigureAwait(false);
            MigratorLog.Applied(logger);

            var formats = SampleNumberFormats.From(configuration);
            if (command == DemoCommand)
            {
                var demo = await DemoDataSeeder.SeedAsync(context, settings, formats, demoSecret, hasher, TimeProvider.System, CancellationToken.None)
                    .ConfigureAwait(false);
                if (demo.Skipped)
                {
                    MigratorLog.DemoSkipped(logger);
                }
                else
                {
                    MigratorLog.DemoApplied(logger, demo.Parties, demo.Accounts);
                }
            }
            else if (command == PerformanceCommand)
            {
                var result = await PerformanceDataGenerator.GenerateAsync(
                        context, settings, formats, counts.Parties, counts.Accounts, TimeProvider.System, logger, CancellationToken.None)
                    .ConfigureAwait(false);
                MigratorLog.PerformanceApplied(logger, result.Parties, result.Accounts);
            }

            return Succeeded;
        }
        catch (Exception exception)
        {
            MigratorLog.Failed(logger, exception);
            return FailedCode;
        }
    }

    /// <summary>Demo staff sign in with one shared password from the environment; it must pass the normal rules.</summary>
    internal static string? DemoSecretProblem(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return "Set DEMO_STAFF_SECRET to the password the demo staff will use.";
        }

        return SecretKeyRules.Check(secret, "TELLER1", DemoDataSeeder.StaffSecretMinLength).Count > 0
            ? "DEMO_STAFF_SECRET needs at least 10 characters with upper case, lower case and a digit."
            : null;
    }

    private static bool TryReadCounts(string[] args, out (int Parties, int Accounts) counts)
    {
        counts = (PerformanceDataGenerator.DefaultParties, PerformanceDataGenerator.DefaultAccounts);
        if (args.Length > 1)
        {
            if (!int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out var parties) || parties <= 0)
            {
                return false;
            }

            counts.Parties = parties;
        }

        if (args.Length > 2)
        {
            if (!int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out var accounts) || accounts <= 0)
            {
                return false;
            }

            counts.Accounts = accounts;
        }

        return true;
    }

    private static IConfiguration BuildConfiguration()
    {
        EnvFile.LoadFromAncestors(Directory.GetCurrentDirectory());
        return new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();
    }
}
