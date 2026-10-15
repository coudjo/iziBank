using System.Net;
using System.Net.Http.Json;
using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Application.Errors;
using Banking.Domain.Parties;
using Banking.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Banking.Api.IntegrationTests.Parties;

/// <summary>A4-01: party numbers, display name, create individual, duplicate identification.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class CreateIndividualTests : IAsyncLifetime
{
    private readonly MsSqlContainerFixture _fixture;
    private ApiTestHost _host = null!;
    private HttpClient _teller = null!;
    private int _femaleId;
    private Dictionary<string, int> _idTypes = null!;

    public CreateIndividualTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _host = await ApiTestHost.CreateAsync(_fixture, "individuals");
        await _host.CreateStaffAsync("TELLER61", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.Teller));
        _teller = _host.Client(await _host.TokenAsync("TELLER61"));

        _femaleId = (await LookupAsync("genders"))["FEMALE"];
        _idTypes = await LookupAsync("identification-document-types");
    }

    public async Task DisposeAsync()
    {
        _teller.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task A_new_individual_gets_a_number_and_display_name_and_waits_for_KYC()
    {
        using var created = await _teller.PostAsJsonAsync("/api/v1/parties/individuals", Individual("GHA-100000001-1", firstName: " Ama ", lastName: "Mensah"));
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await ApiTestHost.ReadJsonAsync(created);
        var partyId = body.GetProperty("partyId").GetInt64();
        var number = body.GetProperty("partyNumber").GetString()!;
        number.Length.ShouldBe(8);
        number.All(char.IsAsciiDigit).ShouldBeTrue();

        using var read = await _teller.GetAsync(new Uri($"/api/v1/parties/{partyId}", UriKind.Relative));
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        var party = await ApiTestHost.ReadJsonAsync(read);
        party.GetProperty("displayName").GetString().ShouldBe("Ama Mensah");
        party.GetProperty("partyTypeCode").GetString().ShouldBe("INDIVIDUAL");
        party.GetProperty("kycStatusCode").GetString().ShouldBe("PENDING");
        party.GetProperty("supervisionState").GetString().ShouldBe("PENDING");
        party.GetProperty("mobile").GetString().ShouldBe("0241234567");
        party.GetProperty("individual").GetProperty("identificationNumber").GetString().ShouldBe("GHA-100000001-1");

        await using var context = _host.NewContext();
        var document = await context.Set<PartyIdentificationDocument>().SingleAsync(row => row.PartyId == partyId);
        document.DocumentNumber.ShouldBe("GHA-100000001-1");
        document.DocumentTypeId.ShouldBe(_idTypes["GHANA_CARD"]);
    }

    [Fact]
    public async Task The_same_identification_returns_the_existing_customer_number()
    {
        using var first = await _teller.PostAsJsonAsync("/api/v1/parties/individuals", Individual("GHA-200000002-2"));
        var number = (await ApiTestHost.ReadJsonAsync(first)).GetProperty("partyNumber").GetString();

        using var again = await _teller.PostAsJsonAsync("/api/v1/parties/individuals", Individual(" gha-200000002-2 ", firstName: "Someone", lastName: "Else"));
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await ApiTestHost.ReadJsonAsync(again);
        problem.GetProperty("type").GetString().ShouldBe(ProblemCodes.PartyDuplicateIdentification);
        problem.GetProperty("existingPartyNumber").GetString().ShouldBe(number);
    }

    [Fact]
    public async Task Age_document_type_expiry_and_contact_rules_are_enforced()
    {
        var tooYoung = DateTime.UtcNow.AddYears(-17).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        using var response = await _teller.PostAsJsonAsync("/api/v1/parties/individuals", Individual(
            "C-0001",
            dateOfBirth: tooYoung,
            idType: "CERTIFICATE_OF_INCORPORATION",
            expiresOn: "2020-01-01",
            mobile: "12345"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var errors = (await ApiTestHost.ReadJsonAsync(response)).GetProperty("errors");
        foreach (var field in new[] { "dateOfBirth", "identificationTypeId", "identificationExpiresOn", "mobile" })
        {
            errors.TryGetProperty(field, out _).ShouldBeTrue(field);
        }
    }

    [Fact]
    public async Task Concurrent_creates_never_share_a_number_and_a_duplicate_race_has_one_winner()
    {
        var distinct = await Task.WhenAll(Enumerable.Range(0, 20).Select(index =>
            _teller.PostAsJsonAsync("/api/v1/parties/individuals", Individual($"GHA-3{index:D8}-3"))));
        try
        {
            distinct.ShouldAllBe(response => response.StatusCode == HttpStatusCode.Created);
            var numbers = await Task.WhenAll(distinct.Select(async response =>
                (await ApiTestHost.ReadJsonAsync(response)).GetProperty("partyNumber").GetString()));
            numbers.Distinct(StringComparer.Ordinal).Count().ShouldBe(20);
        }
        finally
        {
            Array.ForEach(distinct, response => response.Dispose());
        }

        var race = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            _teller.PostAsJsonAsync("/api/v1/parties/individuals", Individual("GHA-400000004-4"))));
        try
        {
            race.Count(response => response.StatusCode == HttpStatusCode.Created).ShouldBe(1);
            race.Count(response => response.StatusCode == HttpStatusCode.Conflict).ShouldBe(7);

            var winner = (await ApiTestHost.ReadJsonAsync(race.Single(response => response.StatusCode == HttpStatusCode.Created)))
                .GetProperty("partyNumber").GetString();
            foreach (var loser in race.Where(response => response.StatusCode == HttpStatusCode.Conflict))
            {
                (await ApiTestHost.ReadJsonAsync(loser)).GetProperty("existingPartyNumber").GetString().ShouldBe(winner);
            }
        }
        finally
        {
            Array.ForEach(race, response => response.Dispose());
        }
    }

    [Fact]
    public async Task Staff_without_party_rights_cannot_create_or_read_customers()
    {
        await _host.CreateStaffAsync("AUDITOR61", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.Auditor));
        using var auditor = _host.Client(await _host.TokenAsync("AUDITOR61"));

        (await auditor.PostAsJsonAsync("/api/v1/parties/individuals", Individual("GHA-500000005-5"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await auditor.GetAsync(new Uri("/api/v1/parties/1", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private object Individual(
        string idNumber,
        string firstName = "Kwame",
        string lastName = "Asante",
        string dateOfBirth = "1990-04-12",
        string idType = "GHANA_CARD",
        string? expiresOn = "2031-01-01",
        string mobile = "024 123 4567") =>
        new
        {
            title = "Mr",
            firstName,
            lastName,
            dateOfBirth,
            genderId = _femaleId,
            nationality = "Ghanaian",
            mobile,
            address1 = "House 4, Adum, Kumasi",
            identificationTypeId = _idTypes[idType],
            identificationNumber = idNumber,
            identificationIssuedOn = "2021-01-01",
            identificationExpiresOn = expiresOn,
            sourceOfFunds = "Trading",
            occupationOrSector = "Trader"
        };

    private async Task<Dictionary<string, int>> LookupAsync(string type)
    {
        using var response = await _teller.GetAsync(new Uri($"/api/v1/lookups/{type}", UriKind.Relative));
        return (await ApiTestHost.ReadJsonAsync(response)).EnumerateArray()
            .ToDictionary(row => row.GetProperty("code").GetString()!, row => row.GetProperty("id").GetInt32(), StringComparer.Ordinal);
    }
}
