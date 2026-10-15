using Banking.Api.Problems;
using Banking.Api.Security;
using Banking.Application.Errors;
using Banking.Domain.Lookups;
using Banking.Domain.Security;
using Banking.Infrastructure.Parties;

namespace Banking.Api.Endpoints;

internal sealed record NextOfKinRequest(string? FullName, string? Relationship, string? MobileNumber);

internal sealed record AttributeValueRequest(string? AttributeValue);

internal sealed record DesignateCreditOfficerRequest(long StaffUserId);

internal sealed record CreditOfficerStateRequest(bool IsDisabled);

internal sealed record AssignCreditOfficerRequest(long? CreditOfficerId);

/// <summary>A4-05: next of kin, regulatory attributes, credit officers.</summary>
internal static class PartyProfileEndpoints
{
    private const string PartySubModule = "PARTY";
    private const string CreditOfficers = "CREDIT_OFFICERS";

    public static RouteGroupBuilder MapPartyProfileEndpoints(this RouteGroupBuilder api)
    {
        var party = api.MapGroup("/parties/{partyId:long}").WithTags("Party profile");

        party.MapGet("/next-of-kin", NextOfKinAsync)
            .RequirePermission(ModuleCatalog.Party, PartySubModule, PermissionAction.View)
            .Produces<IReadOnlyList<NextOfKinView>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        party.MapPost("/next-of-kin", AddNextOfKinAsync)
            .RequirePermission(ModuleCatalog.Party, PartySubModule, PermissionAction.Edit)
            .Produces<CreatedId>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        party.MapPut("/next-of-kin/{nextOfKinId:long}", UpdateNextOfKinAsync)
            .RequirePermission(ModuleCatalog.Party, PartySubModule, PermissionAction.Edit)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        party.MapDelete("/next-of-kin/{nextOfKinId:long}", DeleteNextOfKinAsync)
            .RequirePermission(ModuleCatalog.Party, PartySubModule, PermissionAction.Edit)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        party.MapGet("/regulatory-attributes", AttributesAsync)
            .RequirePermission(ModuleCatalog.Party, PartySubModule, PermissionAction.View)
            .Produces<IReadOnlyList<RegulatoryAttributeView>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        party.MapGet("/regulatory-attributes/{code}/history", AttributeHistoryAsync)
            .RequirePermission(ModuleCatalog.Party, PartySubModule, PermissionAction.View)
            .Produces<IReadOnlyList<RegulatoryAttributeView>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        party.MapPut("/regulatory-attributes/{code}", SetAttributeAsync)
            .RequirePermission(ModuleCatalog.Party, PartySubModule, PermissionAction.Edit)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        party.MapPut("/credit-officer", AssignAsync)
            .RequirePermission(ModuleCatalog.Party, PartySubModule, PermissionAction.Edit)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        var officers = api.MapGroup("/credit-officers").WithTags("Credit officers");

        officers.MapGet("/", CreditOfficersAsync)
            .RequirePermission(ModuleCatalog.Party, CreditOfficers, PermissionAction.View)
            .Produces<IReadOnlyList<CreditOfficerView>>();

        officers.MapPost("/", DesignateAsync)
            .RequirePermission(ModuleCatalog.Party, CreditOfficers, PermissionAction.Add)
            .Produces<CreatedId>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        officers.MapPut("/{creditOfficerId:long}", SetOfficerStateAsync)
            .RequirePermission(ModuleCatalog.Party, CreditOfficers, PermissionAction.Edit)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return api;
    }

    private static async Task<IResult> NextOfKinAsync(long partyId, PartyProfile profile, CancellationToken cancellationToken)
    {
        var rows = await profile.NextOfKinAsync(partyId, cancellationToken).ConfigureAwait(false);
        return rows is null ? NotFound() : TypedResults.Ok(rows);
    }

    private static async Task<IResult> AddNextOfKinAsync(long partyId, NextOfKinRequest request, PartyProfile profile, CancellationToken cancellationToken)
    {
        var result = await profile.AddNextOfKinAsync(partyId, Draft(request), cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? TypedResults.Created((string?)null, new CreatedId(result.Value)) : ResultProblems.From(result);
    }

    private static async Task<IResult> UpdateNextOfKinAsync(long partyId, long nextOfKinId, NextOfKinRequest request, PartyProfile profile, CancellationToken cancellationToken)
    {
        var result = await profile.UpdateNextOfKinAsync(partyId, nextOfKinId, Draft(request), cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? TypedResults.NoContent() : ResultProblems.From(result);
    }

    private static async Task<IResult> DeleteNextOfKinAsync(long partyId, long nextOfKinId, PartyProfile profile, CancellationToken cancellationToken)
    {
        var result = await profile.DeleteNextOfKinAsync(partyId, nextOfKinId, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? TypedResults.NoContent() : ResultProblems.From(result);
    }

    private static async Task<IResult> AttributesAsync(long partyId, PartyProfile profile, CancellationToken cancellationToken)
    {
        var rows = await profile.CurrentAttributesAsync(partyId, cancellationToken).ConfigureAwait(false);
        return rows is null ? NotFound() : TypedResults.Ok(rows);
    }

    private static async Task<IResult> AttributeHistoryAsync(long partyId, string code, PartyProfile profile, CancellationToken cancellationToken)
    {
        var rows = await profile.AttributeHistoryAsync(partyId, code, cancellationToken).ConfigureAwait(false);
        return rows is null ? NotFound() : TypedResults.Ok(rows);
    }

    private static async Task<IResult> SetAttributeAsync(long partyId, string code, AttributeValueRequest request, PartyProfile profile, CancellationToken cancellationToken)
    {
        var result = await profile.SetAttributeAsync(partyId, code, request.AttributeValue, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? TypedResults.NoContent() : ResultProblems.From(result);
    }

    private static async Task<IResult> AssignAsync(long partyId, AssignCreditOfficerRequest request, PartyProfile profile, CancellationToken cancellationToken)
    {
        var result = await profile.AssignAsync(partyId, request.CreditOfficerId, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? TypedResults.NoContent() : ResultProblems.From(result);
    }

    private static async Task<IResult> CreditOfficersAsync(
        PartyProfile profile,
        int? branchId = null,
        bool includeDisabled = false,
        CancellationToken cancellationToken = default) =>
        TypedResults.Ok(await profile.CreditOfficersAsync(branchId, includeDisabled, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> DesignateAsync(DesignateCreditOfficerRequest request, PartyProfile profile, CancellationToken cancellationToken)
    {
        var result = await profile.DesignateAsync(request.StaffUserId, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? TypedResults.Created((string?)null, new CreatedId(result.Value)) : ResultProblems.From(result);
    }

    private static async Task<IResult> SetOfficerStateAsync(long creditOfficerId, CreditOfficerStateRequest request, PartyProfile profile, CancellationToken cancellationToken)
    {
        var result = await profile.SetOfficerDisabledAsync(creditOfficerId, request.IsDisabled, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? TypedResults.NoContent() : ResultProblems.From(result);
    }

    private static NextOfKinDraft Draft(NextOfKinRequest request) =>
        new(request.FullName ?? string.Empty, request.Relationship, request.MobileNumber);

    private static IResult NotFound() => ApiProblems.Result(StatusCodes.Status404NotFound, ProblemCodes.NotFound);
}
