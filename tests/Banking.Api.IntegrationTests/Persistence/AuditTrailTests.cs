using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Application.Abstractions;
using Banking.Domain.Institution;
using Banking.Domain.Lookups;
using Banking.Domain.Parties;
using Banking.Domain.Security;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Persistence.Interceptors;
using Banking.Migrator;
using Microsoft.EntityFrameworkCore;

namespace Banking.Api.IntegrationTests.Persistence;

/// <summary>A2-01 audit footer and A2-02 data change audit, against a real SQL Server 2022 ledger table.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class AuditTrailTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 9, 30, 0, DateTimeKind.Utc);

    private readonly MsSqlContainerFixture _fixture;

    public AuditTrailTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Insert_and_update_fill_the_footer_and_created_values_never_change()
    {
        var (connectionString, user) = await PrepareAsync();

        long branchId;
        await using (var context = CreateContext(connectionString, user))
        {
            var branch = new Branch { BranchCode = "T01", BranchName = "Test branch" };
            context.Add(branch);
            await context.SaveChangesAsync();
            branchId = branch.BranchId;
        }

        await using (var context = CreateContext(connectionString, user, Now.AddHours(1)))
        {
            var branch = await context.Set<Branch>().SingleAsync(row => row.BranchId == branchId);
            branch.CreatedByUserId.ShouldBe(user.StaffUserId);
            branch.CreatedOn.ShouldBe(Now);
            branch.ModifiedOn.ShouldBeNull();

            branch.BranchName = "Renamed branch";
            branch.CreatedOn = Now.AddYears(-1);
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext(connectionString, user))
        {
            var branch = await context.Set<Branch>().AsNoTracking().SingleAsync(row => row.BranchId == branchId);
            branch.CreatedOn.ShouldBe(Now);
            branch.ModifiedByUserId.ShouldBe(user.StaffUserId);
            branch.ModifiedOn.ShouldBe(Now.AddHours(1));
        }
    }

    [Fact]
    public async Task Insert_update_and_delete_write_audit_rows_with_changed_columns_only()
    {
        var (connectionString, user) = await PrepareAsync();

        int branchId;
        await using (var context = CreateContext(connectionString, user))
        {
            var branch = new Branch { BranchCode = "T02", BranchName = "Audit branch" };
            context.Add(branch);
            await context.SaveChangesAsync();
            branchId = branch.BranchId;

            branch.BranchName = "Audit branch renamed";
            await context.SaveChangesAsync();

            context.Remove(branch);
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext(connectionString, user))
        {
            var rows = await context.Set<DataChangeAuditLog>()
                .AsNoTracking()
                .Where(row => row.TableName == nameof(Branch) && row.RecordId == branchId.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .OrderBy(row => row.AuditId)
                .ToListAsync();

            rows.Select(row => row.ActionType).ShouldBe(["INSERT", "UPDATE", "DELETE"]);
            rows.ShouldAllBe(row => row.StaffUserId == user.StaffUserId && row.BranchId == user.BranchId);

            rows[0].OldValueJson.ShouldBeNull();
            rows[0].NewValueJson!.ShouldContain("\"BranchCode\":\"T02\"");

            rows[1].OldValueJson.ShouldBe("{\"BranchName\":\"Audit branch\"}");
            rows[1].NewValueJson.ShouldBe("{\"BranchName\":\"Audit branch renamed\"}");

            rows[2].OldValueJson!.ShouldContain("Audit branch renamed");
            rows[2].NewValueJson.ShouldBeNull();
        }
    }

    [Fact]
    public async Task Secret_hashes_and_image_bytes_never_reach_the_audit_log()
    {
        var (connectionString, user) = await PrepareAsync();

        long partyId;
        await using (var context = CreateContext(connectionString, user))
        {
            var admin = await context.Set<StaffUser>().SingleAsync(row => row.StaffUserId == user.StaffUserId);
            admin.SecretKeyHash = "NEW-HASH-VALUE";
            admin.FullName = "Administrator renamed";

            var partyTypeId = await context.Set<PartyType>()
                .Where(row => row.Code == LookupCodes.PartyTypes.Individual)
                .Select(row => row.Id)
                .SingleAsync();
            var party = new Party { PartyNumber = "00000001", DisplayName = "Ama Mensah", PartyTypeId = partyTypeId };
            context.Add(party);
            await context.SaveChangesAsync();
            partyId = party.PartyId;

            context.Add(new PartyImage { PartyId = partyId, ImageValue = [1, 2, 3, 4] });
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext(connectionString, user))
        {
            var json = string.Join(
                '\n',
                await context.Set<DataChangeAuditLog>()
                    .AsNoTracking()
                    .Select(row => (row.OldValueJson ?? string.Empty) + (row.NewValueJson ?? string.Empty))
                    .ToListAsync());

            json.ShouldNotContain("NEW-HASH-VALUE");
            json.ShouldContain("Administrator renamed");
            json.ShouldContain("[redacted]");
            json.ShouldContain("[binary, 4 bytes]");
        }
    }

    [Fact]
    public async Task Business_change_rolls_back_when_its_audit_rows_cannot_be_written()
    {
        var (connectionString, user) = await PrepareAsync();
        var brokenUser = new TestUser(user.StaffUserId, BranchId: 999_999, SessionId: null);

        await using (var context = CreateContext(connectionString, brokenUser))
        {
            context.Add(new Branch { BranchCode = "T03", BranchName = "Never saved" });
            await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }

        await using (var context = CreateContext(connectionString, user))
        {
            (await context.Set<Branch>().AnyAsync(row => row.BranchCode == "T03")).ShouldBeFalse();
        }
    }

    private async Task<(string ConnectionString, TestUser User)> PrepareAsync()
    {
        var connectionString = await _fixture.CreateDatabaseAsync($"iziBank_audit_{Guid.NewGuid():N}");
        var settings = BootstrapSettings.Create(connectionString, "ADMIN", "ReplaceWithAStrongSecret1", "001", "Head Office");

        await using (var context = new BankingDbContext(
                         new DbContextOptionsBuilder<BankingDbContext>().UseSqlServer(connectionString).Options))
        {
            await DatabaseInitializer.ApplyAsync(context, settings);
            var admin = await context.Set<StaffUser>().SingleAsync(row => row.StaffCode == "ADMIN");
            return (connectionString, new TestUser(admin.StaffUserId, admin.BranchId, SessionId: null));
        }
    }

    private static BankingDbContext CreateContext(string connectionString, ICurrentUser user, DateTime? now = null)
    {
        var time = new FixedTimeProvider(now ?? Now);
        var options = new DbContextOptionsBuilder<BankingDbContext>()
            .UseSqlServer(connectionString)
            .AddInterceptors(new AuditFooterInterceptor(user, time), new DataChangeAuditInterceptor(user, time))
            .Options;

        return new BankingDbContext(options);
    }

    private sealed record TestUser(long? StaffUserId, int? BranchId, long? SessionId) : ICurrentUser;

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
