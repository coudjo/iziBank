using Banking.Api.Problems;
using Banking.Api.Security;
using Banking.Application.Errors;
using Banking.Domain.Lookups;
using Banking.Domain.Security;
using Banking.Infrastructure.Products;
using Banking.Infrastructure.Security;

namespace Banking.Api.Endpoints;

internal sealed record ProductRequest(
    string? ProductCode,
    string? ProductName,
    int? ProductTypeId,
    string? AccountPrefix,
    int? CurrencyId,
    long? LedgerControlAccountId,
    decimal? CreditInterestRate,
    decimal? DebitInterestRate,
    decimal? MinimumBalance,
    short? AccountsPerCustomer,
    bool? IsChequeBookAllowed,
    bool? IsFreezed,
    bool? IsActive);

internal sealed record ProductRateRequest(int? RateTypeId, decimal? MinimumAmount, decimal? MaximumAmount, decimal? InterestRate, DateOnly? EffectiveFrom, DateOnly? EffectiveTo);

internal sealed record CloseRateRequest(DateOnly? EffectiveTo);

/// <summary>A5-01 and A5-02: products with maker-checker, and their effective-dated interest rates.</summary>
internal static class ProductEndpoints
{
    private const string ProductArea = "PRODUCT";
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    public static RouteGroupBuilder MapProductEndpoints(this RouteGroupBuilder api)
    {
        var products = api.MapGroup("/products").WithTags("Products");

        products.MapGet("/", ListAsync)
            .RequirePermission(ModuleCatalog.Accounts, ProductArea, PermissionAction.View)
            .Produces<PagedResult<ProductView>>();

        products.MapGet("/{productId:int}", GetAsync)
            .RequirePermission(ModuleCatalog.Accounts, ProductArea, PermissionAction.View)
            .Produces<ProductView>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        products.MapPost("/", CreateAsync)
            .RequirePermission(ModuleCatalog.Accounts, ProductArea, PermissionAction.Add)
            .Produces<CreatedId>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        products.MapPut("/{productId:int}", UpdateAsync)
            .RequirePermission(ModuleCatalog.Accounts, ProductArea, PermissionAction.Edit)
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ChangeRequestAccepted>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        products.MapPost("/{productId:int}/supervise", ApproveAsync)
            .RequirePermission(ModuleCatalog.Accounts, ProductArea, PermissionAction.Supervise)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        products.MapPost("/{productId:int}/reject", RejectAsync)
            .RequirePermission(ModuleCatalog.Accounts, ProductArea, PermissionAction.Supervise)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        var rates = products.MapGroup("/{productId:int}/interest-rates");

        rates.MapGet("/", ListRatesAsync)
            .RequirePermission(ModuleCatalog.Accounts, ProductArea, PermissionAction.View)
            .Produces<IReadOnlyList<ProductRateView>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        rates.MapPost("/", AddRateAsync)
            .RequirePermission(ModuleCatalog.Accounts, ProductArea, PermissionAction.Edit)
            .Produces<ProductRateAdded>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        rates.MapPut("/{rateId:long}", UpdateRateAsync)
            .RequirePermission(ModuleCatalog.Accounts, ProductArea, PermissionAction.Edit)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        rates.MapPost("/{rateId:long}/close", CloseRateAsync)
            .RequirePermission(ModuleCatalog.Accounts, ProductArea, PermissionAction.Edit)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        rates.MapDelete("/{rateId:long}", DeleteRateAsync)
            .RequirePermission(ModuleCatalog.Accounts, ProductArea, PermissionAction.Edit)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return api;
    }

    private static async Task<IResult> ListAsync(
        ProductAdmin admin,
        int? productTypeId = null,
        string? search = null,
        string? supervisionState = null,
        bool? isActive = null,
        bool openable = false,
        int page = 1,
        int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default) =>
        TypedResults.Ok(await admin.ListAsync(
                new ProductFilter(productTypeId, search, supervisionState, isActive, openable),
                Math.Max(1, page),
                Math.Clamp(pageSize, 1, MaxPageSize),
                cancellationToken)
            .ConfigureAwait(false));

    private static async Task<IResult> GetAsync(int productId, ProductAdmin admin, CancellationToken cancellationToken)
    {
        var product = await admin.GetAsync(productId, cancellationToken).ConfigureAwait(false);
        return product is null ? ApiProblems.Result(StatusCodes.Status404NotFound, ProblemCodes.NotFound) : TypedResults.Ok(product);
    }

    private static async Task<IResult> CreateAsync(ProductRequest request, ProductAdmin admin, CancellationToken cancellationToken)
    {
        var result = await admin.CreateAsync(ToDraft(request), cancellationToken).ConfigureAwait(false);
        return result.Succeeded
            ? TypedResults.Created((string?)null, new CreatedId(result.Value))
            : ResultProblems.From(result.ProblemCode!, result.Errors);
    }

    private static async Task<IResult> UpdateAsync(int productId, ProductRequest request, ProductAdmin admin, CancellationToken cancellationToken)
    {
        var result = await admin.UpdateAsync(productId, ToDraft(request), cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return ResultProblems.From(result.ProblemCode!, result.Errors);
        }

        return result.Value!.ChangeRequestId is { } changeRequestId
            ? TypedResults.Accepted((string?)null, new ChangeRequestAccepted(changeRequestId))
            : TypedResults.NoContent();
    }

    private static async Task<IResult> ApproveAsync(int productId, ProductAdmin admin, CancellationToken cancellationToken)
    {
        var result = await admin.ApproveAsync(productId, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? TypedResults.NoContent() : ResultProblems.From(result.ProblemCode!, result.Errors);
    }

    private static async Task<IResult> RejectAsync(int productId, RejectRequest? request, ProductAdmin admin, CancellationToken cancellationToken)
    {
        var result = await admin.RejectAsync(productId, request?.Reason, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? TypedResults.NoContent() : ResultProblems.From(result.ProblemCode!, result.Errors);
    }

    private static async Task<IResult> ListRatesAsync(int productId, ProductRates rates, CancellationToken cancellationToken)
    {
        var rows = await rates.ListAsync(productId, cancellationToken).ConfigureAwait(false);
        return rows is null ? ApiProblems.Result(StatusCodes.Status404NotFound, ProblemCodes.NotFound) : TypedResults.Ok(rows);
    }

    private static async Task<IResult> AddRateAsync(int productId, ProductRateRequest request, ProductRates rates, CancellationToken cancellationToken)
    {
        var result = await rates.AddAsync(productId, ToDraft(request), cancellationToken).ConfigureAwait(false);
        return result.Succeeded
            ? TypedResults.Created((string?)null, result.Value)
            : ResultProblems.From(result.ProblemCode!, result.Errors);
    }

    private static async Task<IResult> UpdateRateAsync(int productId, long rateId, ProductRateRequest request, ProductRates rates, CancellationToken cancellationToken)
    {
        var result = await rates.UpdateAsync(productId, rateId, ToDraft(request), cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? TypedResults.NoContent() : ResultProblems.From(result.ProblemCode!, result.Errors);
    }

    private static async Task<IResult> CloseRateAsync(int productId, long rateId, CloseRateRequest request, ProductRates rates, CancellationToken cancellationToken)
    {
        var result = await rates.CloseAsync(productId, rateId, request.EffectiveTo, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? TypedResults.NoContent() : ResultProblems.From(result.ProblemCode!, result.Errors);
    }

    private static async Task<IResult> DeleteRateAsync(int productId, long rateId, ProductRates rates, CancellationToken cancellationToken)
    {
        var result = await rates.DeleteAsync(productId, rateId, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? TypedResults.NoContent() : ResultProblems.From(result.ProblemCode!, result.Errors);
    }

    private static ProductDraft ToDraft(ProductRequest request) =>
        new(
            request.ProductCode,
            request.ProductName ?? string.Empty,
            request.ProductTypeId,
            request.AccountPrefix,
            request.CurrencyId,
            request.LedgerControlAccountId,
            request.CreditInterestRate,
            request.DebitInterestRate,
            request.MinimumBalance,
            request.AccountsPerCustomer,
            request.IsChequeBookAllowed,
            request.IsFreezed,
            request.IsActive);

    private static ProductRateDraft ToDraft(ProductRateRequest request) =>
        new(request.RateTypeId, request.MinimumAmount, request.MaximumAmount, request.InterestRate, request.EffectiveFrom, request.EffectiveTo);
}
