using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Api.IntegrationTests.Transactions;
using Banking.Domain.Transactions;
using Banking.Infrastructure.Inquiry;
using Banking.Infrastructure.Transactions;
using Microsoft.Extensions.DependencyInjection;

namespace Banking.Api.IntegrationTests.Inquiry;

/// <summary>B7-02: the trial balance balances and the GL inquiry shows the same lines as the postings.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class LedgerInquiryTests : IAsyncLifetime
{
    private readonly MsSqlContainerFixture _fixture;
    private PostingTestBed _bed = null!;

    public LedgerInquiryTests(MsSqlContainerFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => _bed = await PostingTestBed.CreateAsync(_fixture, "ledgerinquiry");

    public async Task DisposeAsync() => await _bed.Host.DisposeAsync();

    [Fact]
    public async Task Trial_balance_balances_and_matches_the_ledger_movements()
    {
        var (accountId, _) = await _bed.AccountAsync(funding: 1000m);
        await JournalAsync(accountId, 300m);

        using var scope = _bed.Host.Factory.Services.CreateScope();
        var inquiry = scope.ServiceProvider.GetRequiredService<LedgerInquiry>();

        var suspense = await inquiry.EntriesAsync(_bed.SuspenseId, _bed.HomeBranchId, null, null, 1, 25, CancellationToken.None);
        suspense.ShouldNotBeNull();
        suspense.TotalCount.ShouldBe(2);
        suspense.Items[0].CrDb.ShouldBe("C");
        suspense.Items[0].Amount.ShouldBe(300m);

        var day = suspense.Items[0].EntryDate;
        var trialBalance = await inquiry.TrialBalanceAsync(_bed.HomeBranchId, day, CancellationToken.None);

        trialBalance.Balanced.ShouldBeTrue();
        trialBalance.TotalDebit.ShouldBe(1300m);
        trialBalance.TotalCredit.ShouldBe(1300m);
        trialBalance.Rows.Single(row => row.LedgerAccountId == _bed.SuspenseId).Net.ShouldBe(700m);
        trialBalance.Rows.Single(row => row.LedgerAccountId == _bed.ControlId).Net.ShouldBe(-700m);
    }

    [Fact]
    public async Task Unknown_ledger_account_returns_null()
    {
        using var scope = _bed.Host.Factory.Services.CreateScope();
        var inquiry = scope.ServiceProvider.GetRequiredService<LedgerInquiry>();

        (await inquiry.EntriesAsync(999_999, _bed.HomeBranchId, null, null, 1, 25, CancellationToken.None)).ShouldBeNull();
    }

    /// <summary>A debit to the customer account, offset to the suspense account, posted as a journal.</summary>
    private async Task JournalAsync(long accountId, decimal amount)
    {
        var actor = new PostingActor(_bed.SetupStaffId, _bed.HomeBranchId);
        using var scope = _bed.Host.Factory.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<PostingEngine>();
        var draft = new PostingDraft(
            TransactionCodes.Journal,
            null,
            amount,
            "Ledger inquiry test debit",
            [new AccountLine(accountId, EntrySides.Debit, amount)],
            [new LedgerLine(_bed.SuspenseId, _bed.HomeBranchId, EntrySides.Credit, amount)],
            CheckSignerKyc: false);

        var result = await engine.SubmitSystemAsync(actor, draft, CancellationToken.None);
        result.ProblemCode.ShouldBeNull();
    }
}
