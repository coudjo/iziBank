using System.Text.Json;
using Banking.Api.Problems;
using Banking.Api.Security;
using Banking.Application.Abstractions;
using Banking.Application.Errors;
using Banking.Domain.Charges;
using Banking.Domain.Lookups;
using Banking.Domain.Security;
using Banking.Domain.Transactions;
using Banking.Infrastructure.Charges;
using Banking.Infrastructure.Security;
using Banking.Infrastructure.Transactions;

namespace Banking.Api.Endpoints;

internal sealed record ChargeQuoteRequest(long? AccountId, string? Kind, decimal? Amount);

/// <summary>B5-01: the charge on a transaction. Amount is what the account would pay; zero when waived or none applies.</summary>
internal sealed record ChargeQuoteView(long? ProductChargeId, string? ChargeCode, decimal Amount, decimal Arrear, bool Waived);

internal sealed record CashDepositRequest(long? AccountId, decimal? Amount, string? Narration, bool? ByHolder, CashPerson? Depositor);

internal sealed record CashWithdrawalRequest(
    long? AccountId,
    decimal? Amount,
    string? Narration,
    IReadOnlyList<long>? ConfirmedSignerPartyIds,
    bool? ToHolder,
    CashPerson? Recipient);

internal sealed record ReversalRequest(string? Reference, string? Reason);

internal sealed record JournalLineRequest(long? LedgerAccountId, long? AccountId, int? BranchId, string? CrDb, decimal? Amount);

internal sealed record JournalRequest(string? Narration, IReadOnlyList<JournalLineRequest>? Lines);

internal sealed record TillAmountRequest(decimal? Amount);

internal sealed record TillCloseRequest(decimal? CountedAmount);

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

        api.MapPost("/transactions/cash-deposits", DepositAsync)
            .WithTags("Transactions")
            .RequirePermission(ModuleCatalog.Transactions, "CASH", PermissionAction.Add)
            .Produces<PostingOutcome>(StatusCodes.Status201Created)
            .Produces<PostingOutcome>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        api.MapPost("/transactions/cash-withdrawals", WithdrawAsync)
            .WithTags("Transactions")
            .RequirePermission(ModuleCatalog.Transactions, "CASH", PermissionAction.Add)
            .Produces<PostingOutcome>(StatusCodes.Status201Created)
            .Produces<PostingOutcome>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        api.MapPost("/transactions/reversals", ReverseAsync)
            .WithTags("Transactions")
            .RequirePermission(ModuleCatalog.Transactions, "REVERSAL", PermissionAction.Add)
            .Produces<PostingOutcome>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        api.MapPost("/transactions/journals", JournalAsync)
            .WithTags("Transactions")
            .RequirePermission(ModuleCatalog.Ledger, "JOURNAL", PermissionAction.Add)
            .Produces<PostingOutcome>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        api.MapGet("/accounts/{accountId:long}/signers", SignersAsync)
            .WithTags("Transactions")
            .RequirePermission(ModuleCatalog.Accounts, "ACCOUNT", PermissionAction.View)
            .Produces<AccountSignersView>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPost("/transactions/quote", QuoteAsync)
            .WithTags("Transactions")
            .RequirePermission(ModuleCatalog.Transactions, "CASH", PermissionAction.View)
            .Produces<ChargeQuoteView>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        var tills = api.MapGroup("/tills").WithTags("Tills");

        tills.MapGet("/mine", MyTillAsync)
            .RequirePermission(ModuleCatalog.Transactions, "TILL", PermissionAction.View)
            .Produces<MyTillView>();

        tills.MapPost("/open", OpenTillAsync)
            .RequirePermission(ModuleCatalog.Transactions, "TILL", PermissionAction.Add)
            .Produces<TillSessionView>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict);

        tills.MapPost("/{tillSessionId:long}/buy", BuyAsync)
            .RequirePermission(ModuleCatalog.Transactions, "TILL", PermissionAction.Add)
            .Produces<PostingOutcome>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        tills.MapPost("/{tillSessionId:long}/sell", SellAsync)
            .RequirePermission(ModuleCatalog.Transactions, "TILL", PermissionAction.Add)
            .Produces<PostingOutcome>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        tills.MapPost("/{tillSessionId:long}/close", CloseTillAsync)
            .RequirePermission(ModuleCatalog.Transactions, "TILL", PermissionAction.Add)
            .Produces<TillCloseOutcome>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        api.MapGet("/branches/{branchId:int}/tills", BranchTillsAsync)
            .WithTags("Tills")
            .RequirePermission(ModuleCatalog.Transactions, "VAULT", PermissionAction.View)
            .Produces<BranchTillsView>();

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

    private static Task<IResult> DepositAsync(
        CashDepositRequest request,
        HttpRequest http,
        CashPostings cash,
        IdempotencyStore idempotency,
        CancellationToken cancellationToken) =>
        PostingAsync(
            http,
            idempotency,
            request,
            () => cash.DepositAsync(new CashDepositDraft(request.AccountId, request.Amount, request.Narration, request.ByHolder, request.Depositor), cancellationToken),
            cancellationToken);

    private static Task<IResult> WithdrawAsync(
        CashWithdrawalRequest request,
        HttpRequest http,
        CashPostings cash,
        IdempotencyStore idempotency,
        CancellationToken cancellationToken) =>
        PostingAsync(
            http,
            idempotency,
            request,
            () => cash.WithdrawAsync(
                new CashWithdrawalDraft(request.AccountId, request.Amount, request.Narration, request.ConfirmedSignerPartyIds, request.ToHolder, request.Recipient),
                cancellationToken),
            cancellationToken);

    private static Task<IResult> ReverseAsync(
        ReversalRequest request,
        HttpRequest http,
        Reversals reversals,
        IdempotencyStore idempotency,
        CancellationToken cancellationToken) =>
        PostingAsync(
            http,
            idempotency,
            request,
            () => reversals.ReverseAsync(new ReversalDraft(request.Reference, request.Reason), cancellationToken),
            cancellationToken);

    private static Task<IResult> JournalAsync(
        JournalRequest request,
        HttpRequest http,
        Journals journals,
        IdempotencyStore idempotency,
        CancellationToken cancellationToken) =>
        PostingAsync(
            http,
            idempotency,
            request,
            () => journals.PostAsync(
                new JournalDraft(
                    request.Narration,
                    request.Lines?.Select(static line => new JournalLineDraft(line.LedgerAccountId, line.AccountId, line.BranchId, line.CrDb, line.Amount)).ToList()),
                cancellationToken),
            cancellationToken);

    /// <summary>B5-01: what the charge would be for a withdrawal or transfer of this amount, without posting anything.</summary>
    private static async Task<IResult> QuoteAsync(ChargeQuoteRequest request, ChargeQuoter quoter, CancellationToken cancellationToken)
    {
        var condition = ChargeQuoteKinds.ConditionFor(request.Kind);
        if (request.AccountId is null || request.Amount is not > 0m || condition is null)
        {
            var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
            if (request.AccountId is null)
            {
                errors["accountId"] = ["Choose the account."];
            }

            if (request.Amount is not > 0m)
            {
                errors["amount"] = ["Enter an amount above zero."];
            }

            if (condition is null)
            {
                errors["kind"] = ["Choose CASH_WITHDRAWAL or TRANSFER."];
            }

            return ResultProblems.From(OperationResult.Fail<ChargeQuoteView>(ProblemCodes.Validation, errors));
        }

        var quote = await quoter.QuoteAsync(request.AccountId.Value, condition, request.Amount.Value, cancellationToken).ConfigureAwait(false);
        if (quote.ProblemCode is not null)
        {
            return ApiProblems.Result(quote.ProblemCode == ProblemCodes.NotFound ? StatusCodes.Status404NotFound : StatusCodes.Status409Conflict, quote.ProblemCode);
        }

        return TypedResults.Ok(new ChargeQuoteView(quote.ProductChargeId, quote.ChargeCode, quote.Amount, quote.Arrear, quote.Waived));
    }

    private static async Task<IResult> SignersAsync(long accountId, CashPostings cash, CancellationToken cancellationToken)
    {
        var view = await cash.SignersAsync(accountId, cancellationToken).ConfigureAwait(false);
        return view is null ? ApiProblems.Result(StatusCodes.Status404NotFound, ProblemCodes.NotFound) : TypedResults.Ok(view);
    }

    private static async Task<IResult> MyTillAsync(Tills tills, CancellationToken cancellationToken) =>
        TypedResults.Ok(await tills.MineAsync(cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> OpenTillAsync(Tills tills, CancellationToken cancellationToken)
    {
        var result = await tills.OpenAsync(cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? TypedResults.Created((string?)null, result.Value) : ResultProblems.From(result);
    }

    private static Task<IResult> BuyAsync(long tillSessionId, TillAmountRequest request, HttpRequest http, Tills tills, IdempotencyStore idempotency, CancellationToken cancellationToken) =>
        PostingAsync(http, idempotency, request, () => tills.BuyAsync(tillSessionId, request.Amount, cancellationToken), cancellationToken);

    private static Task<IResult> SellAsync(long tillSessionId, TillAmountRequest request, HttpRequest http, Tills tills, IdempotencyStore idempotency, CancellationToken cancellationToken) =>
        PostingAsync(http, idempotency, request, () => tills.SellAsync(tillSessionId, request.Amount, cancellationToken), cancellationToken);

    private static async Task<IResult> CloseTillAsync(long tillSessionId, TillCloseRequest request, Tills tills, CancellationToken cancellationToken)
    {
        var result = await tills.CloseAsync(tillSessionId, request.CountedAmount, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? TypedResults.Ok(result.Value) : ResultProblems.From(result);
    }

    private static async Task<IResult> BranchTillsAsync(int branchId, Tills tills, CancellationToken cancellationToken) =>
        TypedResults.Ok(await tills.BranchAsync(branchId, cancellationToken).ConfigureAwait(false));

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
