using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Application.Errors;
using Banking.Domain.Parties;
using Banking.Domain.Security;
using Banking.Domain.Supervision;
using Microsoft.EntityFrameworkCore;

namespace Banking.Api.IntegrationTests.Parties;

/// <summary>A4-06 controlled edits and A4-07 KYC: checklist, verify-and-approve, reject, computed EXPIRED.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class KycTests : IAsyncLifetime
{
    private static readonly Dictionary<string, string?> FourEyes = new(StringComparer.Ordinal)
    {
        ["Supervision:AllowSelfApproval"] = "false"
    };

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00, 0x01];

    private readonly MsSqlContainerFixture _fixture;
    private ApiTestHost _host = null!;
    private HttpClient _officer = null!;
    private HttpClient _kyc = null!;
    private HttpClient _supervisor = null!;
    private Dictionary<string, int> _idTypes = null!;
    private Dictionary<string, int> _roles = null!;
    private int _genderId;
    private int _limitedCompany;
    private int _next;

    public KycTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _host = await ApiTestHost.CreateAsync(_fixture, "kyc", FourEyes);
        await _host.CreateStaffAsync("FIELD97", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.FieldOfficer));
        await _host.CreateStaffAsync("KYC97", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.KycOfficer));
        await _host.CreateStaffAsync("SUP97", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.Supervisor));
        _officer = _host.Client(await _host.TokenAsync("FIELD97"));
        _kyc = _host.Client(await _host.TokenAsync("KYC97"));
        _supervisor = _host.Client(await _host.TokenAsync("SUP97"));

        _idTypes = await LookupAsync("identification-document-types");
        _roles = await LookupAsync("group-member-roles");
        _genderId = (await LookupAsync("genders")).Values.First();
        _limitedCompany = (await LookupAsync("business-categories"))["LIMITED_COMPANY"];
    }

    public async Task DisposeAsync()
    {
        _officer.Dispose();
        _kyc.Dispose();
        _supervisor.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task Verification_needs_a_complete_checklist_and_approves_the_record_in_the_same_step()
    {
        var partyId = await IndividualAsync(_officer);

        var checklist = await ChecklistAsync(partyId);
        checklist.GetProperty("isComplete").GetBoolean().ShouldBeFalse();
        Missing(checklist).ShouldBe(new[] { "PHOTO", "ID_DOCUMENT_IMAGE", "NEXT_OF_KIN" }, ignoreOrder: true);

        using (var early = await _kyc.PostAsync(new Uri($"/api/v1/parties/{partyId}/kyc/verify", UriKind.Relative), null))
        {
            early.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            var problem = await ApiTestHost.ReadJsonAsync(early);
            problem.GetProperty("type").GetString().ShouldBe(ProblemCodes.KycChecklistIncomplete);
            problem.GetProperty("missing").GetArrayLength().ShouldBe(3);
        }

        await CompleteProfileAsync(partyId, _officer);
        (await ChecklistAsync(partyId)).GetProperty("isComplete").GetBoolean().ShouldBeTrue();

        // The field officer has no KYC rights.
        (await _officer.PostAsync(new Uri($"/api/v1/parties/{partyId}/kyc/verify", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await _kyc.PostAsync(new Uri($"/api/v1/parties/{partyId}/kyc/verify", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var party = await PartyAsync(partyId);
        party.GetProperty("kycStatusCode").GetString().ShouldBe("VERIFIED");
        party.GetProperty("effectiveKycStatusCode").GetString().ShouldBe("VERIFIED");
        party.GetProperty("supervisionState").GetString().ShouldBe("APPROVED");

        using var again = await _kyc.PostAsync(new Uri($"/api/v1/parties/{partyId}/kyc/verify", UriKind.Relative), null);
        (await ApiTestHost.ProblemTypeAsync(again)).ShouldBe(ProblemCodes.KycNotPending);
    }

    [Fact]
    public async Task Whoever_created_or_last_changed_the_customer_cannot_verify_them()
    {
        var partyId = await IndividualAsync(_supervisor);
        await CompleteProfileAsync(partyId, _supervisor);

        using var self = await _supervisor.PostAsync(new Uri($"/api/v1/parties/{partyId}/kyc/verify", UriKind.Relative), null);
        self.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApiTestHost.ProblemTypeAsync(self)).ShouldBe(SupervisionProblems.SelfApproval);

        (await _kyc.PostAsync(new Uri($"/api/v1/parties/{partyId}/kyc/verify", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_controlled_edit_resets_KYC_and_approval_but_an_address_change_does_not()
    {
        var partyId = await IndividualAsync(_officer);
        await CompleteProfileAsync(partyId, _officer);
        (await _kyc.PostAsync(new Uri($"/api/v1/parties/{partyId}/kyc/verify", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var number = await IdNumberAsync(partyId);
        (await _officer.PutAsJsonAsync($"/api/v1/parties/{partyId}/individual", Individual(number, lastName: "Mensah", address: "New address, Tema"))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var afterAddress = await PartyAsync(partyId);
        afterAddress.GetProperty("kycStatusCode").GetString().ShouldBe("VERIFIED");
        afterAddress.GetProperty("supervisionState").GetString().ShouldBe("APPROVED");
        afterAddress.GetProperty("address1").GetString().ShouldBe("New address, Tema");

        (await _officer.PutAsJsonAsync($"/api/v1/parties/{partyId}/individual", Individual(number, lastName: "Mensah-Boateng", address: "New address, Tema"))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var afterName = await PartyAsync(partyId);
        afterName.GetProperty("kycStatusCode").GetString().ShouldBe("PENDING");
        afterName.GetProperty("supervisionState").GetString().ShouldBe("PENDING");
        afterName.GetProperty("displayName").GetString().ShouldBe("Abena Mensah-Boateng");
    }

    [Fact]
    public async Task A_verified_customer_reads_as_expired_once_their_ID_expires_and_can_be_rejected_with_a_reason()
    {
        var partyId = await IndividualAsync(_officer);
        await CompleteProfileAsync(partyId, _officer);
        (await _kyc.PostAsync(new Uri($"/api/v1/parties/{partyId}/kyc/verify", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using (var context = _host.NewContext())
        {
            var document = await context.Set<PartyIdentificationDocument>().SingleAsync(row => row.PartyId == partyId);
            document.IssuedOn = new DateOnly(2010, 1, 1);
            document.ExpiresOn = new DateOnly(2020, 1, 1);
            await context.SaveChangesAsync();
        }

        var party = await PartyAsync(partyId);
        party.GetProperty("kycStatusCode").GetString().ShouldBe("VERIFIED");
        party.GetProperty("effectiveKycStatusCode").GetString().ShouldBe("EXPIRED");
        (await ChecklistAsync(partyId)).GetProperty("effectiveStatus").GetString().ShouldBe("EXPIRED");

        (await _kyc.PostAsJsonAsync($"/api/v1/parties/{partyId}/kyc/reject", new { reason = "" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await _kyc.PostAsJsonAsync($"/api/v1/parties/{partyId}/kyc/reject", new { reason = "ID expired, customer to bring a new one" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await PartyAsync(partyId)).GetProperty("kycStatusCode").GetString().ShouldBe("REJECTED");

        using var twice = await _kyc.PostAsJsonAsync($"/api/v1/parties/{partyId}/kyc/reject", new { reason = "Again" });
        (await ApiTestHost.ProblemTypeAsync(twice)).ShouldBe(ProblemCodes.KycNotPending);
    }

    [Fact]
    public async Task Businesses_need_verified_major_owners_and_groups_need_enough_verified_members()
    {
        var owner = await IndividualAsync(_officer);
        using var created = await _officer.PostAsJsonAsync("/api/v1/parties/businesses", new
        {
            businessName = "Kente Weavers Ltd",
            registrationNumber = "CS-KYC-1",
            taxIdentificationNumber = "C000111",
            businessCategoryId = _limitedCompany,
            mobile = "0302555555",
            address1 = "Bonwire",
            sourceOfFunds = "Sales",
            beneficialOwners = new[] { new { ownerPartyId = owner, ownershipPercentage = 60m } }
        });
        var businessId = (await ApiTestHost.ReadJsonAsync(created)).GetProperty("partyId").GetInt64();
        Missing(await ChecklistAsync(businessId)).ShouldBe(new[] { "ID_DOCUMENT_IMAGE", "MAJOR_OWNERS_VERIFIED" }, ignoreOrder: true);

        using var group = await _officer.PostAsJsonAsync("/api/v1/parties/groups", new
        {
            groupName = "Small Group",
            members = new[] { new { memberPartyId = owner, groupMemberRoleId = _roles["CHAIRPERSON"] } }
        });
        var groupId = (await ApiTestHost.ReadJsonAsync(group)).GetProperty("partyId").GetInt64();
        Missing(await ChecklistAsync(groupId)).ShouldBe(new[] { "MINIMUM_MEMBERS", "MEMBERS_VERIFIED" }, ignoreOrder: true);

        (await _officer.PutAsJsonAsync($"/api/v1/parties/{groupId}/group", new { groupName = "Small Savings Group" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await PartyAsync(groupId)).GetProperty("displayName").GetString().ShouldBe("Small Savings Group");
    }

    private static string[] Missing(JsonElement checklist) =>
        checklist.GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("requiredForKyc").GetBoolean() && !item.GetProperty("done").GetBoolean())
            .Select(item => item.GetProperty("code").GetString()!)
            .ToArray();

    private async Task CompleteProfileAsync(long partyId, HttpClient client)
    {
        using (var documents = await client.GetAsync(new Uri($"/api/v1/parties/{partyId}/documents", UriKind.Relative)))
        {
            var documentId = (await ApiTestHost.ReadJsonAsync(documents)).EnumerateArray().Single().GetProperty("partyIdentificationDocumentId").GetInt64();
            (await PutImageAsync(client, $"/api/v1/parties/{partyId}/documents/{documentId}/image")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await PutImageAsync(client, $"/api/v1/parties/{partyId}/photo")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await _kyc.PostAsync(new Uri($"/api/v1/parties/{partyId}/photo/supervise", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PostAsJsonAsync($"/api/v1/parties/{partyId}/next-of-kin", new { fullName = "Yaw Mensah", relationship = "Brother" })).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private static async Task<HttpResponseMessage> PutImageAsync(HttpClient client, string url)
    {
        using var content = new ByteArrayContent(Jpeg);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return await client.PutAsync(new Uri(url, UriKind.Relative), content);
    }

    private object Individual(string idNumber, string lastName = "Mensah", string address = "Kaneshie, Accra") =>
        new
        {
            firstName = "Abena",
            lastName,
            dateOfBirth = "1987-07-07",
            genderId = _genderId,
            nationality = "Ghanaian",
            mobile = "0209998888",
            address1 = address,
            identificationTypeId = _idTypes["GHANA_CARD"],
            identificationNumber = idNumber,
            identificationExpiresOn = "2033-01-01",
            sourceOfFunds = "Salary",
            occupationOrSector = "Teacher"
        };

    private async Task<long> IndividualAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/parties/individuals", Individual("GHA-9" + (_next++).ToString("D8", System.Globalization.CultureInfo.InvariantCulture) + "-9"));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await ApiTestHost.ReadJsonAsync(response)).GetProperty("partyId").GetInt64();
    }

    private async Task<string> IdNumberAsync(long partyId) =>
        (await PartyAsync(partyId)).GetProperty("individual").GetProperty("identificationNumber").GetString()!;

    private async Task<JsonElement> PartyAsync(long partyId)
    {
        using var response = await _kyc.GetAsync(new Uri($"/api/v1/parties/{partyId}", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await ApiTestHost.ReadJsonAsync(response);
    }

    private async Task<JsonElement> ChecklistAsync(long partyId)
    {
        using var response = await _officer.GetAsync(new Uri($"/api/v1/parties/{partyId}/kyc/checklist", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await ApiTestHost.ReadJsonAsync(response);
    }

    private async Task<Dictionary<string, int>> LookupAsync(string type)
    {
        using var response = await _officer.GetAsync(new Uri($"/api/v1/lookups/{type}", UriKind.Relative));
        return (await ApiTestHost.ReadJsonAsync(response)).EnumerateArray()
            .ToDictionary(row => row.GetProperty("code").GetString()!, row => row.GetProperty("id").GetInt32(), StringComparer.Ordinal);
    }
}
