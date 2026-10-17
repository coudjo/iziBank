using System.Text.Json;
using Banking.Api.Problems;
using Banking.Api.Security;
using Banking.Application.Errors;
using Banking.Domain.Lookups;
using Banking.Domain.Security;
using Banking.Infrastructure.Accounts;
using Banking.Infrastructure.Security;

namespace Banking.Api.Endpoints;

internal sealed record AccountHolderRequest(long? PartyId, int? AccountHolderRoleId);

internal sealed record OpenAccountRequest(int? ProductId, string? AccountName, int? OperatingModeId, IReadOnlyList<AccountHolderRequest>? Holders);

internal sealed record AccountSearchRequest(string? Q, int? BranchId, int? ProductId, string? Status, int? Page, int? PageSize);

internal sealed record FreezeActionRequest(string? Action, decimal? Amount, string? Remarks);

internal sealed record CloseAccountRequest(int? CloseReasonId);

internal sealed record ClassificationRequest(int? AccountClassificationId, int? AccountSubClassificationId);

internal sealed record ReminderRequest(string? ReminderText);

internal sealed record ClassificationCatalogRequest(string? Code, string? Name);

/// <summary>A5-04 to A5-08: open, read, approve, reject, service and search accounts.</summary>
internal static class AccountEndpoints
{
    private const string AccountArea = "ACCOUNT";
    private const string ConditionArea = "CONDITION";
    private const string ClassificationArea = "CLASSIFICATION";
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static RouteGroupBuilder MapAccountEndpoints(this RouteGroupBuilder api)
    {
        var accounts = api.MapGroup("/accounts").WithTags("Accounts");

        accounts.MapPost("/", OpenAsync)
            .RequirePermission(ModuleCatalog.Accounts, AccountArea, PermissionAction.Add)
            .Produces<AccountOpened>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        accounts.MapGet("/{accountId:long}", GetAsync)
            .RequirePermission(ModuleCatalog.Accounts, AccountArea, PermissionAction.View)
            .Produces<AccountView>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        accounts.MapGet("/{accountId:long}/balance", GetBalanceAsync)
            .RequirePermission(ModuleCatalog.Accounts, AccountArea, PermissionAction.View)
            .Produces<AccountBalanceView>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        accounts.MapPost("/{accountId:long}/supervise", ApproveAsync)
            .RequirePermission(ModuleCatalog.Accounts, AccountArea, PermissionAction.Supervise)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        accounts.MapPost("/{accountId:long}/reject", RejectAsync)
            .RequirePermission(ModuleCatalog.Accounts, AccountArea, PermissionAction.Supervise)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        accounts.MapGet("/", ListAsync)
            .RequirePermission(ModuleCatalog.Accounts, AccountArea, PermissionAction.View)
            .Produces<PagedResult<AccountListRow>>();

        // The term travels in the body: names, mobiles and ID numbers stay out of URLs (as for customers, A4-n).
        accounts.MapPost("/search", SearchAsync)
            .RequirePermission(ModuleCatalog.Accounts, AccountArea, PermissionAction.View)
            .Produces<PagedResult<AccountListRow>>();

        accounts.MapGet("/{accountId:long}/freeze-actions", FreezeHistoryAsync)
            .RequirePermission(ModuleCatalog.Accounts, ConditionArea, PermissionAction.View)
            .Produces<IReadOnlyList<FreezeActionView>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        accounts.MapPost("/{accountId:long}/freeze-actions", FreezeActionAsync)
            .RequirePermission(ModuleCatalog.Accounts, ConditionArea, PermissionAction.Edit)
            .Produces<FreezeActionResult>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        accounts.MapGet("/{accountId:long}/close-check", CloseCheckAsync)
            .RequirePermission(ModuleCatalog.Accounts, AccountArea, PermissionAction.View)
            .Produces<AccountCloseStatus>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        accounts.MapPost("/{accountId:long}/close", CloseAsync)
            .RequirePermission(ModuleCatalog.Accounts, AccountArea, PermissionAction.Delete)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        accounts.MapPost("/{accountId:long}/dormancy/mark", MarkDormantAsync)
            .RequirePermission(ModuleCatalog.Accounts, ConditionArea, PermissionAction.Edit)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        accounts.MapPost("/{accountId:long}/dormancy/reactivate", ReactivateAsync)
            .RequirePermission(ModuleCatalog.Accounts, ConditionArea, PermissionAction.Edit)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        accounts.MapGet("/{accountId:long}/classifications", ClassificationsAsync)
            .RequirePermission(ModuleCatalog.Accounts, ClassificationArea, PermissionAction.View)
            .Produces<IReadOnlyList<AccountClassificationView>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        accounts.MapPost("/{accountId:long}/classifications", AssignClassificationAsync)
            .RequirePermission(ModuleCatalog.Accounts, ClassificationArea, PermissionAction.Edit)
            .Produces<CreatedId>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        accounts.MapDelete("/{accountId:long}/classifications/{assignmentId:long}", RemoveClassificationAsync)
            .RequirePermission(ModuleCatalog.Accounts, ClassificationArea, PermissionAction.Edit)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        accounts.MapGet("/{accountId:long}/reminders", RemindersAsync)
            .RequirePermission(ModuleCatalog.Accounts, AccountArea, PermissionAction.View)
            .Produces<IReadOnlyList<AccountReminderView>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        accounts.MapPost("/{accountId:long}/reminders", AddReminderAsync)
            .RequirePermission(ModuleCatalog.Accounts, AccountArea, PermissionAction.Edit)
            .Produces<CreatedId>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        accounts.MapDelete("/{accountId:long}/reminders/{reminderId:long}", RemoveReminderAsync)
            .RequirePermission(ModuleCatalog.Accounts, AccountArea, PermissionAction.Edit)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        var catalog = api.MapGroup("/account-classifications").WithTags("Accounts");

        catalog.MapGet("/", CatalogAsync)
            .RequirePermission(ModuleCatalog.Accounts, ClassificationArea, PermissionAction.View)
            .Produces<IReadOnlyList<ClassificationOption>>();

        catalog.MapPost("/", CreateClassificationAsync)
            .RequirePermission(ModuleCatalog.Accounts, ClassificationArea, PermissionAction.Add)
            .Produces<CreatedId>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        catalog.MapPost("/{classificationId:int}/sub-classifications", CreateSubClassificationAsync)
            .RequirePermission(ModuleCatalog.Accounts, ClassificationArea, PermissionAction.Add)
            .Produces<CreatedId>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return api;
    }

    private static async Task<IResult> ListAsync(
        AccountSearch search,
        int? branchId = null,
        int? productId = null,
        string? status = null,
        int page = 1,
        int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default) =>
        TypedResults.Ok(await search.SearchAsync(
                new AccountSearchFilter(null, branchId, productId, status),
                Math.Max(1, page),
                Math.Clamp(pageSize, 1, MaxPageSize),
                cancellationToken)
            .ConfigureAwait(false));

    private static async Task<IResult> SearchAsync(AccountSearchRequest request, AccountSearch search, CancellationToken cancellationToken) =>
        TypedResults.Ok(await search.SearchAsync(
                new AccountSearchFilter(request.Q, request.BranchId, request.ProductId, request.Status),
                Math.Max(1, request.Page ?? 1),
                Math.Clamp(request.PageSize ?? DefaultPageSize, 1, MaxPageSize),
                cancellationToken)
            .ConfigureAwait(false));

    private static async Task<IResult> FreezeHistoryAsync(long accountId, AccountConditions conditions, CancellationToken cancellationToken)
    {
        var rows = await conditions.HistoryAsync(accountId, cancellationToken).ConfigureAwait(false);
        return rows is null ? NotFound() : TypedResults.Ok(rows);
    }

    private static Task<IResult> FreezeActionAsync(
        long accountId,
        FreezeActionRequest request,
        HttpRequest http,
        AccountConditions conditions,
        IdempotencyStore idempotency,
        CancellationToken cancellationToken) =>
        IdempotentRequest.RunAsync(
            http,
            idempotency,
            JsonSerializer.SerializeToUtf8Bytes(request, Json),
            async () =>
            {
                var result = await conditions.ApplyAsync(accountId, new FreezeActionDraft(request.Action, request.Amount, request.Remarks), cancellationToken)
                    .ConfigureAwait(false);
                return result.Succeeded
                    ? IdempotentOutcome.Store(StatusCodes.Status201Created, result.Value)
                    : IdempotentOutcome.DoNotStore(ResultProblems.From(result));
            },
            cancellationToken);

    private static async Task<IResult> CloseCheckAsync(long accountId, AccountServicing servicing, CancellationToken cancellationToken)
    {
        var status = await servicing.CloseStatusAsync(accountId, cancellationToken).ConfigureAwait(false);
        return status is null ? NotFound() : TypedResults.Ok(status);
    }

    private static async Task<IResult> CloseAsync(long accountId, CloseAccountRequest request, AccountServicing servicing, CancellationToken cancellationToken) =>
        NoContentOr(await servicing.CloseAsync(accountId, new CloseAccountDraft(request.CloseReasonId), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> MarkDormantAsync(long accountId, AccountServicing servicing, CancellationToken cancellationToken) =>
        NoContentOr(await servicing.SetDormantAsync(accountId, dormant: true, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> ReactivateAsync(long accountId, AccountServicing servicing, CancellationToken cancellationToken) =>
        NoContentOr(await servicing.SetDormantAsync(accountId, dormant: false, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> ClassificationsAsync(long accountId, AccountServicing servicing, CancellationToken cancellationToken)
    {
        var rows = await servicing.ClassificationsAsync(accountId, cancellationToken).ConfigureAwait(false);
        return rows is null ? NotFound() : TypedResults.Ok(rows);
    }

    private static async Task<IResult> AssignClassificationAsync(long accountId, ClassificationRequest request, AccountServicing servicing, CancellationToken cancellationToken) =>
        CreatedOr(await servicing.AssignClassificationAsync(accountId, new ClassificationDraft(request.AccountClassificationId, request.AccountSubClassificationId), cancellationToken)
            .ConfigureAwait(false));

    private static async Task<IResult> RemoveClassificationAsync(long accountId, long assignmentId, AccountServicing servicing, CancellationToken cancellationToken) =>
        NoContentOr(await servicing.RemoveClassificationAsync(accountId, assignmentId, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> RemindersAsync(long accountId, AccountServicing servicing, CancellationToken cancellationToken)
    {
        var rows = await servicing.RemindersAsync(accountId, cancellationToken).ConfigureAwait(false);
        return rows is null ? NotFound() : TypedResults.Ok(rows);
    }

    private static async Task<IResult> AddReminderAsync(long accountId, ReminderRequest request, AccountServicing servicing, CancellationToken cancellationToken) =>
        CreatedOr(await servicing.AddReminderAsync(accountId, request.ReminderText, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> RemoveReminderAsync(long accountId, long reminderId, AccountServicing servicing, CancellationToken cancellationToken) =>
        NoContentOr(await servicing.RemoveReminderAsync(accountId, reminderId, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> CatalogAsync(AccountServicing servicing, CancellationToken cancellationToken) =>
        TypedResults.Ok(await servicing.ClassificationCatalogAsync(cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> CreateClassificationAsync(ClassificationCatalogRequest request, AccountServicing servicing, CancellationToken cancellationToken)
    {
        var result = await servicing.CreateClassificationAsync(null, request.Code, request.Name, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? TypedResults.Created((string?)null, new CreatedId(result.Value)) : ResultProblems.From(result);
    }

    private static async Task<IResult> CreateSubClassificationAsync(int classificationId, ClassificationCatalogRequest request, AccountServicing servicing, CancellationToken cancellationToken)
    {
        var result = await servicing.CreateClassificationAsync(classificationId, request.Code, request.Name, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? TypedResults.Created((string?)null, new CreatedId(result.Value)) : ResultProblems.From(result);
    }

    private static IResult NotFound() => ApiProblems.Result(StatusCodes.Status404NotFound, ProblemCodes.NotFound);

    private static IResult NoContentOr(OperationResult<long> result) =>
        result.Succeeded ? TypedResults.NoContent() : ResultProblems.From(result);

    private static IResult CreatedOr(OperationResult<long> result) =>
        result.Succeeded ? TypedResults.Created((string?)null, new CreatedId(result.Value)) : ResultProblems.From(result);

    private static Task<IResult> OpenAsync(
        OpenAccountRequest request,
        HttpRequest http,
        AccountOpening opening,
        IdempotencyStore idempotency,
        CancellationToken cancellationToken) =>
        IdempotentRequest.RunAsync(
            http,
            idempotency,
            JsonSerializer.SerializeToUtf8Bytes(request, Json),
            async () =>
            {
                var draft = new OpenAccountDraft(
                    request.ProductId,
                    request.AccountName,
                    request.OperatingModeId,
                    request.Holders?.Select(holder => new AccountHolderDraft(holder.PartyId, holder.AccountHolderRoleId)).ToList());
                var result = await opening.OpenAsync(draft, cancellationToken).ConfigureAwait(false);
                return result.Succeeded
                    ? IdempotentOutcome.Store(StatusCodes.Status201Created, result.Value)
                    : IdempotentOutcome.DoNotStore(ResultProblems.From(result));
            },
            cancellationToken);

    private static async Task<IResult> GetAsync(long accountId, AccountAdmin admin, CancellationToken cancellationToken)
    {
        var account = await admin.GetAsync(accountId, cancellationToken).ConfigureAwait(false);
        return account is null ? ApiProblems.Result(StatusCodes.Status404NotFound, ProblemCodes.NotFound) : TypedResults.Ok(account);
    }

    private static async Task<IResult> GetBalanceAsync(long accountId, AccountAdmin admin, CancellationToken cancellationToken)
    {
        var balance = await admin.GetBalanceAsync(accountId, cancellationToken).ConfigureAwait(false);
        return balance is null ? ApiProblems.Result(StatusCodes.Status404NotFound, ProblemCodes.NotFound) : TypedResults.Ok(balance);
    }

    private static async Task<IResult> ApproveAsync(long accountId, AccountAdmin admin, CancellationToken cancellationToken)
    {
        var result = await admin.ApproveAsync(accountId, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? TypedResults.NoContent() : ResultProblems.From(result);
    }

    private static async Task<IResult> RejectAsync(long accountId, RejectRequest? request, AccountAdmin admin, CancellationToken cancellationToken)
    {
        var result = await admin.RejectAsync(accountId, request?.Reason, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? TypedResults.NoContent() : ResultProblems.From(result);
    }
}
