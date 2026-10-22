using System.Net;
using System.Net.Http.Json;
using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Api.IntegrationTests.Transactions;
using Banking.Domain.Lookups;
using Banking.Domain.Security;

namespace Banking.Api.IntegrationTests.Charges;

/// <summary>B5-01: per-transaction charges on cash withdrawals and transfers, booked to charge income, and the quote.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class ChargePostingTests : IAsyncLifetime
{
    private readonly MsSqlContainerFixture _fixture;
    private PostingTestBed _bed = null!;
    private HttpClient _accountant = null!;
    private HttpClient _accountant2 = null!;
    private HttpClient _supervisor = null!;
    private HttpClient _teller = null!;
    private long _till;

    public ChargePostingTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _bed = await PostingTestBed.CreateAsync(_fixture, "chargeposting");
        await _bed.LimitAsync(RoleGroupCatalog.Teller, TransactionLimitCodes.CashWithdrawal, 2_000m);
        await _bed.LimitAsync(RoleGroupCatalog.Teller, TransactionLimitCodes.Transfer, 5_000m);
        await _bed.FundVaultAsync(100_000m);
        await _bed.Host.CreateStaffAsync("CHGA1", canSwitchBranch: false, (_bed.HomeBranchId, RoleGroupCatalog.Accountant));
        await _bed.Host.CreateStaffAsync("CHGA2", canSwitchBranch: false, (_bed.HomeBranchId, RoleGroupCatalog.Accountant));
        await _bed.Host.CreateStaffAsync("CHGVS", canSwitchBranch: false, (_bed.HomeBranchId, RoleGroupCatalog.Supervisor));
        _accountant = _bed.Host.Client(await _bed.Host.TokenAsync("CHGA1"));
        _accountant2 = _bed.Host.Client(await _bed.Host.TokenAsync("CHGA2"));
        _supervisor = _bed.Host.Client(await _bed.Host.TokenAsync("CHGVS"));
        var (_, till) = await _bed.TellerAsync("CHGT");
        _till = till;
        _teller = _bed.Host.Client(await _bed.Host.TokenAsync("CHGT"));
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
    public async Task Withdrawals_pay_the_charge_after_the_free_ones_and_book_it_to_charge_income()
    {
        await ApprovedChargeAsync("WDLFEE", LookupCodes.ChargeConditions.PerCashWithdrawal, free: 1, amount: 2m);
        await OpenTillWithCashAsync(10_000m);
        var (account, signers) = await _bed.AccountAsync(1_000m);

        // The first withdrawal is within the free allowance: no charge.
        using (var free = await PostingTestBed.PostAsync(_teller, "/api/v1/transactions/cash-withdrawals", Withdrawal(account, 200m, signers)))
        {
            free.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        (await _bed.BalanceAsync(account)).ClearBalance.ShouldBe(800m);
        (await _bed.LedgerBalanceAsync(_bed.ChargeIncomeId)).ShouldBe(0m);

        // The second is charged: the account pays the cash and the fee; the till pays out the cash only.
        using (var charged = await PostingTestBed.PostAsync(_teller, "/api/v1/transactions/cash-withdrawals", Withdrawal(account, 100m, signers)))
        {
            charged.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        (await _bed.BalanceAsync(account)).ClearBalance.ShouldBe(698m);
        (await _bed.LedgerBalanceAsync(_till)).ShouldBe(9_700m);
        (await _bed.LedgerBalanceAsync(_bed.ChargeIncomeId)).ShouldBe(-2m);

        using var quote = await _teller.PostAsJsonAsync(new Uri("/api/v1/transactions/quote", UriKind.Relative), new { accountId = account, kind = "CASH_WITHDRAWAL", amount = 50m });
        quote.StatusCode.ShouldBe(HttpStatusCode.OK);
        var view = await ApiTestHost.ReadJsonAsync(quote);
        view.GetProperty("amount").GetDecimal().ShouldBe(2m);
        view.GetProperty("waived").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Transfers_pay_the_charge_on_top_of_the_amount_moved()
    {
        await ApprovedChargeAsync("TRFFEE", LookupCodes.ChargeConditions.PerTransferOut, free: null, amount: 5m);
        var (from, signers) = await _bed.AccountAsync(500m);
        var (to, _) = await _bed.AccountAsync();

        using var transfer = await PostingTestBed.PostAsync(
            _teller,
            "/api/v1/transactions/transfers",
            new { fromAccountId = from, toAccountId = to, amount = 100m, narration = "Rent", confirmedSignerPartyIds = signers });
        transfer.StatusCode.ShouldBe(HttpStatusCode.Created);

        (await _bed.BalanceAsync(from)).ClearBalance.ShouldBe(395m);
        (await _bed.BalanceAsync(to)).ClearBalance.ShouldBe(100m);
        (await _bed.LedgerBalanceAsync(_bed.ChargeIncomeId)).ShouldBe(-5m);
    }

    [Fact]
    public async Task The_quote_refuses_an_unknown_kind_and_a_missing_amount()
    {
        var (account, _) = await _bed.AccountAsync();

        using var unknown = await _teller.PostAsJsonAsync(new Uri("/api/v1/transactions/quote", UriKind.Relative), new { accountId = account, kind = "SALARY", amount = 10m });
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var none = await _teller.PostAsJsonAsync(new Uri("/api/v1/transactions/quote", UriKind.Relative), new { accountId = account, kind = "TRANSFER" });
        none.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>Creates a code, a flat per-transaction charge on the savings product, and has a second accountant approve it.</summary>
    private async Task ApprovedChargeAsync(string chargeCode, string condition, short? free, decimal amount)
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
                   conditionCode = condition,
                   freeTransactions = free,
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

    private static object Withdrawal(long account, decimal amount, long[] signers) =>
        new { accountId = account, amount, narration = "Counter", confirmedSignerPartyIds = signers, toHolder = true };
}
