using Banking.Api.Problems;
using Banking.Api.Security;
using Banking.Application.Errors;
using Banking.Domain.Lookups;
using Banking.Domain.Security;
using Banking.Infrastructure.Ledger;
using Banking.Infrastructure.Security;

namespace Banking.Api.Endpoints;

internal sealed record LedgerAccountRequest(string? LedgerAccountCode, string? AccountName, int? LedgerAccountTypeId, int? CurrencyId);

/// <summary>A3-03: chart of accounts with maker-checker.</summary>
internal static class LedgerEndpoints
{
    private const string Chart = "CHART";
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    public static RouteGroupBuilder MapLedgerEndpoints(this RouteGroupBuilder api)
    {
        var accounts = api.MapGroup("/ledger-accounts").WithTags("Ledger accounts");

        accounts.MapGet("/", ListAsync)
            .RequirePermission(ModuleCatalog.Ledger, Chart, PermissionAction.View)
            .Produces<PagedResult<LedgerAccountView>>();

        accounts.MapGet("/{ledgerAccountId:long}", GetAsync)
            .RequirePermission(ModuleCatalog.Ledger, Chart, PermissionAction.View)
            .Produces<LedgerAccountView>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        accounts.MapPost("/", CreateAsync)
            .RequirePermission(ModuleCatalog.Ledger, Chart, PermissionAction.Add)
            .Produces<CreatedId>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        accounts.MapPut("/{ledgerAccountId:long}", UpdateAsync)
            .RequirePermission(ModuleCatalog.Ledger, Chart, PermissionAction.Edit)
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ChangeRequestAccepted>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        accounts.MapPost("/{ledgerAccountId:long}/approve", ApproveAsync)
            .RequirePermission(ModuleCatalog.Ledger, Chart, PermissionAction.Supervise)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        accounts.MapPost("/{ledgerAccountId:long}/reject", RejectAsync)
            .RequirePermission(ModuleCatalog.Ledger, Chart, PermissionAction.Supervise)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return api;
    }

    private static async Task<IResult> ListAsync(
        LedgerAccountAdmin admin,
        int? branchId = null,
        int? ledgerAccountTypeId = null,
        string? search = null,
        string? supervisionState = null,
        int page = 1,
        int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default) =>
        TypedResults.Ok(await admin.ListAsync(
                new LedgerAccountFilter(branchId, ledgerAccountTypeId, search, supervisionState),
                Math.Max(1, page),
                Math.Clamp(pageSize, 1, MaxPageSize),
                cancellationToken)
            .ConfigureAwait(false));

    private static async Task<IResult> GetAsync(long ledgerAccountId, LedgerAccountAdmin admin, CancellationToken cancellationToken)
    {
        var account = await admin.GetAsync(ledgerAccountId, cancellationToken).ConfigureAwait(false);
        return account is null ? ApiProblems.Result(StatusCodes.Status404NotFound, ProblemCodes.NotFound) : TypedResults.Ok(account);
    }

    private static async Task<IResult> CreateAsync(LedgerAccountRequest request, LedgerAccountAdmin admin, CancellationToken cancellationToken)
    {
        var result = await admin.CreateAsync(ToDraft(request), cancellationToken).ConfigureAwait(false);
        return result.Succeeded
            ? TypedResults.Created((string?)null, new CreatedId(result.Value))
            : ResultProblems.From(result.ProblemCode!, result.Errors);
    }

    private static async Task<IResult> UpdateAsync(long ledgerAccountId, LedgerAccountRequest request, LedgerAccountAdmin admin, CancellationToken cancellationToken)
    {
        var result = await admin.UpdateAsync(ledgerAccountId, ToDraft(request), cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return ResultProblems.From(result.ProblemCode!, result.Errors);
        }

        return result.Value!.ChangeRequestId is { } changeRequestId
            ? TypedResults.Accepted((string?)null, new ChangeRequestAccepted(changeRequestId))
            : TypedResults.NoContent();
    }

    private static async Task<IResult> ApproveAsync(long ledgerAccountId, LedgerAccountAdmin admin, CancellationToken cancellationToken)
    {
        var result = await admin.ApproveAsync(ledgerAccountId, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? TypedResults.NoContent() : ResultProblems.From(result.ProblemCode!, result.Errors);
    }

    private static async Task<IResult> RejectAsync(long ledgerAccountId, RejectRequest? request, LedgerAccountAdmin admin, CancellationToken cancellationToken)
    {
        var result = await admin.RejectAsync(ledgerAccountId, request?.Reason, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? TypedResults.NoContent() : ResultProblems.From(result.ProblemCode!, result.Errors);
    }

    private static LedgerAccountDraft ToDraft(LedgerAccountRequest request) =>
        new(request.LedgerAccountCode, request.AccountName ?? string.Empty, request.LedgerAccountTypeId, request.CurrencyId);
}
