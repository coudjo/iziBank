using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Application.Errors;
using Banking.Domain.Security;
using Banking.Domain.Supervision;
using Microsoft.EntityFrameworkCore;

namespace Banking.Api.IntegrationTests.Parties;

/// <summary>A4-09 and A4-11: specimen signatures, four-eyes approval, the effective specimen, PNG checks.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class PartySignatureTests : IAsyncLifetime
{
    private static readonly Dictionary<string, string?> FourEyes = new(StringComparer.Ordinal)
    {
        ["Supervision:AllowSelfApproval"] = "false"
    };

    private readonly MsSqlContainerFixture _fixture;
    private ApiTestHost _host = null!;
    private HttpClient _officer = null!;
    private HttpClient _kyc = null!;
    private Dictionary<string, int> _idTypes = null!;
    private int _genderId;
    private int _next;

    public PartySignatureTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _host = await ApiTestHost.CreateAsync(_fixture, "signatures", FourEyes);
        await _host.CreateStaffAsync("FIELD91", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.FieldOfficer));
        await _host.CreateStaffAsync("KYC91", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.KycOfficer));
        _officer = _host.Client(await _host.TokenAsync("FIELD91"));
        _kyc = _host.Client(await _host.TokenAsync("KYC91"));

        using var types = await _officer.GetAsync(new Uri("/api/v1/lookups/identification-document-types", UriKind.Relative));
        _idTypes = (await ApiTestHost.ReadJsonAsync(types)).EnumerateArray()
            .ToDictionary(row => row.GetProperty("code").GetString()!, row => row.GetProperty("id").GetInt32(), StringComparer.Ordinal);
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
    public async Task A_capture_becomes_effective_only_when_a_different_supervisor_approves_it()
    {
        var partyId = await IndividualAsync();
        var first = Png(400, 150, marker: 1);

        (await CaptureAsync(_officer, partyId, first)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var status = await StatusAsync(partyId);
        status.GetProperty("hasEffective").GetBoolean().ShouldBeFalse();
        status.GetProperty("pending").ValueKind.ShouldBe(JsonValueKind.Object);
        (await _officer.GetAsync(new Uri($"/api/v1/parties/{partyId}/signature/image", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The officer cannot approve (no supervise right); the KYC officer can.
        (await _officer.PostAsync(new Uri($"/api/v1/parties/{partyId}/signature/supervise", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await _kyc.PostAsync(new Uri($"/api/v1/parties/{partyId}/signature/supervise", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await StatusAsync(partyId)).GetProperty("hasEffective").GetBoolean().ShouldBeTrue();

        // A recapture waits; the approved specimen stays effective until it is approved.
        var second = Png(400, 150, marker: 2);
        (await CaptureAsync(_officer, partyId, second)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ImageAsync(partyId, pending: false)).ShouldBe(first);
        (await ImageAsync(partyId, pending: true)).ShouldBe(second);

        // Rejecting the recapture keeps the first specimen.
        (await _kyc.PostAsJsonAsync($"/api/v1/parties/{partyId}/signature/reject", new { reason = "Smudged" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var after = await StatusAsync(partyId);
        after.GetProperty("pending").ValueKind.ShouldBe(JsonValueKind.Null);
        (await ImageAsync(partyId, pending: false)).ShouldBe(first);

        using var nothing = await _kyc.PostAsync(new Uri($"/api/v1/parties/{partyId}/signature/supervise", UriKind.Relative), null);
        (await ApiTestHost.ProblemTypeAsync(nothing)).ShouldBe(SupervisionProblems.NotPending);
    }

    [Fact]
    public async Task The_person_who_captured_cannot_approve_and_signatures_do_not_touch_the_customer_record()
    {
        await _host.CreateStaffAsync("SUP91", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.Supervisor));
        using var supervisor = _host.Client(await _host.TokenAsync("SUP91"));
        var partyId = await IndividualAsync();

        await using (var context = _host.NewContext())
        {
            var party = await context.Set<Banking.Domain.Parties.Party>().SingleAsync(row => row.PartyId == partyId);
            party.SupervisedByUserId.ShouldBeNull();
        }

        // The supervisor captures (has PTY edit via the whole module) and may not approve their own capture.
        (await CaptureAsync(supervisor, partyId, Png(300, 100, marker: 3))).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var self = await supervisor.PostAsync(new Uri($"/api/v1/parties/{partyId}/signature/supervise", UriKind.Relative), null);
        self.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApiTestHost.ProblemTypeAsync(self)).ShouldBe(SupervisionProblems.SelfApproval);

        using var party2 = await _kyc.GetAsync(new Uri($"/api/v1/parties/{partyId}", UriKind.Relative));
        (await ApiTestHost.ReadJsonAsync(party2)).GetProperty("kycStatusCode").GetString().ShouldBe("PENDING");
    }

    [Fact]
    public async Task Only_small_PNGs_within_2000_by_1000_are_accepted_and_bytes_never_reach_the_audit()
    {
        var partyId = await IndividualAsync();

        await ExpectAsync(await CaptureAsync(_officer, partyId, [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3], "image/jpeg"), ProblemCodes.UploadTypeNotAllowed);
        await ExpectAsync(await CaptureAsync(_officer, partyId, Png(2001, 500, marker: 4)), ProblemCodes.UploadDimensions);
        await ExpectAsync(await CaptureAsync(_officer, partyId, Png(500, 1001, marker: 5)), ProblemCodes.UploadDimensions);

        var oversized = new byte[(1024 * 1024) + 1];
        Png(100, 100, marker: 6).CopyTo(oversized, 0);
        await ExpectAsync(await CaptureAsync(_officer, partyId, oversized), ProblemCodes.UploadTooLarge);

        (await CaptureAsync(_officer, partyId, Png(2000, 1000, marker: 7))).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var context = _host.NewContext();
        var audit = await context.Set<DataChangeAuditLog>()
            .Where(row => row.TableName == "PartySignature")
            .Select(row => row.NewValueJson)
            .ToListAsync();
        audit.ShouldNotBeEmpty();
        audit.ShouldAllBe(json => json!.Contains("[binary,", StringComparison.Ordinal));

        // Businesses do not sign.
        using var business = await _officer.PostAsJsonAsync("/api/v1/parties/businesses", new
        {
            businessName = "No Signature Ltd",
            registrationNumber = "CS-SIG-1",
            businessCategoryId = (await LookupAsync("business-categories"))["LIMITED_COMPANY"],
            mobile = "0302000000",
            address1 = "Accra",
            sourceOfFunds = "Sales"
        });
        var businessId = (await ApiTestHost.ReadJsonAsync(business)).GetProperty("partyId").GetInt64();
        (await CaptureAsync(_officer, businessId, Png(100, 50, marker: 8))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>A minimal PNG header with the given size; <paramref name="marker"/> makes each image distinct.</summary>
    private static byte[] Png(int width, int height, byte marker) =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R',
        (byte)(width >> 24), (byte)(width >> 16), (byte)(width >> 8), (byte)width,
        (byte)(height >> 24), (byte)(height >> 16), (byte)(height >> 8), (byte)height,
        8, 2, 0, 0, 0, marker
    ];

    private static async Task<HttpResponseMessage> CaptureAsync(HttpClient client, long partyId, byte[] bytes, string contentType = "image/png")
    {
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return await client.PutAsync(new Uri($"/api/v1/parties/{partyId}/signature", UriKind.Relative), content);
    }

    private async Task<JsonElement> StatusAsync(long partyId)
    {
        using var response = await _officer.GetAsync(new Uri($"/api/v1/parties/{partyId}/signature", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await ApiTestHost.ReadJsonAsync(response);
    }

    private async Task<byte[]> ImageAsync(long partyId, bool pending)
    {
        using var response = await _kyc.GetAsync(new Uri($"/api/v1/parties/{partyId}/signature/image?pending={(pending ? "true" : "false")}", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
        return await response.Content.ReadAsByteArrayAsync();
    }

    private async Task<long> IndividualAsync()
    {
        var id = _next++;
        using var response = await _officer.PostAsJsonAsync("/api/v1/parties/individuals", new
        {
            firstName = "Signer",
            lastName = "Number" + id,
            dateOfBirth = "1979-09-09",
            genderId = _genderId,
            nationality = "Ghanaian",
            mobile = "0270000000",
            address1 = "Ho",
            identificationTypeId = _idTypes["PASSPORT"],
            identificationNumber = "SIG" + id.ToString("D6", System.Globalization.CultureInfo.InvariantCulture)
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await ApiTestHost.ReadJsonAsync(response)).GetProperty("partyId").GetInt64();
    }

    private async Task<Dictionary<string, int>> LookupAsync(string type)
    {
        using var response = await _officer.GetAsync(new Uri($"/api/v1/lookups/{type}", UriKind.Relative));
        return (await ApiTestHost.ReadJsonAsync(response)).EnumerateArray()
            .ToDictionary(row => row.GetProperty("code").GetString()!, row => row.GetProperty("id").GetInt32(), StringComparer.Ordinal);
    }

    private static async Task ExpectAsync(HttpResponseMessage response, string problemCode)
    {
        using (response)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await ApiTestHost.ProblemTypeAsync(response)).ShouldBe(problemCode);
        }
    }
}
