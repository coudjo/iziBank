using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Api.IntegrationTests.Transactions;
using Banking.Domain.Interest;
using Banking.Domain.Ledger;
using Banking.Domain.Lookups;
using Banking.Domain.Products;
using Banking.Domain.Transactions;
using Banking.Infrastructure.EndOfDay;
using Banking.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Banking.Api.IntegrationTests.Interest;

/// <summary>B6: the end-of-day run posts the day's interest journal once, and a second run for the same day posts nothing.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class EndOfDayTests : IAsyncLifetime
{
    private readonly MsSqlContainerFixture _fixture;
    private PostingTestBed _bed = null!;

    public EndOfDayTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync() => _bed = await PostingTestBed.CreateAsync(_fixture, "endofday");

    public async Task DisposeAsync() => await _bed.Host.DisposeAsync();

    [Fact]
    public async Task The_daily_interest_journal_posts_once_and_matches_the_accruals()
    {
        var (accountId, _) = await _bed.AccountAsync(funding: 1000m);
        var (expenseId, payableId) = await SetUpInterestAsync(5m);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var actor = new PostingActor(_bed.SetupStaffId, _bed.HomeBranchId);

        using (var scope = _bed.Host.Factory.Services.CreateScope())
        {
            var first = await scope.ServiceProvider.GetRequiredService<EndOfDayRunner>().RunAsync(actor, today, CancellationToken.None);
            first.ProblemCode.ShouldBeNull();
            first.Value!.InterestJournalPosted.ShouldBeTrue();
        }

        decimal accrued;
        await using (var context = _bed.Host.NewContext())
        {
            accrued = Math.Round(
                await context.Set<AccountInterestAccrual>()
                    .Where(row => row.AccountId == accountId && row.AccrualDate == today)
                    .SumAsync(row => row.Amount),
                2,
                MidpointRounding.AwayFromZero);
        }

        accrued.ShouldBeGreaterThan(0m);
        (await _bed.LedgerBalanceAsync(expenseId)).ShouldBe(accrued);
        (await _bed.LedgerBalanceAsync(payableId)).ShouldBe(-accrued);

        using (var scope = _bed.Host.Factory.Services.CreateScope())
        {
            var second = await scope.ServiceProvider.GetRequiredService<EndOfDayRunner>().RunAsync(actor, today, CancellationToken.None);
            second.ProblemCode.ShouldBeNull();
            second.Value!.InterestJournalPosted.ShouldBeFalse();
        }

        (await _bed.LedgerBalanceAsync(expenseId)).ShouldBe(accrued);
        await using var check = _bed.Host.NewContext();
        var codeId = await check.Set<TransactionCode>().Where(code => code.Code == TransactionCodes.InterestAccrual).Select(code => code.TransactionCodeId).SingleAsync();
        (await check.Set<PostedTransaction>().CountAsync(row => row.TransactionCodeId == codeId && row.BranchId == _bed.HomeBranchId)).ShouldBe(1);
    }

    /// <summary>A savings product with a credit rate, and its interest expense and payable accounts.</summary>
    private async Task<(long ExpenseId, long PayableId)> SetUpInterestAsync(decimal rate)
    {
        await using var context = _bed.Host.NewContext();
        var creditId = await context.Set<RateType>().Where(row => row.Code == LookupCodes.RateTypes.Credit).Select(row => row.Id).SingleAsync();
        var expenseId = await PostingTestBed.LedgerAccountAsync(context, "5801-01", LookupCodes.LedgerAccountTypes.Expense, _bed.HomeBranchId);
        var payableId = await PostingTestBed.LedgerAccountAsync(context, "2201-01", LookupCodes.LedgerAccountTypes.Liability, _bed.HomeBranchId);

        context.Set<ProductInterestRate>().Add(new ProductInterestRate
        {
            ProductId = _bed.Savings.ProductId,
            RateTypeId = creditId,
            InterestRate = rate,
            EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30),
            CreatedOn = DateTime.UtcNow
        });
        context.Set<ProductLedgerSetting>().Add(new ProductLedgerSetting
        {
            ProductId = _bed.Savings.ProductId,
            SettingCode = LedgerSettingCodes.InterestExpense,
            LedgerAccountId = expenseId,
            CreatedOn = DateTime.UtcNow
        });
        context.Set<ProductLedgerSetting>().Add(new ProductLedgerSetting
        {
            ProductId = _bed.Savings.ProductId,
            SettingCode = LedgerSettingCodes.InterestPayable,
            LedgerAccountId = payableId,
            CreatedOn = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        return (expenseId, payableId);
    }
}
