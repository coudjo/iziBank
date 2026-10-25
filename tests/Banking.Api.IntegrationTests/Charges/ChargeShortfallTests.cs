using System.Net;
using System.Net.Http.Json;
using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Api.IntegrationTests.Transactions;
using Banking.Domain.Charges;
using Banking.Domain.Lookups;
using Banking.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Banking.Api.IntegrationTests.Charges;

/// <summary>B5-02: the shortfall policies on a withdrawal, and arrears collected from a later credit.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class ChargeShortfallTests : IAsyncLifetime
{
    private readonly MsSqlContainerFixture _fixture;
    private PostingTestBed _bed = null!;
    private HttpClient _accountant = null!;
    private HttpClient _accountant2 = null!;
    private HttpClient _supervisor = null!;
    private HttpClient _teller = null!;

    public ChargeShortfallTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _bed = await PostingTestBed.CreateAsync(_fixture, "chargeshortfall");
        await _bed.LimitAsync(RoleGroupCatalog.Teller, TransactionLimitCodes.CashWithdrawal, 2_000m);
        await _bed.LimitAsync(RoleGroupCatalog.Teller, TransactionLimitCodes.CashDeposit, 50_000m);
        await _bed.FundVaultAsync(100_000m);
        await _bed.Host.CreateStaffAsync("SFA1", canSwitchBranch: false, (_bed.HomeBranchId, RoleGroupCatalog.Accountant));
        await _bed.Host.CreateStaffAsync("SFA2", canSwitchBranch: false, (_bed.HomeBranchId, RoleGroupCatalog.Accountant));
        await _bed.Host.CreateStaffAsync("SFVS", canSwitchBranch: false, (_bed.HomeBranchId, RoleGroupCatalog.Supervisor));
        _accountant = _bed.Host.Client(await _bed.Host.TokenAsync("SFA1"));
        _accountant2 = _bed.Host.Client(await _bed.Host.TokenAsync("SFA2"));
        _supervisor = _bed.Host.Client(await _bed.Host.TokenAsync("SFVS"));
        await _bed.TellerAsync("SFT");
        _teller = _bed.Host.Client(await _bed.Host.TokenAsync("SFT"));
    }

    public async Task DisposeAsync()
    {
        _accountant.Dispose();
        _accountant2.Dispose();
        _supervisor.Dispose();
        _teller.Dispose();
        await _bed.Host.DisposeAsync();
    }

    [Fact]
    public async Task Defer_charges_what_the_balance_allows_and_a_later_credit_collects_the_rest()
    {
        await ApprovedChargeAsync("WDLFEE", LookupCodes.ChargeShortfallPolicies.Defer, amount: 10m);
        await OpenTillWithCashAsync(10_000m);
        var (account, signers) = await _bed.AccountAsync(55m);

        // 55 less the 50 withdrawn leaves 5: the charge takes 5 and the other 5 is an arrear.
        using (var withdrawal = await PostingTestBed.PostAsync(_teller, "/api/v1/transactions/cash-withdrawals", Withdrawal(account, 50m, signers)))
        {
            withdrawal.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        (await _bed.BalanceAsync(account)).ClearBalance.ShouldBe(0m);
        (await _bed.LedgerBalanceAsync(_bed.ChargeIncomeId)).ShouldBe(-5m);
        (await ArrearsAsync(account)).ShouldBe((5m, 1));

        // A deposit of 20 collects the arrear first: the account ends with 20 less the 5 collected.
        using (var deposit = await PostingTestBed.PostAsync(_teller, "/api/v1/transactions/cash-deposits", Deposit(account, 20m)))
        {
            deposit.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        (await _bed.BalanceAsync(account)).ClearBalance.ShouldBe(15m);
        (await _bed.LedgerBalanceAsync(_bed.ChargeIncomeId)).ShouldBe(-10m);
        (await ArrearsAsync(account)).ShouldBe((0m, 1));
    }

    [Fact]
    public async Task A_credit_collects_no_more_than_it_adds()
    {
        await ApprovedChargeAsync("WDLFEE", LookupCodes.ChargeShortfallPolicies.Defer, amount: 10m);
        await OpenTillWithCashAsync(10_000m);
        var (account, signers) = await _bed.AccountAsync(55m);
        using (var withdrawal = await PostingTestBed.PostAsync(_teller, "/api/v1/transactions/cash-withdrawals", Withdrawal(account, 50m, signers)))
        {
            withdrawal.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        // Three of the five owed are collected; two stay owed.
        using (var deposit = await PostingTestBed.PostAsync(_teller, "/api/v1/transactions/cash-deposits", Deposit(account, 3m)))
        {
            deposit.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        (await _bed.BalanceAsync(account)).ClearBalance.ShouldBe(0m);
        (await ArrearsAsync(account)).ShouldBe((2m, 1));
    }

    [Fact]
    public async Task Collect_available_writes_off_what_the_balance_cannot_pay()
    {
        await ApprovedChargeAsync("WDLFEE", LookupCodes.ChargeShortfallPolicies.CollectAvailable, amount: 10m);
        await OpenTillWithCashAsync(10_000m);
        var (account, signers) = await _bed.AccountAsync(55m);

        using (var withdrawal = await PostingTestBed.PostAsync(_teller, "/api/v1/transactions/cash-withdrawals", Withdrawal(account, 50m, signers)))
        {
            withdrawal.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        (await _bed.BalanceAsync(account)).ClearBalance.ShouldBe(0m);
        (await _bed.LedgerBalanceAsync(_bed.ChargeIncomeId)).ShouldBe(-5m);
        (await ArrearsAsync(account)).ShouldBe((0m, 0));
    }

    [Fact]
    public async Task Charge_full_takes_the_whole_fee_and_then_refuses_further_debits()
    {
        await ApprovedChargeAsync("WDLFEE", LookupCodes.ChargeShortfallPolicies.ChargeFull, amount: 10m);
        await OpenTillWithCashAsync(10_000m);
        var (account, signers) = await _bed.AccountAsync(55m);

        // The charge may take the balance below zero: 55 - 50 - 10 = -5.
        using (var withdrawal = await PostingTestBed.PostAsync(_teller, "/api/v1/transactions/cash-withdrawals", Withdrawal(account, 50m, signers)))
        {
            withdrawal.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        (await _bed.BalanceAsync(account)).ClearBalance.ShouldBe(-5m);
        (await _bed.LedgerBalanceAsync(_bed.ChargeIncomeId)).ShouldBe(-10m);

        // A debit is refused while the balance is below zero; only a charge may make it negative.
        using (var refused = await PostingTestBed.PostAsync(_teller, "/api/v1/transactions/cash-withdrawals", Withdrawal(account, 1m, signers)))
        {
            refused.StatusCode.ShouldNotBe(HttpStatusCode.Created);
        }

        (await _bed.BalanceAsync(account)).ClearBalance.ShouldBe(-5m);
    }

    /// <summary>Creates a code and a flat per-withdrawal charge with the given shortfall policy, and has a second accountant approve it.</summary>
    private async Task ApprovedChargeAsync(string chargeCode, string policy, decimal amount)
    {
        using (var code = await _accountant.PostAsJsonAsync("/api/v1/charge-codes", new { code = chargeCode, description = chargeCode, categoryCode = LookupCodes.ChargeCategories.Transaction, isActive = true }))
        {
            code.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        var path = $"/api/v1/products/{_bed.Savings.ProductId}/charges";
        var tier = new { basisCode = LookupCodes.ChargeBases.Flat, transactionAmount = (decimal?)null, amountOrPercentage = amount };
        long id;
        using (var created = await _accountant.PostAsJsonAsync(path, new
               {
                   chargeCode,
                   conditionCode = LookupCodes.ChargeConditions.PerCashWithdrawal,
                   freeTransactions = (short?)null,
                   shortfallPolicyCode = policy,
                   effectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
                   tiers = new[] { tier }
               }))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.Accepted);
            id = (await ApiTestHost.ReadJsonAsync(created)).GetProperty("productChargeId").GetInt64();
        }

        using var approve = await _accountant2.PostAsync(new Uri($"{path}/{id}/supervise", UriKind.Relative), null);
        approve.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    /// <summary>Opens the teller's till and has the vault supervisor release the cash to it.</summary>
    private async Task OpenTillWithCashAsync(decimal amount)
    {
        using var open = await _teller.PostAsync(new Uri("/api/v1/tills/open", UriKind.Relative), null);
        open.StatusCode.ShouldBe(HttpStatusCode.Created);
        var session = (await ApiTestHost.ReadJsonAsync(open)).GetProperty("tillSessionId").GetInt64();

        using var buy = await PostingTestBed.PostAsync(_teller, $"/api/v1/tills/{session}/buy", new { amount });
        buy.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var requestId = (await ApiTestHost.ReadJsonAsync(buy)).GetProperty("postingRequestId").GetInt64();
        using var approve = await _supervisor.PostAsync(new Uri($"/api/v1/transaction-requests/{requestId}/approve", UriKind.Relative), null);
        approve.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>The account's arrears: what is still owed, and how many arrears there are.</summary>
    private async Task<(decimal Outstanding, int Rows)> ArrearsAsync(long account)
    {
        await using var context = _bed.Host.NewContext();
        var rows = await context.Set<AccountChargeArrear>().Where(row => row.AccountId == account).ToListAsync();
        return (rows.Sum(row => row.OutstandingAmount), rows.Count);
    }

    private static object Withdrawal(long account, decimal amount, long[] signers) =>
        new { accountId = account, amount, narration = "Counter", confirmedSignerPartyIds = signers, toHolder = true };

    private static object Deposit(long account, decimal amount) =>
        new { accountId = account, amount, narration = "Counter", byHolder = true, depositor = (object?)null };
}
