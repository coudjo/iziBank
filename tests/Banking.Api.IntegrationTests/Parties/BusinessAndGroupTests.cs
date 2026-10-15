using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Application.Errors;
using Banking.Domain.Lookups;
using Banking.Domain.Parties;
using Banking.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Banking.Api.IntegrationTests.Parties;

/// <summary>A4-02 businesses and beneficial owners; A4-03 groups, members and the chairperson.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class BusinessAndGroupTests : IAsyncLifetime
{
    private readonly MsSqlContainerFixture _fixture;
    private ApiTestHost _host = null!;
    private HttpClient _officer = null!;
    private Dictionary<string, int> _idTypes = null!;
    private Dictionary<string, int> _roles = null!;
    private int _limitedCompany;
    private int _femaleId;
    private int _nextId;

    public BusinessAndGroupTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _host = await ApiTestHost.CreateAsync(_fixture, "businessgroups");
        await _host.CreateStaffAsync("FIELD71", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.FieldOfficer));
        _officer = _host.Client(await _host.TokenAsync("FIELD71"));

        _idTypes = await LookupAsync("identification-document-types");
        _roles = await LookupAsync("group-member-roles");
        _limitedCompany = (await LookupAsync("business-categories"))["LIMITED_COMPANY"];
        _femaleId = (await LookupAsync("genders"))["FEMALE"];
    }

    public async Task DisposeAsync()
    {
        _officer.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task A_business_is_created_with_its_owners_and_registration_numbers_are_unique()
    {
        var (owner1, _) = await IndividualAsync();
        var (owner2, _) = await IndividualAsync();

        using var created = await _officer.PostAsJsonAsync("/api/v1/parties/businesses", Business("CS-100200", [(owner1, 60m), (owner2, 40m)]));
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await ApiTestHost.ReadJsonAsync(created);
        var partyId = body.GetProperty("partyId").GetInt64();
        var number = body.GetProperty("partyNumber").GetString();

        var party = await GetPartyAsync(partyId);
        party.GetProperty("displayName").GetString().ShouldBe("Adom Traders Ltd");
        party.GetProperty("partyTypeCode").GetString().ShouldBe("BUSINESS");
        var business = party.GetProperty("business");
        business.GetProperty("registrationNumber").GetString().ShouldBe("CS-100200");
        business.GetProperty("beneficialOwners").GetArrayLength().ShouldBe(2);
        business.GetProperty("beneficialOwners")[0].GetProperty("ownershipPercentage").GetDecimal().ShouldBe(60m);

        using var duplicate = await _officer.PostAsJsonAsync("/api/v1/parties/businesses", Business(" cs-100200 ", []));
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await ApiTestHost.ReadJsonAsync(duplicate);
        problem.GetProperty("type").GetString().ShouldBe(ProblemCodes.PartyDuplicateRegistration);
        problem.GetProperty("existingPartyNumber").GetString().ShouldBe(number);
    }

    [Fact]
    public async Task Owners_must_be_individuals_with_valid_shares_totalling_at_most_100()
    {
        var (person, _) = await IndividualAsync();
        var (otherPerson, _) = await IndividualAsync();
        using var firstBusiness = await _officer.PostAsJsonAsync("/api/v1/parties/businesses", Business("CS-200300", []));
        var businessId = (await ApiTestHost.ReadJsonAsync(firstBusiness)).GetProperty("partyId").GetInt64();

        foreach (var owners in new[]
                 {
                     new[] { (businessId, 50m) },
                     new[] { (person, 70m), (otherPerson, 40m) },
                     new[] { (person, 0m) },
                     new[] { (person, 10.123m) },
                     new[] { (person, 20m), (person, 30m) }
                 })
        {
            using var refused = await _officer.PostAsJsonAsync("/api/v1/parties/businesses", Business("CS-9" + _nextId++, owners));
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await ApiTestHost.ReadJsonAsync(refused)).GetProperty("errors").TryGetProperty("beneficialOwners", out _).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Replacing_owners_sends_KYC_back_to_pending()
    {
        var (owner1, _) = await IndividualAsync();
        var (owner2, _) = await IndividualAsync();
        using var created = await _officer.PostAsJsonAsync("/api/v1/parties/businesses", Business("CS-300400", [(owner1, 100m)]));
        var businessId = (await ApiTestHost.ReadJsonAsync(created)).GetProperty("partyId").GetInt64();
        await SetKycAsync(businessId, LookupCodes.KycStatuses.Verified);

        using var replaced = await _officer.PutAsJsonAsync($"/api/v1/parties/{businessId}/beneficial-owners", new
        {
            beneficialOwners = new[] { new { ownerPartyId = owner1, ownershipPercentage = 50m }, new { ownerPartyId = owner2, ownershipPercentage = 50m } }
        });
        replaced.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var owners = await _officer.GetAsync(new Uri($"/api/v1/parties/{businessId}/beneficial-owners", UriKind.Relative));
        (await ApiTestHost.ReadJsonAsync(owners)).EnumerateArray().Select(row => row.GetProperty("ownershipPercentage").GetDecimal()).ToArray().ShouldBe(new[] { 50m, 50m });
        (await GetPartyAsync(businessId)).GetProperty("kycStatusCode").GetString().ShouldBe("PENDING");
    }

    [Fact]
    public async Task A_group_has_one_chairperson_and_members_belong_to_one_group_at_a_time()
    {
        var (chair, _) = await IndividualAsync();
        var (treasurer, _) = await IndividualAsync();
        var (member, memberNumber) = await IndividualAsync();

        using var created = await _officer.PostAsJsonAsync("/api/v1/parties/groups", Group("Nkabom Susu", (chair, "CHAIRPERSON"), (treasurer, "TREASURER"), (member, "MEMBER")));
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await ApiTestHost.ReadJsonAsync(created);
        var groupId = body.GetProperty("partyId").GetInt64();
        var groupNumber = body.GetProperty("partyNumber").GetString();

        var group = (await GetPartyAsync(groupId)).GetProperty("group");
        group.GetProperty("chairpersonPartyId").GetInt64().ShouldBe(chair);
        group.GetProperty("activeMemberCount").GetInt32().ShouldBe(3);

        var (newcomer, _) = await IndividualAsync();
        using var twoChairs = await _officer.PostAsJsonAsync("/api/v1/parties/groups", Group("Two chairs", (newcomer, "CHAIRPERSON"), (member, "CHAIRPERSON")));
        twoChairs.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var elsewhere = await _officer.PostAsJsonAsync("/api/v1/parties/groups", Group("Second group", (newcomer, "CHAIRPERSON"), (member, "MEMBER")));
        elsewhere.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await ApiTestHost.ReadJsonAsync(elsewhere);
        problem.GetProperty("type").GetString().ShouldBe(ProblemCodes.GroupMemberInOtherGroup);
        problem.GetProperty("memberPartyNumber").GetString().ShouldBe(memberNumber);
        problem.GetProperty("groupPartyNumber").GetString().ShouldBe(groupNumber);

        using var secondChair = await _officer.PostAsJsonAsync($"/api/v1/parties/{groupId}/members", new { memberPartyId = newcomer, groupMemberRoleId = _roles["CHAIRPERSON"] });
        secondChair.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApiTestHost.ProblemTypeAsync(secondChair)).ShouldBe(ProblemCodes.GroupChairpersonExists);
    }

    [Fact]
    public async Task Members_leave_with_history_kept_and_the_chair_can_be_handed_over()
    {
        var (chair, _) = await IndividualAsync();
        var (secretary, _) = await IndividualAsync();
        using var created = await _officer.PostAsJsonAsync("/api/v1/parties/groups", Group("Abusua Group", (chair, "CHAIRPERSON"), (secretary, "SECRETARY")));
        var groupId = (await ApiTestHost.ReadJsonAsync(created)).GetProperty("partyId").GetInt64();

        // The chair cannot be promoted twice; the chairperson leaves and the secretary takes over.
        using var blocked = await _officer.PutAsJsonAsync($"/api/v1/parties/{groupId}/members/{secretary}/role", new { groupMemberRoleId = _roles["CHAIRPERSON"] });
        (await ApiTestHost.ProblemTypeAsync(blocked)).ShouldBe(ProblemCodes.GroupChairpersonExists);

        using var left = await _officer.PostAsJsonAsync($"/api/v1/parties/{groupId}/members/{chair}/leave", new { });
        left.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GetPartyAsync(groupId)).GetProperty("group").GetProperty("chairpersonPartyId").ValueKind.ShouldBe(JsonValueKind.Null);

        using var promoted = await _officer.PutAsJsonAsync($"/api/v1/parties/{groupId}/members/{secretary}/role", new { groupMemberRoleId = _roles["CHAIRPERSON"] });
        promoted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var active = await _officer.GetAsync(new Uri($"/api/v1/parties/{groupId}/members", UriKind.Relative));
        var activeRows = (await ApiTestHost.ReadJsonAsync(active)).EnumerateArray().ToList();
        activeRows.Count.ShouldBe(1);
        activeRows[0].GetProperty("isChairperson").GetBoolean().ShouldBeTrue();

        using var all = await _officer.GetAsync(new Uri($"/api/v1/parties/{groupId}/members?includeFormer=true", UriKind.Relative));
        (await ApiTestHost.ReadJsonAsync(all)).GetArrayLength().ShouldBe(2);

        // Having left, the former chairperson may join another group.
        using var rejoin = await _officer.PostAsJsonAsync("/api/v1/parties/groups", Group("New Dawn", (chair, "MEMBER")));
        rejoin.StatusCode.ShouldBe(HttpStatusCode.Created);

        await using var context = _host.NewContext();
        (await context.Set<GroupMember>().CountAsync(row => row.MemberPartyId == chair)).ShouldBe(2);
    }

    [Fact]
    public async Task Members_must_be_individuals_and_tellers_cannot_create_groups()
    {
        using var business = await _officer.PostAsJsonAsync("/api/v1/parties/businesses", Business("CS-400500", []));
        var businessId = (await ApiTestHost.ReadJsonAsync(business)).GetProperty("partyId").GetInt64();

        using var refused = await _officer.PostAsJsonAsync("/api/v1/parties/groups", Group("Mixed", (businessId, "MEMBER")));
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        await _host.CreateStaffAsync("TELLER71", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.Teller));
        using var teller = _host.Client(await _host.TokenAsync("TELLER71"));
        (await teller.PostAsJsonAsync("/api/v1/parties/groups", Group("Not allowed"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private object Business(string registration, IEnumerable<(long Owner, decimal Share)> owners) =>
        new
        {
            businessName = "Adom Traders Ltd",
            registrationNumber = registration,
            taxIdentificationNumber = "C0012345678",
            incorporationDate = "2015-06-01",
            businessCategoryId = _limitedCompany,
            mobile = "0302123456",
            address1 = "12 Liberation Road, Accra",
            sourceOfFunds = "Sales",
            occupationOrSector = "Wholesale",
            beneficialOwners = owners.Select(owner => new { ownerPartyId = owner.Owner, ownershipPercentage = owner.Share }).ToArray()
        };

    private object Group(string name, params (long Member, string Role)[] members) =>
        new
        {
            groupName = name,
            formationDate = "2022-03-01",
            meetingFrequency = "Weekly",
            members = members.Select(member => new { memberPartyId = member.Member, groupMemberRoleId = _roles[member.Role] }).ToArray()
        };

    private async Task<(long PartyId, string PartyNumber)> IndividualAsync()
    {
        var id = _nextId++;
        using var response = await _officer.PostAsJsonAsync("/api/v1/parties/individuals", new
        {
            firstName = "Person",
            lastName = "Number" + id,
            dateOfBirth = "1985-01-01",
            genderId = _femaleId,
            nationality = "Ghanaian",
            mobile = "0201112222",
            address1 = "Tamale",
            identificationTypeId = _idTypes["VOTER_ID"],
            identificationNumber = "V" + id.ToString("D9", System.Globalization.CultureInfo.InvariantCulture)
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await ApiTestHost.ReadJsonAsync(response);
        return (body.GetProperty("partyId").GetInt64(), body.GetProperty("partyNumber").GetString()!);
    }

    private async Task<JsonElement> GetPartyAsync(long partyId)
    {
        using var response = await _officer.GetAsync(new Uri($"/api/v1/parties/{partyId}", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await ApiTestHost.ReadJsonAsync(response);
    }

    private async Task SetKycAsync(long partyId, string code)
    {
        await using var context = _host.NewContext();
        var statusId = await context.Set<KYCStatusType>().Where(row => row.Code == code).Select(row => row.Id).SingleAsync();
        var party = await context.Set<Party>().SingleAsync(row => row.PartyId == partyId);
        party.KYCStatusId = statusId;
        await context.SaveChangesAsync();
    }

    private async Task<Dictionary<string, int>> LookupAsync(string type)
    {
        using var response = await _officer.GetAsync(new Uri($"/api/v1/lookups/{type}", UriKind.Relative));
        return (await ApiTestHost.ReadJsonAsync(response)).EnumerateArray()
            .ToDictionary(row => row.GetProperty("code").GetString()!, row => row.GetProperty("id").GetInt32(), StringComparer.Ordinal);
    }
}
