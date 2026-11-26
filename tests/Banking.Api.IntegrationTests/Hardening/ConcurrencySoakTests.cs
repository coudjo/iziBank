using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Api.IntegrationTests.Transactions;
using Banking.Domain.Accounts;
using Banking.Domain.Transactions;
using Banking.Infrastructure.Inquiry;
using Banking.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Banking.Api.IntegrationTests.Hardening;

/// <summary>
/// B8-03: many parallel transfers on a few accounts. No money is lost or created, every account's statement reconciles
/// to its balance, and the branch's trial balance is zero.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class ConcurrencySoakTests : IAsyncLifetime
{
    private const int AccountCount = 3;
    private const int TransferCount = 60;
    private const decimal Funding = 1_000m;

    private readonly MsSqlContainerFixture _fixture;
    private PostingTestBed _bed = null!;

    public ConcurrencySoakTests(MsSqlContainerFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => _bed = await PostingTestBed.CreateAsync(_fixture, "soak");

    public async Task DisposeAsync() => await _bed.Host.DisposeAsync();

    [Fact]
    public async Task Parallel_transfers_on_few_accounts_keep_every_balance_and_the_ledger_consistent()
    {
        var accounts = new List<long>();
        for (var index = 0; index < AccountCount; index++)
        {
            accounts.Add((await _bed.AccountAsync(funding: Funding)).AccountId);
        }

        // Fixed seed, so a failure can be reproduced.
        var random = new Random(20261124);
        var plan = Enumerable.Range(0, TransferCount).Select(_ =>
        {
            var from = random.Next(AccountCount);
            var to = (from + 1 + random.Next(AccountCount - 1)) % AccountCount;
            return (From: accounts[from], To: accounts[to], Amount: random.Next(1, 6));
        }).ToList();

        var actor = new PostingActor(_bed.SetupStaffId, _bed.HomeBranchId);
        var outcomes = await Task.WhenAll(plan.Select(async transfer =>
        {
            using var scope = _bed.Host.Factory.Services.CreateScope();
            var engine = scope.ServiceProvider.GetRequiredService<PostingEngine>();
            var draft = new PostingDraft(
                TransactionCodes.Transfer,
                null,
                transfer.Amount,
                "Soak transfer",
                [
                    new AccountLine(transfer.From, EntrySides.Debit, transfer.Amount),
                    new AccountLine(transfer.To, EntrySides.Credit, transfer.Amount),
                ],
                [],
                CheckSignerKyc: false);
            return await engine.SubmitSystemAsync(actor, draft, CancellationToken.None);
        }));

        outcomes.ShouldAllBe(outcome => outcome.ProblemCode == null);

        using var scope = _bed.Host.Factory.Services.CreateScope();
        var history = scope.ServiceProvider.GetRequiredService<AccountHistory>();
        var inquiry = scope.ServiceProvider.GetRequiredService<LedgerInquiry>();

        decimal total = 0m;
        await using (var context = _bed.Host.NewContext())
        {
            foreach (var accountId in accounts)
            {
                var balance = await context.Set<AccountBalance>().SingleAsync(row => row.AccountId == accountId);
                total += balance.ClearBalance;

                var statement = await history.StatementAsync(accountId, DateOnly.MinValue, DateOnly.MaxValue, CancellationToken.None);
                statement.ShouldNotBeNull();
                statement.Reconciles.ShouldBeTrue();
                statement.ClosingBalance.ShouldBe(balance.ClearBalance);
            }
        }

        total.ShouldBe(Funding * AccountCount);

        var trialBalance = await inquiry.TrialBalanceAsync(_bed.HomeBranchId, DateOnly.MaxValue, CancellationToken.None);
        trialBalance.Balanced.ShouldBeTrue();
    }
}
