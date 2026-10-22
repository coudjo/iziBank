using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Application.Errors;
using Banking.Domain.Lookups;
using Banking.Domain.Security;
using Banking.Domain.Transactions;
using Microsoft.EntityFrameworkCore;

namespace Banking.Api.IntegrationTests.Transactions;

/// <summary>B2: tills, the vault, cash deposits and withdrawals, and the count at close.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class TillAndCashTests : IAsyncLifetime
{
    private const decimal Threshold = 10_000m;

    private readonly MsSqlContainerFixture _fixture;
    private PostingTestBed _bed = null!;
    private HttpClient _supervisor = null!;

    public TillAndCashTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _bed = await PostingTestBed.CreateAsync(
            _fixture,
            "cash",
            new Dictionary<string, string?> { ["Transactions:CashTransactionReportThreshold"] = "10000" });
        await _bed.LimitAsync(RoleGroupCatalog.Teller, TransactionLimitCodes.CashDeposit, 50_000m);
        await _bed.LimitAsync(RoleGroupCatalog.Teller, TransactionLimitCodes.CashWithdrawal, 2_000m);
        await _bed.FundVaultAsync(100_000m);
        await _bed.Host.CreateStaffAsync("VAULT1", canSwitchBranch: false, (_bed.HomeBranchId, RoleGroupCatalog.Supervisor));
        _supervisor = _bed.Host.Client(await _bed.Host.TokenAsync("VAULT1"));
    }

    public async Task DisposeAsync()
    {
        _supervisor.Dispose();
        await _bed.Host.DisposeAsync();
    }

    [Fact]
    public async Task A_teller_day_open_buy_take_pay_count_and_close_with_a_shortage()
    {
        var (_, till) = await _bed.TellerAsync("CASH1");
        using var teller = _bed.Host.Client(await _bed.Host.TokenAsync("CASH1"));
        var (account, signers) = await _bed.AccountAsync();

        var session = await OpenAsync(teller);
        using (var again = await teller.PostAsync(new Uri("/api/v1/tills/open", UriKind.Relative), null))
        {
            await ExpectAsync(again, HttpStatusCode.Conflict, ProblemCodes.TillAlreadyOpen);
        }

        // Cash from the vault waits for a vault supervisor.
        using (var buy = await PostingTestBed.PostAsync(teller, $"/api/v1/tills/{session}/buy", new { amount = 10_000m }))
        {
            buy.StatusCode.ShouldBe(HttpStatusCode.Accepted);
            await ApproveAsync(buy);
        }

        (await _bed.LedgerBalanceAsync(till)).ShouldBe(10_000m);

        using (var deposit = await PostingTestBed.PostAsync(teller, "/api/v1/transactions/cash-deposits", Deposit(account, 500m)))
        {
            deposit.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        using (var withdrawal = await PostingTestBed.PostAsync(teller, "/api/v1/transactions/cash-withdrawals", Withdrawal(account, 200m, signers)))
        {
            withdrawal.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        (await _bed.LedgerBalanceAsync(till)).ShouldBe(10_300m);
        (await _bed.BalanceAsync(account)).ClearBalance.ShouldBe(300m);

        // Counted 10 short: the close posting waits, and no cash can move meanwhile.
        using (var close = await teller.PostAsJsonAsync($"/api/v1/tills/{session}/close", new { countedAmount = 10_290m }))
        {
            close.StatusCode.ShouldBe(HttpStatusCode.OK);
            var outcome = await ApiTestHost.ReadJsonAsync(close);
            outcome.GetProperty("session").GetProperty("status").GetString().ShouldBe(TillSessionStatuses.Closing);
            outcome.GetProperty("session").GetProperty("expectedAmount").GetDecimal().ShouldBe(10_300m);

            using (var blocked = await PostingTestBed.PostAsync(teller, "/api/v1/transactions/cash-deposits", Deposit(account, 50m)))
            {
                await ExpectAsync(blocked, HttpStatusCode.Conflict, ProblemCodes.TillNotOpen);
            }

            var requestId = outcome.GetProperty("posting").GetProperty("postingRequestId").GetInt64();
            using var approve = await _supervisor.PostAsync(new Uri($"/api/v1/transaction-requests/{requestId}/approve", UriKind.Relative), null);
            approve.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await _bed.LedgerBalanceAsync(till)).ShouldBe(0m);
        (await _bed.LedgerBalanceAsync(_bed.CashShortId)).ShouldBe(10m);
        using var mine = await teller.GetAsync(new Uri("/api/v1/tills/mine", UriKind.Relative));
        var view = await ApiTestHost.ReadJsonAsync(mine);
        view.GetProperty("hasTillAccount").GetBoolean().ShouldBeTrue();
        view.GetProperty("session").ValueKind.ShouldBe(JsonValueKind.Null);

        await using var context = _bed.Host.NewContext();
        var stored = await context.Set<TillSession>().SingleAsync(row => row.TillSessionId == session);
        stored.Status.ShouldBe(TillSessionStatuses.Closed);
        stored.CountedAmount.ShouldBe(10_290m);
    }

    [Fact]
    public async Task A_rejected_close_reopens_the_till_and_an_overage_goes_to_cash_over()
    {
        var (_, till) = await _bed.TellerAsync("CASH2");
        using var teller = _bed.Host.Client(await _bed.Host.TokenAsync("CASH2"));
        var session = await OpenAsync(teller);
        using (var buy = await PostingTestBed.PostAsync(teller, $"/api/v1/tills/{session}/buy", new { amount = 1_000m }))
        {
            await ApproveAsync(buy);
        }

        long rejected;
        using (var close = await teller.PostAsJsonAsync($"/api/v1/tills/{session}/close", new { countedAmount = 1_005m }))
        {
            rejected = (await ApiTestHost.ReadJsonAsync(close)).GetProperty("posting").GetProperty("postingRequestId").GetInt64();
        }

        using (var reject = await _supervisor.PostAsJsonAsync($"/api/v1/transaction-requests/{rejected}/reject", new { reason = "Count again" }))
        {
            reject.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        await using (var context = _bed.Host.NewContext())
        {
            (await context.Set<TillSession>().SingleAsync(row => row.TillSessionId == session)).Status.ShouldBe(TillSessionStatuses.Open);
        }

        var cashOverBefore = await _bed.LedgerBalanceAsync(_bed.CashOverId);
        using (var close = await teller.PostAsJsonAsync($"/api/v1/tills/{session}/close", new { countedAmount = 1_005m }))
        {
            var requestId = (await ApiTestHost.ReadJsonAsync(close)).GetProperty("posting").GetProperty("postingRequestId").GetInt64();
            (await _bed.ApproveAsync(requestId)).ShouldBe(PostingRequestStatuses.Posted);
        }

        (await _bed.LedgerBalanceAsync(till)).ShouldBe(0m);
        (await _bed.LedgerBalanceAsync(_bed.CashOverId) - cashOverBefore).ShouldBe(-5m);
    }

    [Fact]
    public async Task Cash_rules_tills_vault_and_third_parties()
    {
        await _bed.Host.CreateStaffAsync("NOTILL", canSwitchBranch: false, (_bed.HomeBranchId, RoleGroupCatalog.Teller));
        using (var noTill = _bed.Host.Client(await _bed.Host.TokenAsync("NOTILL")))
        {
            using var open = await noTill.PostAsync(new Uri("/api/v1/tills/open", UriKind.Relative), null);
            await ExpectAsync(open, HttpStatusCode.Conflict, ProblemCodes.TillNotAssigned);
        }

        await _bed.TellerAsync("CASH3");
        using var teller = _bed.Host.Client(await _bed.Host.TokenAsync("CASH3"));
        var (account, signers) = await _bed.AccountAsync(funding: 5_000m);

        using (var closed = await PostingTestBed.PostAsync(teller, "/api/v1/transactions/cash-deposits", Deposit(account, 100m)))
        {
            await ExpectAsync(closed, HttpStatusCode.Conflict, ProblemCodes.TillNotOpen);
        }

        var session = await OpenAsync(teller);

        // The vault cannot give more than it holds; the till cannot pay more than it holds.
        using (var tooMuch = await PostingTestBed.PostAsync(teller, $"/api/v1/tills/{session}/buy", new { amount = 999_999m }))
        {
            await ExpectAsync(tooMuch, HttpStatusCode.Conflict, ProblemCodes.VaultInsufficientCash);
        }

        using (var empty = await PostingTestBed.PostAsync(teller, "/api/v1/transactions/cash-withdrawals", Withdrawal(account, 100m, signers)))
        {
            await ExpectAsync(empty, HttpStatusCode.Conflict, ProblemCodes.TillInsufficientCash);
        }

        // Someone else depositing gives a name and mobile, and an ID at or above the threshold.
        using (var anonymous = await PostingTestBed.PostAsync(teller, "/api/v1/transactions/cash-deposits", Deposit(account, 100m, byHolder: false)))
        {
            anonymous.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            var errors = (await ApiTestHost.ReadJsonAsync(anonymous)).GetProperty("errors");
            errors.TryGetProperty("depositor.name", out _).ShouldBeTrue();
            errors.TryGetProperty("depositor.mobile", out _).ShouldBeTrue();
        }

        var depositor = new { name = "Yaw Third", mobile = "0207654321", identificationTypeId = (int?)null, identificationNumber = (string?)null };
        using (var large = await PostingTestBed.PostAsync(teller, "/api/v1/transactions/cash-deposits", Deposit(account, Threshold, byHolder: false, depositor)))
        {
            large.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await ApiTestHost.ReadJsonAsync(large)).GetProperty("errors").TryGetProperty("depositor.identificationNumber", out _).ShouldBeTrue();
        }

        using (var small = await PostingTestBed.PostAsync(teller, "/api/v1/transactions/cash-deposits", Deposit(account, 100m, byHolder: false, depositor)))
        {
            small.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        // A withdrawal needs the mandate.
        using (var unsigned = await PostingTestBed.PostAsync(teller, "/api/v1/transactions/cash-withdrawals", Withdrawal(account, 50m, [])))
        {
            unsigned.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using var signerView = await teller.GetAsync(new Uri($"/api/v1/accounts/{account}/signers", UriKind.Relative));
        var signer = (await ApiTestHost.ReadJsonAsync(signerView)).GetProperty("signers").EnumerateArray().Single();
        signer.GetProperty("partyId").GetInt64().ShouldBe(signers[0]);
        signer.GetProperty("effectiveKycStatusCode").GetString().ShouldBe(LookupCodes.KycStatuses.Verified);
        signer.GetProperty("hasEffectiveSignature").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Pending_cash_requests_block_the_close_and_withdrawals_over_the_limit_wait()
    {
        var (_, till) = await _bed.TellerAsync("CASH4");
        using var teller = _bed.Host.Client(await _bed.Host.TokenAsync("CASH4"));
        var (account, signers) = await _bed.AccountAsync(funding: 9_000m);
        var session = await OpenAsync(teller);
        using (var buy = await PostingTestBed.PostAsync(teller, $"/api/v1/tills/{session}/buy", new { amount = 5_000m }))
        {
            await ApproveAsync(buy);
        }

        long pending;
        using (var large = await PostingTestBed.PostAsync(teller, "/api/v1/transactions/cash-withdrawals", Withdrawal(account, 3_000m, signers)))
        {
            large.StatusCode.ShouldBe(HttpStatusCode.Accepted);
            pending = (await ApiTestHost.ReadJsonAsync(large)).GetProperty("postingRequestId").GetInt64();
        }

        (await _bed.BalanceAsync(account)).AvailableBalance.ShouldBe(6_000m);
        (await _bed.LedgerBalanceAsync(till)).ShouldBe(5_000m);

        using (var close = await teller.PostAsJsonAsync($"/api/v1/tills/{session}/close", new { countedAmount = 5_000m }))
        {
            await ExpectAsync(close, HttpStatusCode.Conflict, ProblemCodes.TillPendingRequests);
        }

        (await _bed.ApproveAsync(pending)).ShouldBe(PostingRequestStatuses.Posted);
        (await _bed.LedgerBalanceAsync(till)).ShouldBe(2_000m);
        (await _bed.BalanceAsync(account)).ClearBalance.ShouldBe(6_000m);
    }

    [Fact]
    public async Task Cash_for_an_account_in_another_branch_balances_both_branches()
    {
        var (_, till) = await _bed.TellerAsync("CASH5");
        using var teller = _bed.Host.Client(await _bed.Host.TokenAsync("CASH5"));
        var (account, _) = await _bed.AccountAsync(branchId: _bed.OtherBranchId);
        await OpenAsync(teller);

        string reference;
        using (var deposit = await PostingTestBed.PostAsync(teller, "/api/v1/transactions/cash-deposits", Deposit(account, 700m)))
        {
            deposit.StatusCode.ShouldBe(HttpStatusCode.Created);
            reference = (await ApiTestHost.ReadJsonAsync(deposit)).GetProperty("reference").GetString()!;
        }

        await using var context = _bed.Host.NewContext();
        var posted = await context.Set<PostedTransaction>().Include(row => row.LedgerEntries).SingleAsync(row => row.Reference == reference);
        posted.LedgerEntries.Count.ShouldBe(4);
        PostingRules.UnbalancedBranches(posted.LedgerEntries.Select(row => new LedgerLine(row.LedgerAccountId, row.BranchId, row.CrDb, row.Amount))).ShouldBeEmpty();
        (await _bed.LedgerBalanceAsync(till)).ShouldBe(700m);
        (await _bed.BalanceAsync(account)).ClearBalance.ShouldBe(700m);
    }

    // ---------------------------------------------------------------- Helpers

    private static object Deposit(long account, decimal amount, bool byHolder = true, object? depositor = null) =>
        new { accountId = account, amount, narration = "Counter", byHolder, depositor };

    private static object Withdrawal(long account, decimal amount, long[] signers) =>
        new { accountId = account, amount, narration = "Counter", confirmedSignerPartyIds = signers, toHolder = true };

    private static async Task<long> OpenAsync(HttpClient teller)
    {
        using var open = await teller.PostAsync(new Uri("/api/v1/tills/open", UriKind.Relative), null);
        open.StatusCode.ShouldBe(HttpStatusCode.Created);
        var session = await ApiTestHost.ReadJsonAsync(open);
        session.GetProperty("balance").GetDecimal().ShouldBe(0m);
        return session.GetProperty("tillSessionId").GetInt64();
    }

    private async Task ApproveAsync(HttpResponseMessage pending)
    {
        pending.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var requestId = (await ApiTestHost.ReadJsonAsync(pending)).GetProperty("postingRequestId").GetInt64();
        using var approve = await _supervisor.PostAsync(new Uri($"/api/v1/transaction-requests/{requestId}/approve", UriKind.Relative), null);
        approve.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task ExpectAsync(HttpResponseMessage response, HttpStatusCode status, string problem)
    {
        response.StatusCode.ShouldBe(status);
        (await ApiTestHost.ProblemTypeAsync(response)).ShouldBe(problem);
    }
}
