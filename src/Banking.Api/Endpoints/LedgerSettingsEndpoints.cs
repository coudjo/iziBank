using Banking.Api.Problems;
using Banking.Api.Security;
using Banking.Application.Errors;
using Banking.Domain.Lookups;
using Banking.Domain.Security;
using Banking.Infrastructure.Ledger;
using Banking.Infrastructure.Security;

namespace Banking.Api.Endpoints;

internal sealed record LedgerSettingsRequest(IReadOnlyList<LedgerSettingValue>? Settings);

/// <summary>
/// B0-02: branch and product ledger settings (plan B 2.2). A change replaces the whole set and answers 202 with a change
/// request; it applies when a different person with <c>LED/SETTINGS</c> supervise approves it.
/// </summary>
internal static class LedgerSettingsEndpoints
{
    private const string Settings = "SETTINGS";

    public static RouteGroupBuilder MapLedgerSettingsEndpoints(this RouteGroupBuilder api)
    {
        var branch = api.MapGroup("/branches/{branchId:int}/ledger-settings").WithTags("Ledger settings");
        Read(branch.MapGet("/", GetBranchAsync));
        Write(branch.MapPut("/", RequestBranchAsync));
        Decide(branch.MapPost("/approve", ApproveBranchAsync));
        Decide(branch.MapPost("/reject", RejectBranchAsync));

        var product = api.MapGroup("/products/{productId:int}/ledger-settings").WithTags("Ledger settings");
        Read(product.MapGet("/", GetProductAsync));
        Write(product.MapPut("/", RequestProductAsync));
        Decide(product.MapPost("/approve", ApproveProductAsync));
        Decide(product.MapPost("/reject", RejectProductAsync));

        return api;
    }

    private static void Read(RouteHandlerBuilder route) =>
        route.RequirePermission(ModuleCatalog.Ledger, Settings, PermissionAction.View)
            .Produces<LedgerSettingsView>()
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static void Write(RouteHandlerBuilder route) =>
        route.RequirePermission(ModuleCatalog.Ledger, Settings, PermissionAction.Edit)
            .Produces<ChangeRequestAccepted>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static void Decide(RouteHandlerBuilder route) =>
        route.RequirePermission(ModuleCatalog.Ledger, Settings, PermissionAction.Supervise)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> GetBranchAsync(int branchId, LedgerSettingsAdmin admin, CancellationToken cancellationToken) =>
        View(await admin.BranchAsync(branchId, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> GetProductAsync(int productId, LedgerSettingsAdmin admin, CancellationToken cancellationToken) =>
        View(await admin.ProductAsync(productId, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> RequestBranchAsync(int branchId, LedgerSettingsRequest request, LedgerSettingsAdmin admin, CancellationToken cancellationToken) =>
        request.Settings is null
            ? MissingSettings()
            : Accepted(await admin.RequestBranchAsync(branchId, request.Settings, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> RequestProductAsync(int productId, LedgerSettingsRequest request, LedgerSettingsAdmin admin, CancellationToken cancellationToken) =>
        request.Settings is null
            ? MissingSettings()
            : Accepted(await admin.RequestProductAsync(productId, request.Settings, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> ApproveBranchAsync(int branchId, LedgerSettingsAdmin admin, CancellationToken cancellationToken) =>
        Done(await admin.ApproveBranchAsync(branchId, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> ApproveProductAsync(int productId, LedgerSettingsAdmin admin, CancellationToken cancellationToken) =>
        Done(await admin.ApproveProductAsync(productId, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> RejectBranchAsync(int branchId, RejectRequest request, LedgerSettingsAdmin admin, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(request.Reason)
            ? MissingReason()
            : Done(await admin.RejectBranchAsync(branchId, request.Reason, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> RejectProductAsync(int productId, RejectRequest request, LedgerSettingsAdmin admin, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(request.Reason)
            ? MissingReason()
            : Done(await admin.RejectProductAsync(productId, request.Reason, cancellationToken).ConfigureAwait(false));

    private static IResult View(LedgerSettingsView? view) =>
        view is null ? ApiProblems.Result(StatusCodes.Status404NotFound, ProblemCodes.NotFound) : TypedResults.Ok(view);

    private static IResult Accepted(OperationResult<long> result) =>
        result.Succeeded ? TypedResults.Accepted((string?)null, new ChangeRequestAccepted(result.Value)) : ResultProblems.From(result);

    private static IResult Done(OperationResult<long> result) =>
        result.Succeeded ? TypedResults.NoContent() : ResultProblems.From(result);

    private static IResult MissingSettings() =>
        ApiProblems.Validation(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["settings"] = ["Send the full list of settings (an empty list removes them all)."]
        });

    private static IResult MissingReason() =>
        ApiProblems.Validation(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["reason"] = ["Give a reason for rejecting this change."]
        });
}
