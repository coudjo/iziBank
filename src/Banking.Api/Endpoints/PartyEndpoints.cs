using Banking.Api.Problems;
using Banking.Api.Security;
using Banking.Application.Errors;
using Banking.Domain.Lookups;
using Banking.Domain.Security;
using Banking.Infrastructure.Parties;

namespace Banking.Api.Endpoints;

internal sealed record CreateIndividualRequest(
    string? Title,
    string? FirstName,
    string? LastName,
    DateOnly? DateOfBirth,
    int? GenderId,
    string? Nationality,
    string? Mobile,
    string? Phone,
    string? EmailAddress,
    string? Address1,
    string? Address2,
    int? IdentificationTypeId,
    string? IdentificationNumber,
    DateOnly? IdentificationIssuedOn,
    DateOnly? IdentificationExpiresOn,
    string? SourceOfFunds,
    string? OccupationOrSector,
    bool IsPoliticallyExposed = false);

internal sealed record OwnerShareRequest(long OwnerPartyId, decimal OwnershipPercentage);

internal sealed record DocumentRequest(int? DocumentTypeId, string? DocumentNumber, DateOnly? IssuedOn, DateOnly? ExpiresOn);

internal sealed record CreateBusinessRequest(
    string? BusinessName,
    string? RegistrationNumber,
    string? TaxIdentificationNumber,
    DateOnly? IncorporationDate,
    int? BusinessCategoryId,
    string? Mobile,
    string? Phone,
    string? EmailAddress,
    string? Address1,
    string? Address2,
    string? SourceOfFunds,
    string? OccupationOrSector,
    bool IsPoliticallyExposed = false,
    DocumentRequest? Document = null,
    IReadOnlyList<OwnerShareRequest>? BeneficialOwners = null);

internal sealed record ReplaceOwnersRequest(IReadOnlyList<OwnerShareRequest>? BeneficialOwners);

internal sealed record MemberRequest(long MemberPartyId, int? GroupMemberRoleId, DateOnly? JoinedOn);

internal sealed record CreateGroupRequest(
    string? GroupName,
    DateOnly? FormationDate,
    string? MeetingFrequency,
    string? Mobile,
    string? Phone,
    string? EmailAddress,
    string? Address1,
    string? Address2,
    IReadOnlyList<MemberRequest>? Members = null);

internal sealed record LeaveGroupRequest(DateOnly? LeftOn);

internal sealed record ChangeRoleRequest(int? GroupMemberRoleId);

/// <summary>A4: parties (customers).</summary>
internal static class PartyEndpoints
{
    private const string PartySubModule = "PARTY";
    private const string GroupsSubModule = "GROUPS";

    public static RouteGroupBuilder MapPartyEndpoints(this RouteGroupBuilder api)
    {
        var parties = api.MapGroup("/parties").WithTags("Parties");

        parties.MapPost("/individuals", CreateIndividualAsync)
            .RequirePermission(ModuleCatalog.Party, PartySubModule, PermissionAction.Add)
            .Produces<CreatedParty>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        parties.MapGet("/{partyId:long}", GetAsync)
            .RequirePermission(ModuleCatalog.Party, PartySubModule, PermissionAction.View)
            .SensitiveRead()
            .Produces<PartyDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        parties.MapPost("/businesses", CreateBusinessAsync)
            .RequirePermission(ModuleCatalog.Party, PartySubModule, PermissionAction.Add)
            .Produces<CreatedParty>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        parties.MapGet("/{partyId:long}/beneficial-owners", OwnersAsync)
            .RequirePermission(ModuleCatalog.Party, PartySubModule, PermissionAction.View)
            .Produces<IReadOnlyList<BeneficialOwnerView>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        parties.MapPut("/{partyId:long}/beneficial-owners", ReplaceOwnersAsync)
            .RequirePermission(ModuleCatalog.Party, PartySubModule, PermissionAction.Edit)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        parties.MapPost("/groups", CreateGroupAsync)
            .RequirePermission(ModuleCatalog.Party, GroupsSubModule, PermissionAction.Add)
            .Produces<CreatedParty>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        parties.MapGet("/{partyId:long}/members", MembersAsync)
            .RequirePermission(ModuleCatalog.Party, GroupsSubModule, PermissionAction.View)
            .Produces<IReadOnlyList<GroupMemberView>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        parties.MapPost("/{partyId:long}/members", AddMemberAsync)
            .RequirePermission(ModuleCatalog.Party, GroupsSubModule, PermissionAction.Edit)
            .Produces<CreatedId>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        parties.MapPost("/{partyId:long}/members/{memberPartyId:long}/leave", LeaveAsync)
            .RequirePermission(ModuleCatalog.Party, GroupsSubModule, PermissionAction.Edit)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        parties.MapPut("/{partyId:long}/members/{memberPartyId:long}/role", ChangeRoleAsync)
            .RequirePermission(ModuleCatalog.Party, GroupsSubModule, PermissionAction.Edit)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return api;
    }

    private static async Task<IResult> CreateBusinessAsync(CreateBusinessRequest request, BusinessAdmin admin, CancellationToken cancellationToken)
    {
        var document = request.Document is { } doc ? new NewDocument(doc.DocumentTypeId, doc.DocumentNumber, doc.IssuedOn, doc.ExpiresOn) : null;
        var result = await admin.CreateAsync(
                new NewBusiness(
                    request.BusinessName ?? string.Empty,
                    request.RegistrationNumber,
                    request.TaxIdentificationNumber,
                    request.IncorporationDate,
                    request.BusinessCategoryId,
                    request.Mobile,
                    request.Phone,
                    request.EmailAddress,
                    request.Address1,
                    request.Address2,
                    request.SourceOfFunds,
                    request.OccupationOrSector,
                    request.IsPoliticallyExposed,
                    document,
                    Shares(request.BeneficialOwners)),
                cancellationToken)
            .ConfigureAwait(false);

        return result.Succeeded ? TypedResults.Created((string?)null, result.Value) : ResultProblems.From(result);
    }

    private static async Task<IResult> OwnersAsync(long partyId, BusinessAdmin admin, CancellationToken cancellationToken)
    {
        var owners = await admin.OwnersAsync(partyId, cancellationToken).ConfigureAwait(false);
        return owners is null ? ApiProblems.Result(StatusCodes.Status404NotFound, ProblemCodes.NotFound) : TypedResults.Ok(owners);
    }

    private static async Task<IResult> ReplaceOwnersAsync(long partyId, ReplaceOwnersRequest request, BusinessAdmin admin, CancellationToken cancellationToken)
    {
        var result = await admin.ReplaceOwnersAsync(partyId, Shares(request.BeneficialOwners), cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? TypedResults.NoContent() : ResultProblems.From(result);
    }

    private static async Task<IResult> CreateGroupAsync(CreateGroupRequest request, GroupAdmin admin, CancellationToken cancellationToken)
    {
        var result = await admin.CreateAsync(
                new NewGroup(
                    request.GroupName ?? string.Empty,
                    request.FormationDate,
                    request.MeetingFrequency,
                    request.Mobile,
                    request.Phone,
                    request.EmailAddress,
                    request.Address1,
                    request.Address2,
                    (request.Members ?? []).Select(member => new NewMember(member.MemberPartyId, member.GroupMemberRoleId, member.JoinedOn)).ToList()),
                cancellationToken)
            .ConfigureAwait(false);

        return result.Succeeded ? TypedResults.Created((string?)null, result.Value) : ResultProblems.From(result);
    }

    private static async Task<IResult> MembersAsync(long partyId, GroupAdmin admin, bool includeFormer = false, CancellationToken cancellationToken = default)
    {
        var members = await admin.MembersAsync(partyId, includeFormer, cancellationToken).ConfigureAwait(false);
        return members is null ? ApiProblems.Result(StatusCodes.Status404NotFound, ProblemCodes.NotFound) : TypedResults.Ok(members);
    }

    private static async Task<IResult> AddMemberAsync(long partyId, MemberRequest request, GroupAdmin admin, CancellationToken cancellationToken)
    {
        var result = await admin.AddMemberAsync(partyId, new NewMember(request.MemberPartyId, request.GroupMemberRoleId, request.JoinedOn), cancellationToken)
            .ConfigureAwait(false);
        return result.Succeeded ? TypedResults.Created((string?)null, new CreatedId(result.Value)) : ResultProblems.From(result);
    }

    private static async Task<IResult> LeaveAsync(long partyId, long memberPartyId, LeaveGroupRequest? request, GroupAdmin admin, CancellationToken cancellationToken)
    {
        var result = await admin.LeaveAsync(partyId, memberPartyId, request?.LeftOn, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? TypedResults.NoContent() : ResultProblems.From(result);
    }

    private static async Task<IResult> ChangeRoleAsync(long partyId, long memberPartyId, ChangeRoleRequest request, GroupAdmin admin, CancellationToken cancellationToken)
    {
        var result = await admin.ChangeRoleAsync(partyId, memberPartyId, request.GroupMemberRoleId, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? TypedResults.NoContent() : ResultProblems.From(result);
    }

    private static List<OwnerShare> Shares(IReadOnlyList<OwnerShareRequest>? owners) =>
        (owners ?? []).Select(owner => new OwnerShare(owner.OwnerPartyId, owner.OwnershipPercentage)).ToList();

    private static async Task<IResult> CreateIndividualAsync(CreateIndividualRequest request, PartyAdmin admin, CancellationToken cancellationToken)
    {
        var result = await admin.CreateIndividualAsync(
                new NewIndividual(
                    request.Title,
                    request.FirstName ?? string.Empty,
                    request.LastName ?? string.Empty,
                    request.DateOfBirth,
                    request.GenderId,
                    request.Nationality,
                    request.Mobile,
                    request.Phone,
                    request.EmailAddress,
                    request.Address1,
                    request.Address2,
                    request.IdentificationTypeId,
                    request.IdentificationNumber,
                    request.IdentificationIssuedOn,
                    request.IdentificationExpiresOn,
                    request.SourceOfFunds,
                    request.OccupationOrSector,
                    request.IsPoliticallyExposed),
                cancellationToken)
            .ConfigureAwait(false);

        return result.Succeeded
            ? TypedResults.Created((string?)null, result.Value)
            : ResultProblems.From(result);
    }

    private static async Task<IResult> GetAsync(long partyId, PartyAdmin admin, CancellationToken cancellationToken)
    {
        var party = await admin.GetAsync(partyId, cancellationToken).ConfigureAwait(false);
        return party is null ? ApiProblems.Result(StatusCodes.Status404NotFound, ProblemCodes.NotFound) : TypedResults.Ok(party);
    }
}
