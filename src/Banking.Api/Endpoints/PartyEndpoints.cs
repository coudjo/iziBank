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

/// <summary>A4: parties (customers).</summary>
internal static class PartyEndpoints
{
    private const string PartySubModule = "PARTY";

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

        return api;
    }

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
