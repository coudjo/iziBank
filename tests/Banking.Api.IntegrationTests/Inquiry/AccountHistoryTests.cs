using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Api.IntegrationTests.Transactions;
using Banking.Domain.Transactions;
using Banking.Infrastructure.Inquiry;
using Banking.Infrastructure.Transactions;
using Microsoft.Extensions.DependencyInjection;

namespace Banking.Api.IntegrationTests.Inquiry;

/// <summary>B7-01: the history and statement agree with the sub-ledger after funding and a withdrawal.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class AccountHistoryTests : IAsyncLifetime
{
    private readonly MsSqlContainerFixture _fixture;
    private PostingTestBed _bed = null!;

    public AccountHistoryTests(MsSqlContainerFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => _bed = await PostingTestBed.CreateAsync(_fixture, "history");

    public async Task DisposeAsync() => await _bed.Host.DisposeAsync();

    [Fact]
    public async Task Statement_and_entries_reconcile_after_funding_and_a_withdrawal()
    {
        var (accountId, _) = await _bed.AccountAsync(funding: 1000m);
        await JournalAsync(accountId, 300m);

        using var scope = _bed.Host.Factory.Services.CreateScope();
        var history = scope.ServiceProvider.GetRequiredService<AccountHistory>();

        var entries = await history.EntriesAsync(accountId, 1, 25, CancellationToken.None);
        entries.ShouldNotBeNull();
        entries.TotalCount.ShouldBe(2);
        entries.Items[0].BalanceAfter.ShouldBe(700m);
        entries.Items[0].CrDb.ShouldBe("D");

        var day = entries.Items[0].EntryDate;
        var statement = await history.StatementAsync(accountId, day, day, CancellationToken.None);
        statement.ShouldNotBeNull();
        statement.OpeningBalance.ShouldBe(0m);
        statement.TotalCredits.ShouldBe(1000m);
        statement.TotalDebits.ShouldBe(300m);
        statement.ClosingBalance.ShouldBe(700m);
        statement.Reconciles.ShouldBeTrue();
        statement.Entries.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Opening_balance_carries_forward_from_the_last_line_before_the_period()
    {
        var (accountId, _) = await _bed.AccountAsync(funding: 1000m);
        await JournalAsync(accountId, 300m);

        using var scope = _bed.Host.Factory.Services.CreateScope();
        var history = scope.ServiceProvider.GetRequiredService<AccountHistory>();

        var day = (await history.EntriesAsync(accountId, 1, 25, CancellationToken.None))!.Items[0].EntryDate;
        var after = await history.StatementAsync(accountId, day.AddDays(1), day.AddDays(2), CancellationToken.None);

        after.ShouldNotBeNull();
        after.OpeningBalance.ShouldBe(700m);
        after.Entries.Count.ShouldBe(0);
        after.ClosingBalance.ShouldBe(700m);
        after.Reconciles.ShouldBeTrue();
    }

    [Fact]
    public async Task Unknown_account_returns_null()
    {
        using var scope = _bed.Host.Factory.Services.CreateScope();
        var history = scope.ServiceProvider.GetRequiredService<AccountHistory>();

        (await history.EntriesAsync(999_999, 1, 25, CancellationToken.None)).ShouldBeNull();
        (await history.StatementAsync(999_999, DateOnly.MinValue, DateOnly.MaxValue, CancellationToken.None)).ShouldBeNull();
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
            "History test debit",
            [new AccountLine(accountId, EntrySides.Debit, amount)],
            [new LedgerLine(_bed.SuspenseId, _bed.HomeBranchId, EntrySides.Credit, amount)],
            CheckSignerKyc: false);

        var result = await engine.SubmitSystemAsync(actor, draft, CancellationToken.None);
        result.ProblemCode.ShouldBeNull();
    }
}
