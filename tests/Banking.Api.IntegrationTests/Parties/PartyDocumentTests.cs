using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Application.Errors;
using Banking.Domain.Parties;
using Banking.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Banking.Api.IntegrationTests.Parties;

/// <summary>A4-04: documents, images, the photo, upload checks and idempotent retries.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class PartyDocumentTests : IAsyncLifetime
{
    private static readonly Dictionary<string, string?> FourEyes = new(StringComparer.Ordinal)
    {
        ["Supervision:AllowSelfApproval"] = "false"
    };

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R', 0, 0, 0, 10, 0, 0, 0, 10, 8, 2, 0, 0, 0];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00, 0x01];
    private static readonly byte[] Pdf = "%PDF-1.7\n%test document\n"u8.ToArray();
    private static readonly byte[] Gif = "GIF89a....."u8.ToArray();

    private readonly MsSqlContainerFixture _fixture;
    private ApiTestHost _host = null!;
    private HttpClient _officer = null!;
    private HttpClient _kyc = null!;
    private Dictionary<string, int> _idTypes = null!;
    private int _femaleId;
    private int _next;

    public PartyDocumentTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _host = await ApiTestHost.CreateAsync(_fixture, "documents", FourEyes);
        await _host.CreateStaffAsync("FIELD81", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.FieldOfficer));
        await _host.CreateStaffAsync("KYC81", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.KycOfficer));
        _officer = _host.Client(await _host.TokenAsync("FIELD81"));
        _kyc = _host.Client(await _host.TokenAsync("KYC81"));

        using var types = await _officer.GetAsync(new Uri("/api/v1/lookups/identification-document-types", UriKind.Relative));
        _idTypes = (await ApiTestHost.ReadJsonAsync(types)).EnumerateArray()
            .ToDictionary(row => row.GetProperty("code").GetString()!, row => row.GetProperty("id").GetInt32(), StringComparer.Ordinal);
        using var genders = await _officer.GetAsync(new Uri("/api/v1/lookups/genders", UriKind.Relative));
        _femaleId = (await ApiTestHost.ReadJsonAsync(genders)).EnumerateArray().First().GetProperty("id").GetInt32();
    }

    public async Task DisposeAsync()
    {
        _officer.Dispose();
        _kyc.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task Lists_mask_numbers_and_the_primary_ID_cannot_be_removed()
    {
        var partyId = await IndividualAsync("GHA-700000001-7");

        using var added = await _officer.PostAsJsonAsync($"/api/v1/parties/{partyId}/documents", new { documentTypeId = _idTypes["PASSPORT"], documentNumber = "g1234567", issuedOn = "2022-01-01", expiresOn = "2032-01-01" });
        added.StatusCode.ShouldBe(HttpStatusCode.Created);
        var passportId = (await ApiTestHost.ReadJsonAsync(added)).GetProperty("id").GetInt64();

        using var list = await _officer.GetAsync(new Uri($"/api/v1/parties/{partyId}/documents", UriKind.Relative));
        var listJson = await list.Content.ReadAsStringAsync();
        listJson.ShouldNotContain("GHA-700000001-7");
        listJson.ShouldNotContain("imageValue", Case.Insensitive);
        var rows = (await ApiTestHost.ReadJsonAsync(list)).EnumerateArray().ToList();
        rows.Count.ShouldBe(2);
        rows.Single(row => row.GetProperty("isPrimary").GetBoolean()).GetProperty("documentNumberMasked").GetString().ShouldBe("****01-7");

        using var detail = await _officer.GetAsync(new Uri($"/api/v1/parties/{partyId}/documents/{passportId}", UriKind.Relative));
        (await ApiTestHost.ReadJsonAsync(detail)).GetProperty("documentNumber").GetString().ShouldBe("G1234567");

        var otherId = await IndividualAsync("GHA-700000002-7");
        using var taken = await _officer.PostAsJsonAsync($"/api/v1/parties/{otherId}/documents", new { documentTypeId = _idTypes["PASSPORT"], documentNumber = "G1234567" });
        taken.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApiTestHost.ProblemTypeAsync(taken)).ShouldBe(ProblemCodes.PartyDuplicateIdentification);

        using var wrongType = await _officer.PostAsJsonAsync($"/api/v1/parties/{partyId}/documents", new { documentTypeId = _idTypes["TIN_CERTIFICATE"], documentNumber = "T1" });
        wrongType.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var primaryId = rows.Single(row => row.GetProperty("isPrimary").GetBoolean()).GetProperty("partyIdentificationDocumentId").GetInt64();
        using var primary = await _officer.DeleteAsync(new Uri($"/api/v1/parties/{partyId}/documents/{primaryId}", UriKind.Relative));
        primary.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApiTestHost.ProblemTypeAsync(primary)).ShouldBe(ProblemCodes.PartyPrimaryDocument);
        (await _officer.DeleteAsync(new Uri($"/api/v1/parties/{partyId}/documents/{passportId}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Uploads_are_checked_by_content_size_and_declared_type()
    {
        var partyId = await IndividualAsync("GHA-700000003-7");
        var documentId = await PrimaryDocumentAsync(partyId);
        var url = $"/api/v1/parties/{partyId}/documents/{documentId}/image";

        (await PutAsync(url, Pdf, "application/pdf")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using (var image = await _officer.GetAsync(new Uri(url, UriKind.Relative)))
        {
            image.StatusCode.ShouldBe(HttpStatusCode.OK);
            image.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
            (await image.Content.ReadAsByteArrayAsync()).ShouldBe(Pdf);
        }

        await ExpectAsync(await PutAsync(url, Pdf, "image/png"), ProblemCodes.UploadTypeMismatch);
        await ExpectAsync(await PutAsync(url, Gif, "image/jpeg"), ProblemCodes.UploadTypeNotAllowed);
        await ExpectAsync(await PutAsync(url, "hello"u8.ToArray(), "text/plain"), ProblemCodes.UploadTypeNotAllowed);
        await ExpectAsync(await PutAsync(url, [], "image/png"), ProblemCodes.UploadEmpty);

        var tooLarge = new byte[(5 * 1024 * 1024) + 1];
        Png.CopyTo(tooLarge, 0);
        await ExpectAsync(await PutAsync(url, tooLarge, "image/png"), ProblemCodes.UploadTooLarge);

        await using var context = _host.NewContext();
        var audit = await context.Set<DataChangeAuditLog>()
            .Where(row => row.TableName == "PartyIdentificationDocument" && row.ActionType == "UPDATE")
            .Select(row => row.NewValueJson)
            .ToListAsync();
        audit.ShouldNotBeEmpty();
        audit.ShouldAllBe(json => json!.Contains("[binary,", StringComparison.Ordinal) && !json.Contains("JVBERi", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_photo_waits_for_a_different_supervisor_and_a_pending_one_can_be_rejected()
    {
        var partyId = await IndividualAsync("GHA-700000004-7");
        var url = $"/api/v1/parties/{partyId}/photo";

        await ExpectAsync(await PutAsync(url, Pdf, "application/pdf"), ProblemCodes.UploadTypeNotAllowed);
        (await PutAsync(url, Jpeg, "image/jpeg")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await PhotoStateAsync(partyId)).ShouldBe("PENDING");

        (await _officer.PostAsync(new Uri($"{url}/supervise", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await _kyc.PostAsync(new Uri($"{url}/supervise", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await PhotoStateAsync(partyId)).ShouldBe("APPROVED");

        using (var image = await _kyc.GetAsync(new Uri($"{url}/image", UriKind.Relative)))
        {
            image.Content.Headers.ContentType!.MediaType.ShouldBe("image/jpeg");
        }

        // A replacement starts again at Pending and can be rejected, which removes it.
        (await PutAsync(url, Png, "image/png")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await PhotoStateAsync(partyId)).ShouldBe("PENDING");
        (await _kyc.PostAsJsonAsync($"{url}/reject", new { reason = "Blurred" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var after = await _kyc.GetAsync(new Uri(url, UriKind.Relative));
        (await ApiTestHost.ReadJsonAsync(after)).GetProperty("hasPhoto").GetBoolean().ShouldBeFalse();
        using var rejectAgain = await _kyc.PostAsJsonAsync($"{url}/reject", new { });
        rejectAgain.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_retried_request_with_the_same_key_returns_the_first_result()
    {
        var partyId = await IndividualAsync("GHA-700000005-7");
        var body = new { documentTypeId = _idTypes["DRIVERS_LICENCE"], documentNumber = "DL-5555" };

        using var first = await PostWithKeyAsync($"/api/v1/parties/{partyId}/documents", body, "retry-key-1");
        using var second = await PostWithKeyAsync($"/api/v1/parties/{partyId}/documents", body, "retry-key-1");
        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await ApiTestHost.ReadJsonAsync(second)).GetProperty("id").GetInt64()
            .ShouldBe((await ApiTestHost.ReadJsonAsync(first)).GetProperty("id").GetInt64());

        await using (var context = _host.NewContext())
        {
            (await context.Set<PartyIdentificationDocument>().CountAsync(row => row.PartyId == partyId)).ShouldBe(2);
        }

        using var reused = await PostWithKeyAsync($"/api/v1/parties/{partyId}/documents", body with { documentNumber = "DL-6666" }, "retry-key-1");
        reused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApiTestHost.ProblemTypeAsync(reused)).ShouldBe(ProblemCodes.IdempotencyKeyReused);

        // A failed request frees its key, so the corrected retry runs.
        using var failed = await PostWithKeyAsync($"/api/v1/parties/{partyId}/documents", new { documentTypeId = _idTypes["DRIVERS_LICENCE"], documentNumber = "" }, "retry-key-2");
        failed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var corrected = await PostWithKeyAsync($"/api/v1/parties/{partyId}/documents", new { documentTypeId = _idTypes["DRIVERS_LICENCE"], documentNumber = "DL-7777" }, "retry-key-2");
        corrected.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Photo uploads replay too.
        foreach (var attempt in Enumerable.Range(0, 2))
        {
            using var content = new ByteArrayContent(Jpeg);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            using var request = new HttpRequestMessage(HttpMethod.Put, new Uri($"/api/v1/parties/{partyId}/photo", UriKind.Relative)) { Content = content };
            request.Headers.Add("Idempotency-Key", "photo-key-1");
            using var response = await _officer.SendAsync(request);
            response.StatusCode.ShouldBe(HttpStatusCode.NoContent, $"attempt {attempt}");
        }
    }

    private async Task<long> IndividualAsync(string idNumber)
    {
        using var response = await _officer.PostAsJsonAsync("/api/v1/parties/individuals", new
        {
            firstName = "Efua",
            lastName = "Doc" + _next++,
            dateOfBirth = "1988-08-08",
            genderId = _femaleId,
            nationality = "Ghanaian",
            mobile = "0551234567",
            address1 = "Cape Coast",
            identificationTypeId = _idTypes["GHANA_CARD"],
            identificationNumber = idNumber
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await ApiTestHost.ReadJsonAsync(response)).GetProperty("partyId").GetInt64();
    }

    private async Task<long> PrimaryDocumentAsync(long partyId)
    {
        using var list = await _officer.GetAsync(new Uri($"/api/v1/parties/{partyId}/documents", UriKind.Relative));
        return (await ApiTestHost.ReadJsonAsync(list)).EnumerateArray().Single().GetProperty("partyIdentificationDocumentId").GetInt64();
    }

    private async Task<string?> PhotoStateAsync(long partyId)
    {
        using var response = await _officer.GetAsync(new Uri($"/api/v1/parties/{partyId}/photo", UriKind.Relative));
        return (await ApiTestHost.ReadJsonAsync(response)).GetProperty("supervisionState").GetString();
    }

    private async Task<HttpResponseMessage> PutAsync(string url, byte[] bytes, string contentType)
    {
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return await _officer.PutAsync(new Uri(url, UriKind.Relative), content);
    }

    private async Task<HttpResponseMessage> PostWithKeyAsync<T>(string url, T body, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(url, UriKind.Relative)) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key);
        return await _officer.SendAsync(request);
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
