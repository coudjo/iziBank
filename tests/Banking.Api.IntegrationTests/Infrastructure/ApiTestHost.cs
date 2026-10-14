using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Banking.Domain.Institution;
using Banking.Domain.Security;
using Banking.Infrastructure.Persistence;
using Banking.Migrator;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Banking.Api.IntegrationTests.Infrastructure;

/// <summary>A migrated, seeded database plus an API host pointed at it. One per test class.</summary>
public sealed class ApiTestHost : IAsyncDisposable
{
    public const string AdminCode = "ADMIN";
    public const string AdminSecret = "ReplaceWithAStrongSecret1";
    public const string StaffSecret = "Teller-Pass1";

    private ApiTestHost(string connectionString, int homeBranchId)
    {
        ConnectionString = connectionString;
        HomeBranchId = homeBranchId;
        Factory = new BankingApiFactory(connectionString);
    }

    public string ConnectionString { get; }

    public int HomeBranchId { get; }

    public BankingApiFactory Factory { get; }

    public static async Task<ApiTestHost> CreateAsync(MsSqlContainerFixture fixture, string name)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        var connectionString = await fixture.CreateDatabaseAsync($"iziBank_{name}_{Guid.NewGuid():N}");
        var settings = BootstrapSettings.Create(connectionString, AdminCode, AdminSecret, "001", "Head Office");

        await using var context = NewContext(connectionString);
        await DatabaseInitializer.ApplyAsync(context, settings);
        var branchId = await context.Set<Branch>().Where(row => row.BranchCode == "001").Select(row => row.BranchId).SingleAsync();
        return new ApiTestHost(connectionString, branchId);
    }

    public BankingDbContext NewContext() => NewContext(ConnectionString);

    public async Task<int> CreateBranchAsync(string code)
    {
        await using var context = NewContext();
        var branch = new Branch { BranchCode = code, BranchName = $"Branch {code}" };
        context.Add(branch);
        await context.SaveChangesAsync();
        return branch.BranchId;
    }

    /// <summary>Creates an active staff member with approved role-group assignments, keyed by branch.</summary>
    public async Task<long> CreateStaffAsync(
        string code,
        bool canSwitchBranch,
        params (int BranchId, string RoleGroupCode)[] assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        await using var context = NewContext();

        var user = new StaffUser
        {
            BranchId = HomeBranchId,
            StaffCode = code,
            FullName = $"Staff {code}",
            CanChangeSecretKey = true,
            ChangeKeyAtNextLogOn = false,
            CanSwitchBranch = canSwitchBranch
        };
        user.SecretKeyHash = new PasswordHasher<StaffUser>().HashPassword(user, StaffSecret);
        context.Add(user);
        await context.SaveChangesAsync();

        foreach (var (branchId, roleGroupCode) in assignments)
        {
            var roleGroupId = await context.Set<RoleGroup>()
                .Where(row => row.RoleGroupCode == roleGroupCode)
                .Select(row => row.RoleGroupId)
                .SingleAsync();
            context.Add(new StaffUserRoleGroup
            {
                StaffUserId = user.StaffUserId,
                RoleGroupId = roleGroupId,
                BranchId = branchId,
                IsSupervisionRequired = false
            });
        }

        await context.SaveChangesAsync();
        return user.StaffUserId;
    }

    public HttpClient Client(string? token = null, int? branchId = null)
    {
        var client = Factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (branchId is not null)
        {
            client.DefaultRequestHeaders.Add("X-Branch-Id", branchId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return client;
    }

    public async Task<HttpResponseMessage> LoginAsync(string code, string secret, int? branchId = null)
    {
        using var client = Client();
        return await client.PostAsJsonAsync("/api/v1/auth/login", new { staffCode = code, secretKey = secret, branchId });
    }

    public async Task<string> TokenAsync(string code, string secret = StaffSecret, int? branchId = null)
    {
        using var response = await LoginAsync(code, secret, branchId);
        response.EnsureSuccessStatusCode();
        return (await ReadJsonAsync(response)).GetProperty("accessToken").GetString()!;
    }

    /// <summary>Signs the bootstrap admin in and completes the forced password change.</summary>
    public async Task<string> AdminTokenAsync(string newSecret = "Admin-Pass2")
    {
        var first = await TokenAsync(AdminCode, AdminSecret);
        using var client = Client(first);
        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/change-secret-key",
            new { currentSecretKey = AdminSecret, newSecretKey = newSecret });
        response.EnsureSuccessStatusCode();
        return (await ReadJsonAsync(response)).GetProperty("accessToken").GetString()!;
    }

    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    public static async Task<string?> ProblemTypeAsync(HttpResponseMessage response) =>
        (await ReadJsonAsync(response)).TryGetProperty("type", out var type) ? type.GetString() : null;

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
    }

    private static BankingDbContext NewContext(string connectionString) =>
        new(new DbContextOptionsBuilder<BankingDbContext>().UseSqlServer(connectionString).Options);
}
