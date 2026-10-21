using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Domain.Accounts;
using Banking.Domain.Institution;
using Banking.Domain.Lookups;
using Banking.Domain.Parties;
using Banking.Domain.Security;
using Banking.Infrastructure.Persistence;
using Banking.Migrator;
using Banking.Migrator.Sample;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Banking.Api.IntegrationTests.Seed;

/// <summary>A6-01 and A6-05: the development-only sample data commands of the Migrator.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class SampleDataTests
{
    private const string DemoSecret = "Demo-Staff-Pass1";

    private readonly MsSqlContainerFixture _fixture;

    public SampleDataTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("demo", "Production")]
    [InlineData("perf", "Staging")]
    [InlineData("demo", null)]
    public async Task Sample_data_refuses_to_run_outside_development_before_touching_the_database(string command, string? environment)
    {
        var connectionString = await _fixture.CreateDatabaseAsync($"iziBank_refuse_{Guid.NewGuid():N}");

        var exitCode = await MigratorHost.RunAsync([command], Configuration(connectionString, environment));

        exitCode.ShouldBe(MigratorHost.Refused);
        await using var context = CreateContext(connectionString);
        (await context.Database.GetAppliedMigrationsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Unknown_commands_and_bad_counts_are_refused()
    {
        var connectionString = await _fixture.CreateDatabaseAsync($"iziBank_args_{Guid.NewGuid():N}");
        var configuration = Configuration(connectionString, "Development");

        (await MigratorHost.RunAsync(["wipe"], configuration)).ShouldBe(MigratorHost.Refused);
        (await MigratorHost.RunAsync(["perf", "-5"], configuration)).ShouldBe(MigratorHost.Refused);
        (await MigratorHost.RunAsync(["perf", "10", "many"], configuration)).ShouldBe(MigratorHost.Refused);
        MigratorHost.DemoSecretProblem(string.Empty).ShouldNotBeNull();
        MigratorHost.DemoSecretProblem("short").ShouldNotBeNull();
        MigratorHost.DemoSecretProblem(DemoSecret).ShouldBeNull();
    }

    [Fact]
    public async Task Demo_data_is_consistent_with_the_rules_and_is_added_only_once()
    {
        var connectionString = await _fixture.CreateDatabaseAsync($"iziBank_demo_{Guid.NewGuid():N}");
        var configuration = Configuration(connectionString, "Development", DemoSecret);

        (await MigratorHost.RunAsync(["demo"], configuration)).ShouldBe(MigratorHost.Succeeded);
        (await MigratorHost.RunAsync(["demo"], configuration)).ShouldBe(MigratorHost.Succeeded);

        await using var context = CreateContext(connectionString);
        (await context.Set<Party>().CountAsync()).ShouldBe(10);
        (await context.Set<Account>().CountAsync()).ShouldBe(9);
        (await context.Set<BusinessDayStatus>().CountAsync()).ShouldBe(1);

        // One party and one account wait for a supervisor, created by someone other than the approver.
        var pendingParty = await context.Set<Party>().SingleAsync(row => row.SupervisedByUserId == null);
        pendingParty.DisplayName.ShouldBe("Kojo Appiah");
        var pendingAccount = await context.Set<Account>().SingleAsync(row => row.SupervisedByUserId == null);
        var teller = await context.Set<StaffUser>().SingleAsync(row => row.StaffCode == "TELLER1");
        pendingAccount.CreatedByUserId.ShouldBe(teller.StaffUserId);

        // Every signer on every account is approved, KYC verified and has an approved specimen signature (rule 3a).
        var verified = await context.Set<KYCStatusType>().Where(row => row.Code == LookupCodes.KycStatuses.Verified).Select(row => row.Id).SingleAsync();
        var signerRoles = await context.Set<AccountHolderRoleType>().Where(row => row.Code != LookupCodes.AccountHolderRoles.BeneficialOwner).Select(row => row.Id).ToListAsync();
        var individual = await context.Set<PartyType>().Where(row => row.Code == LookupCodes.PartyTypes.Individual).Select(row => row.Id).SingleAsync();
        var signers = await context.Set<AccountHolder>()
            .Where(row => signerRoles.Contains(row.AccountHolderRoleId) && row.Party!.PartyTypeId == individual)
            .Select(row => new
            {
                row.Party!.KYCStatusId,
                Approved = row.Party!.SupervisedByUserId != null,
                Signed = row.Party!.Signatures.Any(signature => signature.SupervisedByUserId != null)
            })
            .ToListAsync();
        signers.ShouldNotBeEmpty();
        signers.ShouldAllBe(row => row.KYCStatusId == verified && row.Approved && row.Signed);

        // Balances follow the calculator, and the states worth seeing are there.
        var balances = await context.Set<AccountBalance>().ToListAsync();
        balances.ShouldAllBe(row => row.AvailableBalance == AccountBalanceCalculator.Available(row) && row.TotalBalance == AccountBalanceCalculator.Total(row));
        (await context.Set<AccountCondition>().CountAsync(row => row.IsFreezed)).ShouldBe(1);
        (await context.Set<FreezeReleaseAccountBalance>().CountAsync()).ShouldBe(1);
        (await context.Set<Account>().CountAsync(row => row.IsDormant)).ShouldBe(1);
        (await context.Set<GroupMember>().CountAsync()).ShouldBe(3);

        // Demo staff sign in with the configured secret and hold their role in the branch, approved.
        new PasswordHasher<StaffUser>().VerifyHashedPassword(teller, teller.SecretKeyHash!, DemoSecret).ShouldBe(PasswordVerificationResult.Success);
        var assignments = await context.Set<StaffUserRoleGroup>().Where(row => row.StaffUserId == teller.StaffUserId).Include(row => row.RoleGroup).ToListAsync();
        assignments.Single().RoleGroup!.RoleGroupCode.ShouldBe(RoleGroupCatalog.Teller);
        assignments.Single().SupervisedByUserId.ShouldNotBeNull();
    }

    [Fact]
    public async Task Performance_data_creates_exactly_the_requested_counts_with_valid_shapes()
    {
        var connectionString = await _fixture.CreateDatabaseAsync($"iziBank_perf_{Guid.NewGuid():N}");

        (await MigratorHost.RunAsync(["perf", "200", "300"], Configuration(connectionString, "Development"))).ShouldBe(MigratorHost.Succeeded);

        await using var context = CreateContext(connectionString);
        (await context.Set<Party>().CountAsync()).ShouldBe(200);
        (await context.Set<Account>().CountAsync()).ShouldBe(300);
        (await context.Set<Account>().Select(row => row.AccountNumber).Distinct().CountAsync()).ShouldBe(300);
        (await context.Set<GroupMember>().CountAsync()).ShouldBe(8);
        (await context.Set<Party>().CountAsync(row => row.SupervisedByUserId == null)).ShouldBe(4);

        // Pending parties hold no accounts; every business and group account has a signatory.
        (await context.Set<AccountHolder>().CountAsync(row => row.Party!.SupervisedByUserId == null)).ShouldBe(0);
        var signatory = await context.Set<AccountHolderRoleType>().Where(row => row.Code == LookupCodes.AccountHolderRoles.Signatory).Select(row => row.Id).SingleAsync();
        var individual = await context.Set<PartyType>().Where(row => row.Code == LookupCodes.PartyTypes.Individual).Select(row => row.Id).SingleAsync();
        (await context.Set<Account>().CountAsync(row =>
                row.Holders.Any(holder => holder.Party!.PartyTypeId != individual)
                && !row.Holders.Any(holder => holder.AccountHolderRoleId == signatory)))
            .ShouldBe(0);
    }

    [Fact]
    public void The_second_account_threshold_reaches_the_account_count()
    {
        PerformanceDataGenerator.SecondAccountThreshold(200, 300).ShouldBe(117);
        PerformanceDataGenerator.SecondAccountThreshold(200, 150).ShouldBe(3);
    }

    private static IConfiguration Configuration(string connectionString, string? environment, string? demoSecret = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Banking"] = connectionString,
                ["BOOTSTRAP_ADMIN_CODE"] = "ADMIN",
                ["BOOTSTRAP_ADMIN_SECRET"] = "ReplaceWithAStrongSecret1",
                ["BOOTSTRAP_BRANCH_CODE"] = "001",
                ["BOOTSTRAP_BRANCH_NAME"] = "Head Office",
                ["DOTNET_ENVIRONMENT"] = environment,
                [MigratorHost.DemoStaffSecretKey] = demoSecret
            })
            .Build();

    private static BankingDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<BankingDbContext>().UseSqlServer(connectionString).Options);
}
