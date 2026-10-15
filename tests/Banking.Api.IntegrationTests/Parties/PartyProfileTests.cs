using System.Net;
using System.Net.Http.Json;
using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Application.Errors;
using Banking.Domain.Parties;
using Banking.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Banking.Api.IntegrationTests.Parties;

/// <summary>A4-05: next of kin, regulatory attributes (append-only), credit officers.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class PartyProfileTests : IAsyncLifetime
{
    private readonly MsSqlContainerFixture _fixture;
    private ApiTestHost _host = null!;
    private HttpClient _officer = null!;
    private HttpClient _supervisor = null!;
    private int _passportId;
    private int _genderId;
    private int _next;

    public PartyProfileTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _host = await ApiTestHost.CreateAsync(_fixture, "profile");
        await _host.CreateStaffAsync("FIELD95", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.FieldOfficer));
        await _host.CreateStaffAsync("SUP95", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.Supervisor));
        _officer = _host.Client(await _host.TokenAsync("FIELD95"));
        _supervisor = _host.Client(await _host.TokenAsync("SUP95"));

        using var types = await _officer.GetAsync(new Uri("/api/v1/lookups/identification-document-types", UriKind.Relative));
        _passportId = (await ApiTestHost.ReadJsonAsync(types)).EnumerateArray()
            .Single(row => row.GetProperty("code").GetString() == "PASSPORT").GetProperty("id").GetInt32();
        using var genders = await _officer.GetAsync(new Uri("/api/v1/lookups/genders", UriKind.Relative));
        _genderId = (await ApiTestHost.ReadJsonAsync(genders)).EnumerateArray().First().GetProperty("id").GetInt32();
    }

    public async Task DisposeAsync()
    {
        _officer.Dispose();
        _supervisor.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task Next_of_kin_can_be_added_changed_and_removed()
    {
        var partyId = await IndividualAsync();
        var url = $"/api/v1/parties/{partyId}/next-of-kin";

        using var added = await _officer.PostAsJsonAsync(url, new { fullName = "Akosua Mensah", relationship = "Sister", mobileNumber = "024 555 0000" });
        added.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await ApiTestHost.ReadJsonAsync(added)).GetProperty("id").GetInt64();

        (await _officer.PostAsJsonAsync(url, new { fullName = "", mobileNumber = "123" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await _officer.PutAsJsonAsync($"{url}/{id}", new { fullName = "Akosua Mensah-Owusu", relationship = "Sister" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var list = await _officer.GetAsync(new Uri(url, UriKind.Relative));
        var row = (await ApiTestHost.ReadJsonAsync(list)).EnumerateArray().Single();
        row.GetProperty("fullName").GetString().ShouldBe("Akosua Mensah-Owusu");
        row.GetProperty("mobileNumber").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);

        (await _officer.DeleteAsync(new Uri($"{url}/{id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await _officer.DeleteAsync(new Uri($"{url}/{id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Regulatory_attributes_keep_every_version_and_read_the_newest()
    {
        var partyId = await IndividualAsync();
        var url = $"/api/v1/parties/{partyId}/regulatory-attributes";

        (await _officer.PutAsJsonAsync($"{url}/risk_rating", new { attributeValue = "LOW" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await _officer.PutAsJsonAsync($"{url}/RISK_RATING", new { attributeValue = "HIGH" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await _officer.PutAsJsonAsync($"{url}/RISK_RATING", new { attributeValue = "HIGH" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await _officer.PutAsJsonAsync($"{url}/SECTOR", new { attributeValue = "Agriculture" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await _officer.PutAsJsonAsync($"{url}/RISK-RATING", new { attributeValue = "X" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var current = await _officer.GetAsync(new Uri(url, UriKind.Relative));
        var rows = (await ApiTestHost.ReadJsonAsync(current)).EnumerateArray().ToList();
        rows.Select(item => item.GetProperty("attributeCode").GetString()).ShouldBe(new[] { "RISK_RATING", "SECTOR" });
        rows[0].GetProperty("attributeValue").GetString().ShouldBe("HIGH");

        using var history = await _officer.GetAsync(new Uri($"{url}/RISK_RATING/history", UriKind.Relative));
        (await ApiTestHost.ReadJsonAsync(history)).EnumerateArray().Select(item => item.GetProperty("attributeValue").GetString())
            .ShouldBe(new[] { "HIGH", "LOW" });

        await using var context = _host.NewContext();
        (await context.Set<PartyRegulatoryAttribute>().CountAsync(item => item.PartyId == partyId)).ShouldBe(3);
    }

    [Fact]
    public async Task Credit_officers_are_designated_once_and_only_active_ones_can_be_assigned()
    {
        var staffId = await _host.CreateStaffAsync("LOANS95", canSwitchBranch: false);
        var partyId = await IndividualAsync();

        (await _officer.PostAsJsonAsync("/api/v1/credit-officers", new { staffUserId = staffId })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var designated = await _supervisor.PostAsJsonAsync("/api/v1/credit-officers", new { staffUserId = staffId });
        designated.StatusCode.ShouldBe(HttpStatusCode.Created);
        var officerId = (await ApiTestHost.ReadJsonAsync(designated)).GetProperty("id").GetInt64();

        using var again = await _supervisor.PostAsJsonAsync("/api/v1/credit-officers", new { staffUserId = staffId });
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApiTestHost.ProblemTypeAsync(again)).ShouldBe(ProblemCodes.CreditOfficerExists);

        using var list = await _officer.GetAsync(new Uri("/api/v1/credit-officers", UriKind.Relative));
        (await ApiTestHost.ReadJsonAsync(list)).EnumerateArray().Single().GetProperty("staffCode").GetString().ShouldBe("LOANS95");

        (await _officer.PutAsJsonAsync($"/api/v1/parties/{partyId}/credit-officer", new { creditOfficerId = officerId })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await using (var context = _host.NewContext())
        {
            (await context.Set<Party>().Where(row => row.PartyId == partyId).Select(row => row.CreditOfficerId).SingleAsync()).ShouldBe(officerId);
        }

        (await _supervisor.PutAsJsonAsync($"/api/v1/credit-officers/{officerId}", new { isDisabled = true })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await _officer.PutAsJsonAsync($"/api/v1/parties/{partyId}/credit-officer", new { creditOfficerId = officerId })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await _officer.PutAsJsonAsync($"/api/v1/parties/{partyId}/credit-officer", new { creditOfficerId = (long?)null })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Re-designating after a disable works; re-enabling the old row would then clash.
        using var redesignated = await _supervisor.PostAsJsonAsync("/api/v1/credit-officers", new { staffUserId = staffId });
        redesignated.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var reenable = await _supervisor.PutAsJsonAsync($"/api/v1/credit-officers/{officerId}", new { isDisabled = false });
        (await ApiTestHost.ProblemTypeAsync(reenable)).ShouldBe(ProblemCodes.CreditOfficerExists);
    }

    private async Task<long> IndividualAsync()
    {
        var id = _next++;
        using var response = await _officer.PostAsJsonAsync("/api/v1/parties/individuals", new
        {
            firstName = "Profile",
            lastName = "Person" + id,
            dateOfBirth = "1991-01-01",
            genderId = _genderId,
            nationality = "Ghanaian",
            mobile = "0240000000",
            address1 = "Takoradi",
            identificationTypeId = _passportId,
            identificationNumber = "PRF" + id.ToString("D6", System.Globalization.CultureInfo.InvariantCulture)
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await ApiTestHost.ReadJsonAsync(response)).GetProperty("partyId").GetInt64();
    }
}
