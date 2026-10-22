using Banking.Api.Problems;
using Banking.Api.Security;
using Banking.Application.Errors;
using Banking.Domain.Lookups;
using Banking.Domain.Security;
using Banking.Infrastructure.Charges;
using Banking.Infrastructure.Security;

namespace Banking.Api.Endpoints;

internal sealed record ChargeAccepted(long ProductChargeId);

/// <summary>
/// B0-03: charge codes and product charges (plan B 2.4, 8). Charge codes are reference data (<c>ACC/CHARGES</c> edit);
/// product charges are supervised: create and edit answer 202 while they wait, and approval or rejection needs supervise
/// on the same sub-module.
/// </summary>
internal static class ChargeEndpoints
{
    private const string Charges = "CHARGES";

    public static RouteGroupBuilder MapChargeEndpoints(this RouteGroupBuilder api)
    {
        var codes = api.MapGroup("/charge-codes").WithTags("Charges");
        codes.MapGet("/", ListCodesAsync)
            .RequirePermission(ModuleCatalog.Accounts, Charges, PermissionAction.View)
            .Produces<IReadOnlyList<ChargeCodeView>>();
        codes.MapPost("/", CreateCodeAsync)
            .RequirePermission(ModuleCatalog.Accounts, Charges, PermissionAction.Edit)
            .Produces<int>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);
        codes.MapPut("/{chargeId:int}", UpdateCodeAsync)
            .RequirePermission(ModuleCatalog.Accounts, Charges, PermissionAction.Edit)
            .Produces<int>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        var products = api.MapGroup("/products/{productId:int}/charges").WithTags("Charges");
        products.MapGet("/", ListProductChargesAsync)
            .RequirePermission(ModuleCatalog.Accounts, Charges, PermissionAction.View)
            .Produces<IReadOnlyList<ProductChargeView>>()
            .ProducesProblem(StatusCodes.Status404NotFound);
        products.MapPost("/", CreateProductChargeAsync)
            .RequirePermission(ModuleCatalog.Accounts, Charges, PermissionAction.Edit)
            .Produces<ChargeAccepted>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
        products.MapPut("/{productChargeId:long}", UpdatePendingAsync)
            .RequirePermission(ModuleCatalog.Accounts, Charges, PermissionAction.Edit)
            .Produces<ChargeAccepted>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);
        products.MapPost("/{productChargeId:long}/supervise", ApproveAsync)
            .RequirePermission(ModuleCatalog.Accounts, Charges, PermissionAction.Supervise)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status409Conflict);
        products.MapPost("/{productChargeId:long}/reject", RejectAsync)
            .RequirePermission(ModuleCatalog.Accounts, Charges, PermissionAction.Supervise)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        return api;
    }

    private static async Task<IResult> ListCodesAsync(ChargeAdmin admin, CancellationToken cancellationToken) =>
        TypedResults.Ok(await admin.ListCodesAsync(cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> CreateCodeAsync(ChargeCodeDraft draft, ChargeAdmin admin, CancellationToken cancellationToken)
    {
        var result = await admin.CreateCodeAsync(draft, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? TypedResults.Created((string?)null, result.Value) : ResultProblems.From(result);
    }

    private static async Task<IResult> UpdateCodeAsync(int chargeId, ChargeCodeDraft draft, ChargeAdmin admin, CancellationToken cancellationToken)
    {
        var result = await admin.UpdateCodeAsync(chargeId, draft, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? TypedResults.Ok(result.Value) : ResultProblems.From(result);
    }

    private static async Task<IResult> ListProductChargesAsync(int productId, ChargeAdmin admin, CancellationToken cancellationToken)
    {
        var rows = await admin.ListProductChargesAsync(productId, cancellationToken).ConfigureAwait(false);
        return rows is null ? ApiProblems.Result(StatusCodes.Status404NotFound, ProblemCodes.NotFound) : TypedResults.Ok(rows);
    }

    private static async Task<IResult> CreateProductChargeAsync(int productId, ProductChargeDraft draft, ChargeAdmin admin, CancellationToken cancellationToken)
    {
        var result = await admin.CreateProductChargeAsync(productId, draft, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? TypedResults.Accepted((string?)null, new ChargeAccepted(result.Value)) : ResultProblems.From(result);
    }

    private static async Task<IResult> UpdatePendingAsync(int productId, long productChargeId, ProductChargeDraft draft, ChargeAdmin admin, CancellationToken cancellationToken)
    {
        var result = await admin.UpdatePendingChargeAsync(productId, productChargeId, draft, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? TypedResults.Accepted((string?)null, new ChargeAccepted(result.Value)) : ResultProblems.From(result);
    }

    private static async Task<IResult> ApproveAsync(int productId, long productChargeId, ChargeAdmin admin, CancellationToken cancellationToken)
    {
        var result = await admin.ApproveAsync(productId, productChargeId, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? TypedResults.NoContent() : ResultProblems.From(result);
    }

    private static async Task<IResult> RejectAsync(int productId, long productChargeId, RejectRequest request, ChargeAdmin admin, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return ApiProblems.Validation(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["reason"] = ["Give a reason for rejecting this charge."]
            });
        }

        var result = await admin.RejectAsync(productId, productChargeId, request.Reason, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? TypedResults.NoContent() : ResultProblems.From(result);
    }
}
