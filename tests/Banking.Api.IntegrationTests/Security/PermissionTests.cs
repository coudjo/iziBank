using System.Net;
using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Api.Security;
using Banking.Application.Errors;
using Banking.Domain.Lookups;
using Banking.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Banking.Api.IntegrationTests.Security;

/// <summary>A2-05: role-group union per branch, X-Branch-Id, deny by default, denial logging.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class PermissionTests : IAsyncLifetime
{
    private readonly MsSqlContainerFixture _fixture;
    private ApiTestHost _host = null!;

    public PermissionTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync() => _host = await ApiTestHost.CreateAsync(_fixture, "perm");

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Teller_is_denied_the_session_viewer_and_the_denial_is_logged()
    {
        await _host.CreateStaffAsync("TELLER1", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.Teller));
        var token = await _host.TokenAsync("TELLER1");

        using var response = await _host.Client(token).GetAsync(new Uri("/api/v1/sessions", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ApiTestHost.ProblemTypeAsync(response)).ShouldBe(ProblemCodes.PermissionDenied);

        await using var context = _host.NewContext();
        var deniedId = await context.Set<EventType>().Where(row => row.EventCode == EventCodes.PermissionDenied).Select(row => row.EventTypeId).SingleAsync();
        var row = await context.Set<UserActivityLog>().SingleAsync(row => row.EventTypeId == deniedId && row.SessionId != null);
        row.Outcome.ShouldBe("Denied");
        row.RequestDetailsJson!.ShouldContain("SEC/SESSIONS/View");
    }

    [Fact]
    public async Task Auditor_can_view_sessions()
    {
        await _host.CreateStaffAsync("AUDIT1", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.Auditor));
        var token = await _host.TokenAsync("AUDIT1");

        using var response = await _host.Client(token).GetAsync(new Uri("/api/v1/sessions", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ApiTestHost.ReadJsonAsync(response)).GetProperty("totalCount").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Permissions_follow_the_branch_and_switching_needs_an_assignment_there()
    {
        var otherBranch = await _host.CreateBranchAsync("002");
        await _host.CreateStaffAsync(
            "SPLIT1",
            canSwitchBranch: true,
            (_host.HomeBranchId, RoleGroupCatalog.Teller),
            (otherBranch, RoleGroupCatalog.Auditor));
        await _host.CreateStaffAsync("NOSWITCH", canSwitchBranch: false, (otherBranch, RoleGroupCatalog.Auditor));

        var token = await _host.TokenAsync("SPLIT1");

        using var home = await _host.Client(token).GetAsync(new Uri("/api/v1/sessions", UriKind.Relative));
        home.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var switched = await _host.Client(token, otherBranch).GetAsync(new Uri("/api/v1/sessions", UriKind.Relative));
        switched.StatusCode.ShouldBe(HttpStatusCode.OK);

        var noSwitchToken = await _host.TokenAsync("NOSWITCH");
        using var refused = await _host.Client(noSwitchToken, otherBranch).GetAsync(new Uri("/api/v1/sessions", UriKind.Relative));
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ApiTestHost.ProblemTypeAsync(refused)).ShouldBe(ProblemCodes.BranchNotAllowed);

        using var me = await _host.Client(token, otherBranch).GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative));
        var body = await ApiTestHost.ReadJsonAsync(me);
        body.GetProperty("branchId").GetInt32().ShouldBe(otherBranch);
        body.GetProperty("branches").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public void Every_api_endpoint_declares_its_access_rule()
    {
        var endpoints = _host.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/v1", StringComparison.Ordinal) == true)
            .ToList();

        endpoints.ShouldNotBeEmpty();
        foreach (var endpoint in endpoints)
        {
            var declared = new object?[]
            {
                endpoint.Metadata.GetMetadata<RequirePermissionAttribute>(),
                endpoint.Metadata.GetMetadata<AuthenticatedOnlyAttribute>(),
                endpoint.Metadata.GetMetadata<IAllowAnonymous>()
            }.Count(static item => item is not null);

            declared.ShouldBe(1, endpoint.RoutePattern.RawText);
        }
    }
}
