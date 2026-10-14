using System.Net;
using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Domain.Lookups;
using Banking.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Banking.Api.IntegrationTests.Security;

/// <summary>A2-06: one activity row per authenticated request; a write's row joins its transaction.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class ActivityLogTests : IAsyncLifetime
{
    private readonly MsSqlContainerFixture _fixture;
    private ApiTestHost _host = null!;

    public ActivityLogTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync() => _host = await ApiTestHost.CreateAsync(_fixture, "activity");

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Each_authenticated_request_writes_exactly_one_row()
    {
        await _host.CreateStaffAsync("COUNT1", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.Teller));
        var token = await _host.TokenAsync("COUNT1");

        using var client = _host.Client(token);
        for (var i = 0; i < 3; i++)
        {
            (await client.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await client.PostAsync(new Uri("/api/v1/auth/logout", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var context = _host.NewContext();
        var sessionId = await context.Set<UserSession>()
            .Where(row => row.StaffUser!.StaffCode == "COUNT1")
            .Select(row => row.SessionId)
            .SingleAsync();

        var codes = await context.Set<UserActivityLog>()
            .Where(row => row.SessionId == sessionId)
            .OrderBy(row => row.EventId)
            .Select(row => row.EventType!.EventCode)
            .ToListAsync();

        codes.ShouldBe([EventCodes.Login, EventCodes.ApiRequest, EventCodes.ApiRequest, EventCodes.ApiRequest, EventCodes.Logout]);
    }

    [Fact]
    public async Task Request_details_never_contain_secrets()
    {
        var token = await _host.AdminTokenAsync("Admin-Pass9");
        (await _host.Client(token).GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var context = _host.NewContext();
        var details = await context.Set<UserActivityLog>()
            .Where(row => row.RequestDetailsJson != null)
            .Select(row => row.RequestDetailsJson!)
            .ToListAsync();

        details.ShouldNotBeEmpty();
        details.ShouldAllBe(json => !json.Contains("Admin-Pass9") && !json.Contains(ApiTestHost.AdminSecret));
    }
}
