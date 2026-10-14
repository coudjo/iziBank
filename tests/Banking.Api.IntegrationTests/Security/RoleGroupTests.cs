using System.Net;
using System.Net.Http.Json;
using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Application.Errors;
using Banking.Domain.Security;
using Banking.Domain.Supervision;
using Microsoft.EntityFrameworkCore;

namespace Banking.Api.IntegrationTests.Security;

/// <summary>A2-09 and A2-10: role-group changes wait for a second approver; safety rules from addendum 2.4.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class RoleGroupTests : IAsyncLifetime
{
    private readonly MsSqlContainerFixture _fixture;
    private ApiTestHost _host = null!;
    private string _admin1 = null!;
    private string _admin2 = null!;
    private long _admin2Id;

    public RoleGroupTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _host = await ApiTestHost.CreateAsync(
            _fixture,
            "roles",
            new Dictionary<string, string?> { ["Supervision:AllowSelfApproval"] = "false" });
        _admin1 = await _host.AdminTokenAsync();
        _admin2Id = await _host.CreateStaffAsync(
            "ADMIN2",
            canSwitchBranch: false,
            (_host.HomeBranchId, RoleGroupCatalog.Administrator),
            (_host.HomeBranchId, RoleGroupCatalog.Supervisor));
        _admin2 = await _host.TokenAsync("ADMIN2");
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task A_new_group_and_its_assignment_take_effect_only_after_a_second_admin_approves()
    {
        var clerkId = await _host.CreateStaffAsync("CLERK1", canSwitchBranch: false);
        var clerk = await _host.TokenAsync("CLERK1");

        var create = await RequestAsync(_admin1, HttpMethod.Post, "/api/v1/role-groups", new
        {
            code = "SESSION_VIEWER",
            name = "Session viewer",
            permissions = new[] { Grant("SEC", "SESSIONS", view: true) }
        });
        (await GroupExistsAsync("SESSION_VIEWER")).ShouldBeFalse();
        await ApproveAsync(_admin2, create);
        (await GroupExistsAsync("SESSION_VIEWER")).ShouldBeTrue();

        var assign = await RequestAsync(_admin1, HttpMethod.Put, $"/api/v1/staff-users/{clerkId}/role-groups", new
        {
            branchId = _host.HomeBranchId,
            roleGroupCodes = new[] { "SESSION_VIEWER" }
        });
        (await _host.Client(clerk).GetAsync(new Uri("/api/v1/sessions", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await ApproveAsync(_admin2, assign);
        (await _host.Client(clerk).GetAsync(new Uri("/api/v1/sessions", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_requester_cannot_approve_their_own_change()
    {
        var id = await RequestAsync(_admin1, HttpMethod.Post, "/api/v1/role-groups", new
        {
            code = "OWN_CHANGE",
            name = "Own change",
            permissions = Array.Empty<object>()
        });

        using var response = await _host.Client(_admin1).PostAsync(new Uri($"/api/v1/change-requests/{id}/approve", UriKind.Relative), null);
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApiTestHost.ProblemTypeAsync(response)).ShouldBe(SupervisionProblems.SelfApproval);
    }

    [Fact]
    public async Task Nobody_can_change_their_own_role_groups()
    {
        using var response = await SendAsync(_admin2, HttpMethod.Put, $"/api/v1/staff-users/{_admin2Id}/role-groups", new
        {
            branchId = _host.HomeBranchId,
            roleGroupCodes = new[] { RoleGroupCatalog.Teller }
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApiTestHost.ProblemTypeAsync(response)).ShouldBe(ProblemCodes.SelfAssignment);
    }

    [Fact]
    public async Task Deactivating_the_last_administrator_group_is_refused_and_nothing_changes()
    {
        var groupId = await GroupIdAsync(RoleGroupCatalog.Administrator);
        var id = await RequestAsync(_admin1, HttpMethod.Post, $"/api/v1/role-groups/{groupId}/deactivate", new { });

        using var response = await _host.Client(_admin2).PostAsync(new Uri($"/api/v1/change-requests/{id}/approve", UriKind.Relative), null);
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApiTestHost.ProblemTypeAsync(response)).ShouldBe(ProblemCodes.LastAdministrator);

        await using var context = _host.NewContext();
        (await context.Set<RoleGroup>().SingleAsync(row => row.RoleGroupId == groupId)).IsActive.ShouldBeTrue();
        (await context.Set<ChangeRequest>().SingleAsync(row => row.ChangeRequestId == id)).Status.ShouldBe(ChangeRequestStatuses.Pending);
    }

    [Fact]
    public async Task Deactivating_a_group_removes_access_on_the_next_request()
    {
        await _host.CreateStaffAsync("AUDITOR9", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.Auditor));
        var auditor = await _host.TokenAsync("AUDITOR9");
        (await _host.Client(auditor).GetAsync(new Uri("/api/v1/sessions", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var id = await RequestAsync(_admin1, HttpMethod.Post, $"/api/v1/role-groups/{await GroupIdAsync(RoleGroupCatalog.Auditor)}/deactivate", new { });
        await ApproveAsync(_admin2, id);

        (await _host.Client(auditor).GetAsync(new Uri("/api/v1/sessions", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task One_pending_change_per_group_and_matrix_rules_are_enforced()
    {
        var tellerId = await GroupIdAsync(RoleGroupCatalog.Teller);
        await RequestAsync(_admin1, HttpMethod.Put, $"/api/v1/role-groups/{tellerId}", new
        {
            name = "Teller",
            permissions = new[] { Grant("PTY", "PARTY", view: true, add: true) }
        });

        using var second = await SendAsync(_admin2, HttpMethod.Put, $"/api/v1/role-groups/{tellerId}", new
        {
            name = "Teller again",
            permissions = Array.Empty<object>()
        });
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApiTestHost.ProblemTypeAsync(second)).ShouldBe(ProblemCodes.ChangePendingExists);

        using var invalid = await SendAsync(_admin1, HttpMethod.Post, "/api/v1/role-groups", new
        {
            code = "BAD_MATRIX",
            name = "Bad",
            permissions = new[] { Grant("PTY", "PARTY", view: false, add: true), Grant("PTY", "CHART", view: true) }
        });
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await ApiTestHost.ReadJsonAsync(invalid);
        problem.GetProperty("errors").GetProperty("permissions").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task The_approver_sees_before_and_after()
    {
        var tellerId = await GroupIdAsync(RoleGroupCatalog.Teller);
        var id = await RequestAsync(_admin1, HttpMethod.Put, $"/api/v1/role-groups/{tellerId}", new
        {
            name = "Counter staff",
            permissions = new[] { Grant("PTY", "PARTY", view: true) }
        });

        using var response = await _host.Client(_admin2).GetAsync(new Uri($"/api/v1/change-requests/{id}", UriKind.Relative));
        var view = await ApiTestHost.ReadJsonAsync(response);
        view.GetProperty("before").GetProperty("name").GetString().ShouldBe("Teller");
        view.GetProperty("proposed").GetProperty("name").GetString().ShouldBe("Counter staff");
        view.GetProperty("proposed").GetProperty("permissions").GetArrayLength().ShouldBe(1);
    }

    private static object Grant(string module, string? subModule, bool view, bool add = false) => new
    {
        moduleCode = module,
        subModuleCode = subModule,
        allowView = view,
        allowAdd = add,
        allowEdit = false,
        allowDelete = false,
        allowSupervise = false
    };

    private async Task<HttpResponseMessage> SendAsync(string token, HttpMethod method, string path, object body)
    {
        using var client = _host.Client(token);
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = JsonContent.Create(body) };
        return await client.SendAsync(request);
    }

    private async Task<long> RequestAsync(string token, HttpMethod method, string path, object body)
    {
        using var response = await SendAsync(token, method, path, body);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        return (await ApiTestHost.ReadJsonAsync(response)).GetProperty("changeRequestId").GetInt64();
    }

    private async Task ApproveAsync(string token, long changeRequestId)
    {
        using var response = await _host.Client(token).PostAsync(new Uri($"/api/v1/change-requests/{changeRequestId}/approve", UriKind.Relative), null);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    private async Task<bool> GroupExistsAsync(string code)
    {
        await using var context = _host.NewContext();
        return await context.Set<RoleGroup>().AnyAsync(row => row.RoleGroupCode == code);
    }

    private async Task<int> GroupIdAsync(string code)
    {
        await using var context = _host.NewContext();
        return await context.Set<RoleGroup>().Where(row => row.RoleGroupCode == code).Select(row => row.RoleGroupId).SingleAsync();
    }
}
