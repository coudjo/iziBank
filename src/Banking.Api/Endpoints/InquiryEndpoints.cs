using Banking.Api.Problems;
using Banking.Api.Security;
using Banking.Application.Errors;
using Banking.Domain.Lookups;
using Banking.Domain.Security;
using Banking.Infrastructure.Inquiry;
using Banking.Infrastructure.Security;

namespace Banking.Api.Endpoints;

/// <summary>B7-01 to B7-03: account entries and statement (plan 6.11), GL inquiry, trial balance and cash and fee reports (plan 8).</summary>
internal static class InquiryEndpoints
{
    private const string AccountArea = "ACCOUNT";
    private const string ReportArea = "REPORTS";
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    public static RouteGroupBuilder MapInquiryEndpoints(this RouteGroupBuilder api)
    {
        var accounts = api.MapGroup("/accounts").WithTags("Accounts");

        accounts.MapGet("/{accountId:long}/entries", EntriesAsync)
            .RequirePermission(ModuleCatalog.Accounts, AccountArea, PermissionAction.View)
            .SensitiveRead()
            .Produces<PagedResult<AccountEntryView>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        accounts.MapGet("/{accountId:long}/statement", StatementAsync)
            .RequirePermission(ModuleCatalog.Accounts, AccountArea, PermissionAction.View)
            .SensitiveRead()
            .Produces<AccountStatement>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        var ledgerAccounts = api.MapGroup("/ledger-accounts").WithTags("Ledger accounts");

        ledgerAccounts.MapGet("/{ledgerAccountId:long}/entries", GlEntriesAsync)
            .RequirePermission(ModuleCatalog.Ledger, ReportArea, PermissionAction.View)
            .Produces<PagedResult<LedgerEntryView>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        var reports = api.MapGroup("/reports").WithTags("Reports");

        reports.MapGet("/trial-balance", TrialBalanceAsync)
            .RequirePermission(ModuleCatalog.Ledger, ReportArea, PermissionAction.View)
            .Produces<TrialBalance>();

        reports.MapGet("/teller-cash", TellerCashAsync)
            .RequirePermission(ModuleCatalog.Ledger, ReportArea, PermissionAction.View)
            .Produces<TellerCashReport>();

        reports.MapGet("/uncollected-fees", UncollectedFeesAsync)
            .RequirePermission(ModuleCatalog.Ledger, ReportArea, PermissionAction.View)
            .Produces<UncollectedFeesReport>();

        return api;
    }

    private static async Task<IResult> EntriesAsync(
        long accountId,
        AccountHistory history,
        int page = 1,
        int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await history
            .EntriesAsync(accountId, Math.Max(1, page), Math.Clamp(pageSize, 1, MaxPageSize), cancellationToken)
            .ConfigureAwait(false);

        return result is null
            ? ApiProblems.Result(StatusCodes.Status404NotFound, ProblemCodes.NotFound)
            : TypedResults.Ok(result);
    }

    private static async Task<IResult> StatementAsync(
        long accountId,
        DateOnly from,
        DateOnly to,
        AccountHistory history,
        CancellationToken cancellationToken)
    {
        if (from > to)
        {
            return ApiProblems.Result(StatusCodes.Status400BadRequest, ProblemCodes.Validation);
        }

        var statement = await history.StatementAsync(accountId, from, to, cancellationToken).ConfigureAwait(false);
        return statement is null
            ? ApiProblems.Result(StatusCodes.Status404NotFound, ProblemCodes.NotFound)
            : TypedResults.Ok(statement);
    }

    private static async Task<IResult> GlEntriesAsync(
        long ledgerAccountId,
        int branchId,
        LedgerInquiry inquiry,
        DateOnly? from = null,
        DateOnly? to = null,
        int page = 1,
        int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        if (from is { } start && to is { } end && start > end)
        {
            return ApiProblems.Result(StatusCodes.Status400BadRequest, ProblemCodes.Validation);
        }

        var result = await inquiry
            .EntriesAsync(ledgerAccountId, branchId, from, to, Math.Max(1, page), Math.Clamp(pageSize, 1, MaxPageSize), cancellationToken)
            .ConfigureAwait(false);

        return result is null
            ? ApiProblems.Result(StatusCodes.Status404NotFound, ProblemCodes.NotFound)
            : TypedResults.Ok(result);
    }

    private static async Task<IResult> TrialBalanceAsync(
        int branchId,
        DateOnly date,
        LedgerInquiry inquiry,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await inquiry.TrialBalanceAsync(branchId, date, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> TellerCashAsync(
        int branchId,
        DateOnly date,
        CashAndFeeReports reports,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await reports.TellerCashAsync(branchId, date, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> UncollectedFeesAsync(
        int branchId,
        DateOnly date,
        CashAndFeeReports reports,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await reports.UncollectedFeesAsync(branchId, date, cancellationToken).ConfigureAwait(false));
}
