using System.Net;
using System.Net.Http.Json;
using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Application.Errors;
using Banking.Domain.Security;
using Banking.Domain.Transactions;
using Microsoft.EntityFrameworkCore;

namespace Banking.Api.IntegrationTests.Transactions;

/// <summary>B4: reversals and manual journals.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class CorrectionTests : IAsyncLifetime
{
    private readonly MsSqlContainerFixture _fixture;
    private PostingTestBed _bed = null!;
    private HttpClient _teller = null!;
    private HttpClient _corrector = null!;

    public CorrectionTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _bed = await PostingTestBed.CreateAsync(_fixture, "corrections");
        await _bed.LimitAsync(RoleGroupCatalog.Teller, TransactionLimitCodes.Transfer, 10_000m);
        await _bed.LimitAsync(RoleGroupCatalog.Teller, TransactionLimitCodes.CashDeposit, 10_000m);
        await _bed.TellerAsync("TELLR");
        _teller = _bed.Host.Client(await _bed.Host.TokenAsync("TELLR"));
        await _bed.Host.CreateStaffAsync("CORR1", canSwitchBranch: false, (_bed.HomeBranchId, RoleGroupCatalog.Supervisor));
        _corrector = _bed.Host.Client(await _bed.Host.TokenAsync("CORR1"));
    }

    public async Task DisposeAsync()
    {
        _teller.Dispose();
        _corrector.Dispose();
        await _bed.Host.DisposeAsync();
    }

    [Fact]
    public async Task A_reversal_mirrors_every_line_waits_for_approval_and_happens_once()
    {
        var (from, signers) = await _bed.AccountAsync(funding: 1_000m);
        var (to, _) = await _bed.AccountAsync(branchId: _bed.OtherBranchId);
        var original = await TransferAsync(from, to, 300m, signers);

        using (var byTeller = await Reverse(_teller, original))
        {
            byTeller.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        long requestId;
        using (var reverse = await Reverse(_corrector, original))
        {
            reverse.StatusCode.ShouldBe(HttpStatusCode.Accepted);
            requestId = (await ApiTestHost.ReadJsonAsync(reverse)).GetProperty("postingRequestId").GetInt64();
        }

        using (var twice = await Reverse(_corrector, original))
        {
            await ExpectAsync(twice, HttpStatusCode.Conflict, ProblemCodes.TransactionReversalPending);
        }

        (await _bed.BalanceAsync(to)).AvailableBalance.ShouldBe(0m);
        (await _bed.ApproveAsync(requestId)).ShouldBe(PostingRequestStatuses.Posted);

        (await _bed.BalanceAsync(from)).ClearBalance.ShouldBe(1_000m);
        (await _bed.BalanceAsync(to)).ClearBalance.ShouldBe(0m);

        await using (var context = _bed.Host.NewContext())
        {
            var first = await context.Set<PostedTransaction>().Include(row => row.AccountEntries).Include(row => row.LedgerEntries).SingleAsync(row => row.Reference == original);
            var mirror = await context.Set<PostedTransaction>().Include(row => row.AccountEntries).Include(row => row.LedgerEntries).SingleAsync(row => row.ReversesTransactionId == first.PostedTransactionId);
            mirror.LedgerEntries.Count.ShouldBe(first.LedgerEntries.Count);
            mirror.AccountEntries.Select(row => (row.AccountId, row.CrDb, row.Amount)).OrderBy(row => row.AccountId)
                .ShouldBe(first.AccountEntries.Select(row => (row.AccountId, CrDb: PostingRules.Opposite(row.CrDb), row.Amount)).OrderBy(row => row.AccountId));
            mirror.LedgerEntries.Select(row => (row.LedgerAccountId, row.BranchId, row.CrDb, row.Amount)).OrderBy(row => row).ToList()
                .ShouldBe(first.LedgerEntries.Select(row => (row.LedgerAccountId, row.BranchId, CrDb: PostingRules.Opposite(row.CrDb), row.Amount)).OrderBy(row => row).ToList());

            using (var again = await Reverse(_corrector, original))
            {
                await ExpectAsync(again, HttpStatusCode.Conflict, ProblemCodes.TransactionAlreadyReversed);
            }

            using var ofReversal = await Reverse(_corrector, mirror.Reference);
            await ExpectAsync(ofReversal, HttpStatusCode.Conflict, ProblemCodes.TransactionNotReversible);
        }
    }

    [Fact]
    public async Task A_reversal_runs_the_normal_checks_and_needs_a_reason()
    {
        var (from, signers) = await _bed.AccountAsync(funding: 500m);
        var (middle, middleSigners) = await _bed.AccountAsync();
        var (last, _) = await _bed.AccountAsync();
        var original = await TransferAsync(from, middle, 200m, signers);
        await TransferAsync(middle, last, 200m, middleSigners);

        using (var gone = await Reverse(_corrector, original))
        {
            await ExpectAsync(gone, HttpStatusCode.Conflict, ProblemCodes.AccountInsufficientFunds);
        }

        using (var noReason = await PostingTestBed.PostAsync(_corrector, "/api/v1/transactions/reversals", new { reference = original, reason = " " }))
        {
            noReason.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using var unknown = await Reverse(_corrector, "001-000000-000000");
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Cash_is_reversed_through_its_open_till_and_holds_the_close()
    {
        var (_, till) = await _bed.TellerAsync("CASHR");
        using var cashier = _bed.Host.Client(await _bed.Host.TokenAsync("CASHR"));
        var (account, _) = await _bed.AccountAsync();
        long session;
        using (var open = await cashier.PostAsync(new Uri("/api/v1/tills/open", UriKind.Relative), null))
        {
            session = (await ApiTestHost.ReadJsonAsync(open)).GetProperty("tillSessionId").GetInt64();
        }

        var deposit = await DepositAsync(cashier, account, 500m);
        long requestId;
        using (var reverse = await Reverse(_corrector, deposit))
        {
            reverse.StatusCode.ShouldBe(HttpStatusCode.Accepted);
            requestId = (await ApiTestHost.ReadJsonAsync(reverse)).GetProperty("postingRequestId").GetInt64();
        }

        using (var close = await cashier.PostAsJsonAsync($"/api/v1/tills/{session}/close", new { countedAmount = 500m }))
        {
            await ExpectAsync(close, HttpStatusCode.Conflict, ProblemCodes.TillPendingRequests);
        }

        (await _bed.ApproveAsync(requestId)).ShouldBe(PostingRequestStatuses.Posted);
        (await _bed.LedgerBalanceAsync(till)).ShouldBe(0m);
        (await _bed.BalanceAsync(account)).ClearBalance.ShouldBe(0m);

        var second = await DepositAsync(cashier, account, 200m);
        using (var close = await cashier.PostAsJsonAsync($"/api/v1/tills/{session}/close", new { countedAmount = 200m }))
        {
            close.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var late = await Reverse(_corrector, second);
        await ExpectAsync(late, HttpStatusCode.Conflict, ProblemCodes.TransactionTillClosed);
    }

    [Fact]
    public async Task A_journal_balances_per_branch_is_always_approved_and_keeps_off_cash_and_control_accounts()
    {
        var suspenseBefore = await _bed.LedgerBalanceAsync(_bed.SuspenseId);
        long requestId;
        using (var journal = await Journal(_corrector, "Correct a misposting", Gl(_bed.SuspenseId, "D", 100m), Gl(_bed.CashOverId, "C", 100m)))
        {
            journal.StatusCode.ShouldBe(HttpStatusCode.Accepted);
            requestId = (await ApiTestHost.ReadJsonAsync(journal)).GetProperty("postingRequestId").GetInt64();
        }

        (await _bed.ApproveAsync(requestId)).ShouldBe(PostingRequestStatuses.Posted);
        (await _bed.LedgerBalanceAsync(_bed.SuspenseId) - suspenseBefore).ShouldBe(100m);

        // A customer line: the engine adds the control line.
        var (account, _) = await _bed.AccountAsync();
        using (var credit = await Journal(_corrector, "Refund a fee", Gl(_bed.SuspenseId, "D", 40m), Customer(account, "C", 40m)))
        {
            requestId = (await ApiTestHost.ReadJsonAsync(credit)).GetProperty("postingRequestId").GetInt64();
        }

        (await _bed.ApproveAsync(requestId)).ShouldBe(PostingRequestStatuses.Posted);
        (await _bed.BalanceAsync(account)).ClearBalance.ShouldBe(40m);

        using (var unbalanced = await Journal(_corrector, "Off", Gl(_bed.SuspenseId, "D", 100m), Gl(_bed.CashOverId, "C", 90m)))
        {
            unbalanced.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await ApiTestHost.ReadJsonAsync(unbalanced)).GetProperty("errors").TryGetProperty("lines", out _).ShouldBeTrue();
        }

        using (var acrossBranches = await Journal(_corrector, "Off", Gl(_bed.SuspenseId, "D", 100m, _bed.OtherBranchId), Gl(_bed.CashOverId, "C", 100m)))
        {
            acrossBranches.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using (var vault = await Journal(_corrector, "Off", Gl(_bed.VaultId, "D", 10m), Gl(_bed.SuspenseId, "C", 10m)))
        {
            await ExpectAsync(vault, HttpStatusCode.Conflict, ProblemCodes.JournalAccountNotAllowed);
        }

        using (var control = await Journal(_corrector, "Off", Gl(_bed.ControlId, "D", 10m), Gl(_bed.SuspenseId, "C", 10m)))
        {
            await ExpectAsync(control, HttpStatusCode.Conflict, ProblemCodes.JournalAccountNotAllowed);
        }

        var (_, till) = await _bed.TellerAsync("TILLJ");
        using (var cash = await Journal(_corrector, "Off", Gl(till, "D", 10m), Gl(_bed.SuspenseId, "C", 10m)))
        {
            await ExpectAsync(cash, HttpStatusCode.Conflict, ProblemCodes.JournalAccountNotAllowed);
        }

        using (var both = await Journal(_corrector, "Off", new { ledgerAccountId = _bed.SuspenseId, accountId = account, crDb = "D", amount = 5m }, Gl(_bed.CashOverId, "C", 5m)))
        {
            both.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using var byTeller = await Journal(_teller, "Off", Gl(_bed.SuspenseId, "D", 1m), Gl(_bed.CashOverId, "C", 1m));
        byTeller.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ---------------------------------------------------------------- Helpers

    private static object Gl(long ledgerAccountId, string side, decimal amount, int? branchId = null) =>
        new { ledgerAccountId, branchId, crDb = side, amount };

    private static object Customer(long accountId, string side, decimal amount) =>
        new { accountId, crDb = side, amount };

    private static Task<HttpResponseMessage> Journal(HttpClient client, string narration, params object[] lines) =>
        PostingTestBed.PostAsync(client, "/api/v1/transactions/journals", new { narration, lines });

    private static Task<HttpResponseMessage> Reverse(HttpClient client, string reference) =>
        PostingTestBed.PostAsync(client, "/api/v1/transactions/reversals", new { reference, reason = "Posted to the wrong account" });

    private async Task<string> TransferAsync(long from, long to, decimal amount, long[] signers)
    {
        using var transfer = await PostingTestBed.PostAsync(
            _teller,
            "/api/v1/transactions/transfers",
            new { fromAccountId = from, toAccountId = to, amount, narration = "Test", confirmedSignerPartyIds = signers });
        transfer.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await ApiTestHost.ReadJsonAsync(transfer)).GetProperty("reference").GetString()!;
    }

    private static async Task<string> DepositAsync(HttpClient cashier, long account, decimal amount)
    {
        using var deposit = await PostingTestBed.PostAsync(
            cashier,
            "/api/v1/transactions/cash-deposits",
            new { accountId = account, amount, narration = "Counter", byHolder = true });
        deposit.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await ApiTestHost.ReadJsonAsync(deposit)).GetProperty("reference").GetString()!;
    }

    private static async Task ExpectAsync(HttpResponseMessage response, HttpStatusCode status, string problem)
    {
        response.StatusCode.ShouldBe(status);
        (await ApiTestHost.ProblemTypeAsync(response)).ShouldBe(problem);
    }
}
