using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Api.IntegrationTests.Transactions;
using Banking.Infrastructure.Inquiry;
using Microsoft.Extensions.DependencyInjection;

namespace Banking.Api.IntegrationTests.Inquiry;

/// <summary>
/// B7-03: the reports answer for a day with no till sessions and no arrears. The till-to-ledger match and the arrears
/// list need real sessions and arrears, which the till and charge tests create; see the note in the change log.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class CashAndFeeReportTests : IAsyncLifetime
{
    private readonly MsSqlContainerFixture _fixture;
    private PostingTestBed _bed = null!;

    public CashAndFeeReportTests(MsSqlContainerFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => _bed = await PostingTestBed.CreateAsync(_fixture, "cashfee");

    public async Task DisposeAsync() => await _bed.Host.DisposeAsync();

    [Fact]
    public async Task Teller_report_for_a_day_without_sessions_is_empty_and_balanced()
    {
        using var scope = _bed.Host.Factory.Services.CreateScope();
        var reports = scope.ServiceProvider.GetRequiredService<CashAndFeeReports>();

        var report = await reports.TellerCashAsync(_bed.HomeBranchId, DateOnly.FromDateTime(DateTime.UtcNow), CancellationToken.None);

        report.Sessions.Count.ShouldBe(0);
        report.TotalExpected.ShouldBe(0m);
        report.TotalCounted.ShouldBe(0m);
        report.Mismatches.ShouldBe(0);
    }

    [Fact]
    public async Task Uncollected_fees_for_a_branch_without_arrears_is_empty()
    {
        using var scope = _bed.Host.Factory.Services.CreateScope();
        var reports = scope.ServiceProvider.GetRequiredService<CashAndFeeReports>();

        var report = await reports.UncollectedFeesAsync(_bed.HomeBranchId, DateOnly.FromDateTime(DateTime.UtcNow), CancellationToken.None);

        report.Rows.Count.ShouldBe(0);
        report.TotalOutstanding.ShouldBe(0m);
    }
}
