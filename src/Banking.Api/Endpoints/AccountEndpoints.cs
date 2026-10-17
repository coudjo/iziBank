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

/// <summary>A5-04 and A5-05: open, read, approve and reject accounts; read the balance.</summary>
internal static class AccountEndpoints
{
    private const string AccountArea = "ACCOUNT";
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

        return api;
    }

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
