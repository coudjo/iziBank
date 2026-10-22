using System.Text.Json;
using Banking.Api.Problems;
using Banking.Api.Security;
using Banking.Application.Abstractions;
using Banking.Application.Errors;
using Banking.Domain.Lookups;
using Banking.Domain.Security;
using Banking.Domain.Transactions;
using Banking.Infrastructure.Security;
using Banking.Infrastructure.Transactions;

namespace Banking.Api.Endpoints;

internal sealed record TransferRequest(
    long? FromAccountId,
    long? ToAccountId,
    decimal? Amount,
    string? Narration,
    IReadOnlyList<long>? ConfirmedSignerPartyIds);

/// <summary>
/// B1 and B3: postings and their approval (plan B section 8). A posting answers 201 when it posted and 202 when it
/// waits for approval; every posting needs an <c>Idempotency-Key</c>, so a retry after a timeout never posts twice.
/// The request endpoints are open to any signed-in user because who may approve depends on the posting's kind; the
/// engine checks supervise on that kind's sub-module, the branch and four-eyes (ADR 0003, B-9).
/// </summary>
internal static class TransactionEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static RouteGroupBuilder MapTransactionEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/transactions/transfers", TransferAsync)
            .WithTags("Transactions")
            .RequirePermission(ModuleCatalog.Transactions, "TRANSFER", PermissionAction.Add)
            .Produces<PostingOutcome>(StatusCodes.Status201Created)
            .Produces<PostingOutcome>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        var requests = api.MapGroup("/transaction-requests").WithTags("Transactions");

        requests.MapGet("/", ListAsync)
            .AuthenticatedOnly()
            .Produces<IReadOnlyList<PostingRequestView>>();

        requests.MapGet("/{postingRequestId:long}", GetAsync)
            .AuthenticatedOnly()
            .Produces<PostingRequestView>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        requests.MapPost("/{postingRequestId:long}/approve", ApproveAsync)
            .AuthenticatedOnly()
            .Produces<PostingOutcome>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        requests.MapPost("/{postingRequestId:long}/reject", RejectAsync)
            .AuthenticatedOnly()
            .Produces<PostingOutcome>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        requests.MapPost("/{postingRequestId:long}/cancel", CancelAsync)
            .AuthenticatedOnly()
            .Produces<PostingOutcome>()
            .ProducesProblem(StatusCodes.Status409Conflict);

        return api;
    }

    private static Task<IResult> TransferAsync(
        TransferRequest request,
        HttpRequest http,
        Transfers transfers,
        IdempotencyStore idempotency,
        CancellationToken cancellationToken) =>
        PostingAsync(
            http,
            idempotency,
            request,
            () => transfers.TransferAsync(
                new TransferDraft(request.FromAccountId, request.ToAccountId, request.Amount, request.Narration, request.ConfirmedSignerPartyIds),
                cancellationToken),
            cancellationToken);

    /// <summary>Requires the idempotency key, then stores the outcome so a retry gets the same answer.</summary>
    internal static async Task<IResult> PostingAsync<TRequest>(
        HttpRequest http,
        IdempotencyStore idempotency,
        TRequest request,
        Func<Task<OperationResult<PostingOutcome>>> post,
        CancellationToken cancellationToken)
    {
        if (!http.Headers.ContainsKey(IdempotentRequest.Header))
        {
            return ApiProblems.Result(StatusCodes.Status400BadRequest, ProblemCodes.IdempotencyKeyRequired);
        }

        return await IdempotentRequest.RunAsync(
                http,
                idempotency,
                JsonSerializer.SerializeToUtf8Bytes(request, Json),
                async () =>
                {
                    var result = await post().ConfigureAwait(false);
                    if (!result.Succeeded)
                    {
                        return IdempotentOutcome.DoNotStore(ResultProblems.From(result));
                    }

                    var status = result.Value!.Status == PostingRequestStatuses.Posted ? StatusCodes.Status201Created : StatusCodes.Status202Accepted;
                    return IdempotentOutcome.Store(status, result.Value);
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<IResult> ListAsync(
        PostingEngine engine,
        ICurrentUser currentUser,
        string? status = null,
        CancellationToken cancellationToken = default) =>
        TypedResults.Ok(await engine.ListAsync(Actor(currentUser), status, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> GetAsync(long postingRequestId, PostingEngine engine, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var view = await engine.GetAsync(Actor(currentUser), postingRequestId, cancellationToken).ConfigureAwait(false);
        return view is null ? ApiProblems.Result(StatusCodes.Status404NotFound, ProblemCodes.NotFound) : TypedResults.Ok(view);
    }

    private static async Task<IResult> ApproveAsync(long postingRequestId, PostingEngine engine, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        Outcome(await engine.ApproveAsync(Actor(currentUser), postingRequestId, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> RejectAsync(
        long postingRequestId,
        RejectRequest request,
        PostingEngine engine,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return ApiProblems.Validation(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["reason"] = ["Give a reason for rejecting this request."]
            });
        }

        return Outcome(await engine.RejectAsync(Actor(currentUser), postingRequestId, request.Reason, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<IResult> CancelAsync(long postingRequestId, PostingEngine engine, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        Outcome(await engine.CancelAsync(Actor(currentUser), postingRequestId, cancellationToken).ConfigureAwait(false));

    private static IResult Outcome(OperationResult<PostingOutcome> result) =>
        result.Succeeded ? TypedResults.Ok(result.Value) : ResultProblems.From(result);

    private static PostingActor Actor(ICurrentUser currentUser) =>
        new(
            currentUser.StaffUserId ?? throw new InvalidOperationException("Postings need a signed-in user."),
            currentUser.BranchId ?? throw new InvalidOperationException("Postings need a working branch."));
}
