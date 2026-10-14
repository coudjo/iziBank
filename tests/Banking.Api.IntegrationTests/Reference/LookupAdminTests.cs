using System.Net;
using System.Net.Http.Json;
using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Application.Errors;
using Banking.Domain.Lookups;
using Banking.Domain.Security;

namespace Banking.Api.IntegrationTests.Reference;

/// <summary>A2-03: lookups are readable by anyone signed in; codes are immutable; coded rows cannot be deactivated.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class LookupAdminTests : IAsyncLifetime
{
    private readonly MsSqlContainerFixture _fixture;
    private ApiTestHost _host = null!;
    private string _admin = null!;

    public LookupAdminTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _host = await ApiTestHost.CreateAsync(_fixture, "lookups");
        _admin = await _host.AdminTokenAsync();
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Any_signed_in_user_can_read_a_lookup_but_a_teller_cannot_add_to_it()
    {
        await _host.CreateStaffAsync("TELLER7", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.Teller));
        using var teller = _host.Client(await _host.TokenAsync("TELLER7"));

        using var genders = await teller.GetAsync(new Uri("/api/v1/lookups/genders", UriKind.Relative));
        genders.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ApiTestHost.ReadJsonAsync(genders)).GetArrayLength().ShouldBe(LookupCodes.Genders.All.Count);

        (await teller.GetAsync(new Uri("/api/v1/lookups/no-such-type", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var denied = await teller.PostAsJsonAsync("/api/v1/lookups/business-categories", new { code = "FISHING", name = "Fishing" });
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_adds_renames_and_deactivates_a_new_row()
    {
        using var client = _host.Client(_admin);

        using var created = await client.PostAsJsonAsync("/api/v1/lookups/business-categories", new { code = "fishing", name = "Fishing" });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await ApiTestHost.ReadJsonAsync(created)).GetProperty("id").GetInt32();

        using var duplicate = await client.PostAsJsonAsync("/api/v1/lookups/business-categories", new { code = "FISHING", name = "Again" });
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApiTestHost.ProblemTypeAsync(duplicate)).ShouldBe(ProblemCodes.LookupCodeTaken);

        using var renamed = await client.PutAsJsonAsync($"/api/v1/lookups/business-categories/{id}", new { name = "Fishing and fish trade", isActive = false });
        renamed.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var active = await client.GetAsync(new Uri("/api/v1/lookups/business-categories", UriKind.Relative));
        (await ApiTestHost.ReadJsonAsync(active)).EnumerateArray().ShouldNotContain(row => row.GetProperty("code").GetString() == "FISHING");

        using var all = await client.GetAsync(new Uri("/api/v1/lookups/business-categories?includeInactive=true", UriKind.Relative));
        var row = (await ApiTestHost.ReadJsonAsync(all)).EnumerateArray().Single(item => item.GetProperty("code").GetString() == "FISHING");
        row.GetProperty("name").GetString().ShouldBe("Fishing and fish trade");
        row.GetProperty("isActive").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Codes_never_change_coded_rows_stay_active_and_nothing_is_deleted()
    {
        using var client = _host.Client(_admin);
        using var list = await client.GetAsync(new Uri("/api/v1/lookups/genders", UriKind.Relative));
        var first = (await ApiTestHost.ReadJsonAsync(list)).EnumerateArray().First();
        var id = first.GetProperty("id").GetInt32();
        var code = first.GetProperty("code").GetString();

        using var recode = await client.PutAsJsonAsync($"/api/v1/lookups/genders/{id}", new { code = "OTHER_CODE", name = "Renamed" });
        recode.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiTestHost.ProblemTypeAsync(recode)).ShouldBe(ProblemCodes.LookupCodeImmutable);

        using var deactivate = await client.PutAsJsonAsync($"/api/v1/lookups/genders/{id}", new { code, name = "Renamed", isActive = false });
        deactivate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApiTestHost.ProblemTypeAsync(deactivate)).ShouldBe(ProblemCodes.LookupCodedRow);

        using var delete = await client.DeleteAsync(new Uri($"/api/v1/lookups/genders/{id}", UriKind.Relative));
        delete.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
    }
}
