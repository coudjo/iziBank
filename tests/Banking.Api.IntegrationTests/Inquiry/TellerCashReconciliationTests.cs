using System.Net;
using System.Net.Http.Json;
using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Api.IntegrationTests.Transactions;
using Banking.Domain.Lookups;
using Banking.Domain.Security;
using Banking.Domain.Transactions;
using Banking.Infrastructure.Inquiry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Banking.Api.IntegrationTests.Inquiry;

/// <summary>
/// B7-03: a till closed short shows in the teller report with the difference the close posting booked, and the two agree.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class TellerCashReconciliationTests : IAsyncLifetime
{
    private readonly MsSqlContainerFixture _fixture;
    private PostingTestBed _bed = null!;
    private HttpClient _supervisor = null!;

    public TellerCashReconciliationTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _bed = await PostingTestBed.CreateAsync(_fixture, "tellerreport");
        await _bed.LimitAsync(RoleGroupCatalog.Teller, TransactionLimitCodes.CashDeposit, 50_000m);
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
    public async Task A_till_closed_short_reconciles_with_the_close_posting()
    {
        await _bed.TellerAsync("CASH1");
        using var teller = _bed.Host.Client(await _bed.Host.TokenAsync("CASH1"));

        long session;
        using (var open = await teller.PostAsync(new Uri("/api/v1/tills/open", UriKind.Relative), null))
        {
            open.StatusCode.ShouldBe(HttpStatusCode.Created);
            session = (await ApiTestHost.ReadJsonAsync(open)).GetProperty("tillSessionId").GetInt64();
        }

        // Cash from the vault waits for a vault supervisor.
        using (var buy = await PostingTestBed.PostAsync(teller, $"/api/v1/tills/{session}/buy", new { amount = 10_000m }))
        {
            await ApproveAsync(buy);
        }

        // Counted 10 short: the close posting books 10 to cash short and waits for approval.
        long closeRequestId;
        using (var close = await teller.PostAsJsonAsync($"/api/v1/tills/{session}/close", new { countedAmount = 9_990m }))
        {
            close.StatusCode.ShouldBe(HttpStatusCode.OK);
            var outcome = await ApiTestHost.ReadJsonAsync(close);
            closeRequestId = outcome.GetProperty("posting").GetProperty("postingRequestId").GetInt64();
        }

        using (var approve = await _supervisor.PostAsync(new Uri($"/api/v1/transaction-requests/{closeRequestId}/approve", UriKind.Relative), null))
        {
            approve.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        DateOnly workingDate;
        await using (var context = _bed.Host.NewContext())
        {
            var stored = await context.Set<TillSession>().SingleAsync(row => row.TillSessionId == session);
            stored.Status.ShouldBe(TillSessionStatuses.Closed);
            workingDate = stored.WorkingDate;
        }

        using var scope = _bed.Host.Factory.Services.CreateScope();
        var reports = scope.ServiceProvider.GetRequiredService<CashAndFeeReports>();
        var report = await reports.TellerCashAsync(_bed.HomeBranchId, workingDate, CancellationToken.None);

        report.Sessions.Count.ShouldBe(1);
        var row = report.Sessions[0];
        row.StaffCode.ShouldBe("CASH1");
        row.Status.ShouldBe(TillSessionStatuses.Closed);
        row.ExpectedAmount.ShouldBe(10_000m);
        row.CountedAmount.ShouldBe(9_990m);
        row.Difference.ShouldBe(-10m);
        row.BookedDifference.ShouldBe(-10m);
        row.Reconciles.ShouldBe(true);

        report.TotalDifference.ShouldBe(-10m);
        report.Mismatches.ShouldBe(0);
    }

    private async Task ApproveAsync(HttpResponseMessage pending)
    {
        pending.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var requestId = (await ApiTestHost.ReadJsonAsync(pending)).GetProperty("postingRequestId").GetInt64();
        using var approve = await _supervisor.PostAsync(new Uri($"/api/v1/transaction-requests/{requestId}/approve", UriKind.Relative), null);
        approve.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
