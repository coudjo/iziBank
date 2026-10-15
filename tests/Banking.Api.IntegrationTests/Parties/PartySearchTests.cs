using System.Data;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Domain.Security;
using Banking.Infrastructure.Parties;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Banking.Api.IntegrationTests.Parties;

/// <summary>A4-08: search, eligibility hints, the overview, and index use with 50,000 customers.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class PartySearchTests : IAsyncLifetime
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00, 0x01];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R', 0, 0, 1, 0, 0, 0, 0, 80, 8, 2, 0, 0, 0];

    private readonly MsSqlContainerFixture _fixture;
    private ApiTestHost _host = null!;
    private HttpClient _officer = null!;
    private HttpClient _kyc = null!;
    private int _passportId;
    private int _genderId;

    public PartySearchTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _host = await ApiTestHost.CreateAsync(_fixture, "search");
        await _host.CreateStaffAsync("FIELD99", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.FieldOfficer));
        await _host.CreateStaffAsync("KYC99", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.KycOfficer));
        _officer = _host.Client(await _host.TokenAsync("FIELD99"));
        _kyc = _host.Client(await _host.TokenAsync("KYC99"));

        using var types = await _officer.GetAsync(new Uri("/api/v1/lookups/identification-document-types", UriKind.Relative));
        _passportId = (await ApiTestHost.ReadJsonAsync(types)).EnumerateArray()
            .Single(row => row.GetProperty("code").GetString() == "PASSPORT").GetProperty("id").GetInt32();
        using var genders = await _officer.GetAsync(new Uri("/api/v1/lookups/genders", UriKind.Relative));
        _genderId = (await ApiTestHost.ReadJsonAsync(genders)).EnumerateArray().First().GetProperty("id").GetInt32();
    }

    public async Task DisposeAsync()
    {
        _officer.Dispose();
        _kyc.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task A_term_finds_customers_by_number_name_mobile_and_ID_without_showing_full_IDs()
    {
        var (kwesi, kwesiNumber) = await IndividualAsync("Kwesi", "Appiah", "P5550001", "0245550001");
        var (efua, _) = await IndividualAsync("Efua", "Kwesiwaa", "P5550002", "0245550002");

        (await SearchIdsAsync(kwesiNumber)).ShouldBe([kwesi]);
        (await SearchIdsAsync("Kwesi")).ShouldContain(kwesi);
        (await SearchIdsAsync("0245550002")).ShouldBe([efua]);
        (await SearchIdsAsync("p555 0002")).ShouldBe([efua]);

        // "wesiwaa" is only inside a name: the contains fallback finds it.
        (await SearchIdsAsync("wesiwaa")).ShouldBe([efua]);

        using var response = await _officer.PostAsJsonAsync("/api/v1/parties/search", new { q = "P5550001" });
        var json = await response.Content.ReadAsStringAsync();
        json.ShouldNotContain("P5550001");
        json.ShouldContain("****0001");
    }

    [Fact]
    public async Task Filters_and_eligibility_hints_follow_KYC_approval_and_signature()
    {
        var (partyId, _) = await IndividualAsync("Yaa", "Asantewaa", "P6660001", "0246660001");

        var pending = await RowAsync(partyId);
        pending.GetProperty("effectiveKycStatusCode").GetString().ShouldBe("PENDING");
        pending.GetProperty("canBePrimaryHolder").GetBoolean().ShouldBeFalse();

        // Complete the profile, verify, then capture and approve a signature.
        using (var documents = await _officer.GetAsync(new Uri($"/api/v1/parties/{partyId}/documents", UriKind.Relative)))
        {
            var documentId = (await ApiTestHost.ReadJsonAsync(documents)).EnumerateArray().Single().GetProperty("partyIdentificationDocumentId").GetInt64();
            (await PutAsync($"/api/v1/parties/{partyId}/documents/{documentId}/image", Jpeg, "image/jpeg")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await PutAsync($"/api/v1/parties/{partyId}/photo", Jpeg, "image/jpeg")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await _kyc.PostAsync(new Uri($"/api/v1/parties/{partyId}/photo/supervise", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await _officer.PostAsJsonAsync($"/api/v1/parties/{partyId}/next-of-kin", new { fullName = "Kojo Asante" })).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await _kyc.PostAsync(new Uri($"/api/v1/parties/{partyId}/kyc/verify", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var verified = await RowAsync(partyId);
        verified.GetProperty("canBePrimaryHolder").GetBoolean().ShouldBeTrue();
        verified.GetProperty("canBeSignatory").GetBoolean().ShouldBeFalse();

        (await PutAsync($"/api/v1/parties/{partyId}/signature", Png, "image/png")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _kyc.PostAsync(new Uri($"/api/v1/parties/{partyId}/signature/supervise", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await RowAsync(partyId)).GetProperty("canBeSignatory").GetBoolean().ShouldBeTrue();

        using var approved = await _officer.PostAsJsonAsync("/api/v1/parties/search", new { partyType = "individual", supervision = "APPROVED", kycStatus = "VERIFIED" });
        (await ApiTestHost.ReadJsonAsync(approved)).GetProperty("items").EnumerateArray()
            .ShouldContain(row => row.GetProperty("partyId").GetInt64() == partyId);
        using var groups = await _officer.PostAsJsonAsync("/api/v1/parties/search", new { partyType = "GROUP" });
        (await ApiTestHost.ReadJsonAsync(groups)).GetProperty("items").EnumerateArray()
            .ShouldNotContain(row => row.GetProperty("partyId").GetInt64() == partyId);

        using var overview = await _officer.GetAsync(new Uri($"/api/v1/parties/{partyId}/overview", UriKind.Relative));
        overview.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await ApiTestHost.ReadJsonAsync(overview);
        body.GetProperty("checklist").GetProperty("isComplete").GetBoolean().ShouldBeTrue();
        body.GetProperty("photo").GetProperty("supervisionState").GetString().ShouldBe("APPROVED");
        body.GetProperty("signature").GetProperty("hasEffective").GetBoolean().ShouldBeTrue();
        body.GetProperty("documentCount").GetInt32().ShouldBe(1);
        body.GetProperty("nextOfKinCount").GetInt32().ShouldBe(1);
        body.GetProperty("accounts").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task With_50000_customers_the_indexed_search_only_seeks()
    {
        await using (var context = _host.NewContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                DECLARE @type int = (SELECT [Id] FROM [dbo].[PartyType] WHERE [Code] = 'INDIVIDUAL');
                DECLARE @kyc int = (SELECT [Id] FROM [dbo].[KYCStatusType] WHERE [Code] = 'PENDING');
                INSERT INTO [dbo].[Party] ([PartyNumber], [DisplayName], [PartyTypeId], [KYCStatusId], [IsPoliticallyExposed], [IsSupervisionRequired], [CreatedOn])
                SELECT TOP (50000)
                    CONCAT('G', RIGHT(CONCAT('0000000', n), 7)),
                    CONCAT(N'Generated Person ', n),
                    @type, @kyc, 0, 1, SYSUTCDATETIME()
                FROM (SELECT ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_objects AS a CROSS JOIN sys.all_objects AS b) AS numbers;
                UPDATE STATISTICS [dbo].[Party];
                """);
        }

        using var scope = _host.Factory.Services.CreateScope();
        var search = scope.ServiceProvider.GetRequiredService<PartySearch>();

        foreach (var term in new[] { "G00123", "Generated Person 4999" })
        {
            var query = (await search.IndexedMatchesAsync(search.Parties(), term, CancellationToken.None)).Select(party => party.PartyId);
            var plan = await ActualPlanAsync(query);

            plan.ShouldContain("Index Seek", customMessage: term);
            plan.ShouldNotContain("PhysicalOp=\"Clustered Index Scan\"", customMessage: term);
            plan.ShouldNotContain("PhysicalOp=\"Index Scan\"", customMessage: term);
            plan.ShouldNotContain("PhysicalOp=\"Table Scan\"", customMessage: term);
        }

        (await SearchIdsAsync("G0012345")).Count.ShouldBe(1);
    }

    /// <summary>
    /// Runs EF's own parameterised command with STATISTICS XML on and returns the actual plan. A DECLARE-based
    /// script would compile with unknown values and guess scans, which is not what the API does.
    /// </summary>
    private static async Task<string> ActualPlanAsync(IQueryable<long> query)
    {
        await using var command = query.CreateDbCommand();
        var connection = command.Connection!;
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using (var on = connection.CreateCommand())
        {
            on.CommandText = "SET STATISTICS XML ON;";
            await on.ExecuteNonQueryAsync();
        }

        var plan = new StringBuilder();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            do
            {
                var isPlan = reader.FieldCount == 1 && reader.GetName(0).Contains("Showplan", StringComparison.OrdinalIgnoreCase);
                while (await reader.ReadAsync())
                {
                    if (isPlan)
                    {
                        plan.Append(reader.GetString(0));
                    }
                }
            }
            while (await reader.NextResultAsync());
        }

        await using (var off = connection.CreateCommand())
        {
            off.CommandText = "SET STATISTICS XML OFF;";
            await off.ExecuteNonQueryAsync();
        }

        return plan.ToString();
    }

    private async Task<List<long>> SearchIdsAsync(string term)
    {
        using var response = await _officer.PostAsJsonAsync("/api/v1/parties/search", new { q = term, pageSize = 50 });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await ApiTestHost.ReadJsonAsync(response)).GetProperty("items").EnumerateArray().Select(row => row.GetProperty("partyId").GetInt64()).ToList();
    }

    private async Task<JsonElement> RowAsync(long partyId)
    {
        var number = await PartyNumberAsync(partyId);
        using var response = await _officer.PostAsJsonAsync("/api/v1/parties/search", new { q = number });
        return (await ApiTestHost.ReadJsonAsync(response)).GetProperty("items").EnumerateArray().Single(row => row.GetProperty("partyId").GetInt64() == partyId);
    }

    private async Task<string> PartyNumberAsync(long partyId)
    {
        await using var context = _host.NewContext();
        return await context.Set<Banking.Domain.Parties.Party>().Where(row => row.PartyId == partyId).Select(row => row.PartyNumber).SingleAsync();
    }

    private async Task<HttpResponseMessage> PutAsync(string url, byte[] bytes, string contentType)
    {
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return await _officer.PutAsync(new Uri(url, UriKind.Relative), content);
    }

    private async Task<(long PartyId, string PartyNumber)> IndividualAsync(string first, string last, string passport, string mobile)
    {
        using var response = await _officer.PostAsJsonAsync("/api/v1/parties/individuals", new
        {
            firstName = first,
            lastName = last,
            dateOfBirth = "1983-03-03",
            genderId = _genderId,
            nationality = "Ghanaian",
            mobile,
            address1 = "Sunyani",
            identificationTypeId = _passportId,
            identificationNumber = passport,
            sourceOfFunds = "Farming",
            occupationOrSector = "Farmer"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await ApiTestHost.ReadJsonAsync(response);
        return (body.GetProperty("partyId").GetInt64(), body.GetProperty("partyNumber").GetString()!);
    }
}
