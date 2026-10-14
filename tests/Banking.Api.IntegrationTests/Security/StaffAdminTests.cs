using System.Net;
using System.Net.Http.Json;
using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Application.Errors;
using Banking.Domain.Lookups;
using Banking.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Banking.Api.IntegrationTests.Security;

/// <summary>A2-08: staff users and audit viewers.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class StaffAdminTests : IAsyncLifetime
{
    private readonly MsSqlContainerFixture _fixture;
    private ApiTestHost _host = null!;
    private string _admin = null!;

    public StaffAdminTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _host = await ApiTestHost.CreateAsync(_fixture, "staff");
        _admin = await _host.AdminTokenAsync();
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task New_staff_must_change_the_initial_password_and_codes_are_unique()
    {
        using var client = _host.Client(_admin);
        var body = new { staffCode = "ama01", fullName = "Ama Mensah", branchId = _host.HomeBranchId, canSwitchBranch = false, initialSecretKey = "Welcome-2026" };

        using var created = await client.PostAsJsonAsync("/api/v1/staff-users", body);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var duplicate = await client.PostAsJsonAsync("/api/v1/staff-users", body);
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApiTestHost.ProblemTypeAsync(duplicate)).ShouldBe(ProblemCodes.StaffCodeTaken);

        using var login = await _host.LoginAsync("AMA01", "Welcome-2026");
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ApiTestHost.ReadJsonAsync(login)).GetProperty("mustChangeSecretKey").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Disabling_a_user_ends_their_session_and_admins_cannot_change_themselves()
    {
        var tellerId = await _host.CreateStaffAsync("TELLER5", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.Teller));
        using var teller = _host.Client(await _host.TokenAsync("TELLER5"));
        (await teller.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var client = _host.Client(_admin);
        using var disabled = await client.PutAsJsonAsync($"/api/v1/staff-users/{tellerId}", new { fullName = "Teller Five", canSwitchBranch = false, isDisabled = true });
        disabled.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var after = await teller.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative));
        after.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        await using (var context = _host.NewContext())
        {
            var reason = await context.Set<UserSession>()
                .Where(row => row.StaffUserId == tellerId)
                .Select(row => row.LogoutReason!.Code)
                .SingleAsync();
            reason.ShouldBe(LookupCodes.LogoutReasons.AccountDisabled);
        }

        long adminId;
        await using (var context = _host.NewContext())
        {
            adminId = await context.Set<StaffUser>().Where(row => row.StaffCode == ApiTestHost.AdminCode).Select(row => row.StaffUserId).SingleAsync();
        }

        using var self = await client.PutAsJsonAsync($"/api/v1/staff-users/{adminId}", new { fullName = "Me", canSwitchBranch = true, isDisabled = true });
        self.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApiTestHost.ProblemTypeAsync(self)).ShouldBe(ProblemCodes.StaffSelfChange);
    }

    [Fact]
    public async Task Disabling_the_last_full_administrator_is_refused()
    {
        // USERMGR can manage users but cannot supervise KYC, so the bootstrap admin is the last full administrator.
        await _host.CreateStaffAsync("USERMGR", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.Administrator));
        using var userManager = _host.Client(await _host.TokenAsync("USERMGR"));

        long adminId;
        await using (var context = _host.NewContext())
        {
            adminId = await context.Set<StaffUser>().Where(row => row.StaffCode == ApiTestHost.AdminCode).Select(row => row.StaffUserId).SingleAsync();
        }

        using var refused = await userManager.PutAsJsonAsync(
            $"/api/v1/staff-users/{adminId}",
            new { fullName = "Administrator", canSwitchBranch = true, isDisabled = true });
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApiTestHost.ProblemTypeAsync(refused)).ShouldBe(ProblemCodes.LastAdministrator);

        await using (var context = _host.NewContext())
        {
            (await context.Set<StaffUser>().SingleAsync(row => row.StaffUserId == adminId)).IsDisabled.ShouldBeFalse();
            (await context.Set<UserSession>().CountAsync(row => row.StaffUserId == adminId && row.LogoutAt == null)).ShouldBe(1);
        }

        (await _host.Client(_admin).GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reset_password_forces_a_change_and_ends_the_session()
    {
        var tellerId = await _host.CreateStaffAsync("TELLER6", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.Teller));
        using var teller = _host.Client(await _host.TokenAsync("TELLER6"));

        using var reset = await _host.Client(_admin).PostAsJsonAsync($"/api/v1/staff-users/{tellerId}/reset-secret-key", new { newSecretKey = "Temp-Pass77" });
        reset.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await teller.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var login = await _host.LoginAsync("TELLER6", "Temp-Pass77");
        (await ApiTestHost.ReadJsonAsync(login)).GetProperty("mustChangeSecretKey").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Auditor_reads_both_audit_trails_and_the_read_itself_is_logged()
    {
        await _host.CreateStaffAsync("AUDITOR3", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.Auditor));
        await _host.CreateStaffAsync("TELLER8", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.Teller));
        using var auditor = _host.Client(await _host.TokenAsync("AUDITOR3"));
        using var teller = _host.Client(await _host.TokenAsync("TELLER8"));

        using var activity = await auditor.GetAsync(new Uri("/api/v1/audit/activity?eventCode=LOGIN", UriKind.Relative));
        activity.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ApiTestHost.ReadJsonAsync(activity)).GetProperty("totalCount").GetInt32().ShouldBeGreaterThanOrEqualTo(2);

        using var changes = await auditor.GetAsync(new Uri("/api/v1/audit/changes?tableName=StaffUser", UriKind.Relative));
        changes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await changes.Content.ReadAsStringAsync();
        json.ShouldContain("[redacted]");

        (await teller.GetAsync(new Uri("/api/v1/audit/activity", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await using var context = _host.NewContext();
        var auditViewId = await context.Set<EventType>().Where(row => row.EventCode == EventCodes.AuditView).Select(row => row.EventTypeId).SingleAsync();
        (await context.Set<UserActivityLog>().CountAsync(row => row.EventTypeId == auditViewId)).ShouldBe(2);
    }
}
