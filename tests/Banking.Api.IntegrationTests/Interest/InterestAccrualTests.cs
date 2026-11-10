using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Api.IntegrationTests.Transactions;
using Banking.Domain.Interest;
using Banking.Domain.Lookups;
using Banking.Domain.Products;
using Banking.Infrastructure.Interest;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Banking.Api.IntegrationTests.Interest;

/// <summary>B5-03: daily accrual on the closing balance, each day written once.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class InterestAccrualTests : IAsyncLifetime
{
    private readonly MsSqlContainerFixture _fixture;
    private PostingTestBed _bed = null!;

    public InterestAccrualTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync() => _bed = await PostingTestBed.CreateAsync(_fixture, "interest");

    public async Task DisposeAsync() => await _bed.Host.DisposeAsync();

    [Fact]
    public async Task Each_day_accrues_on_the_closing_balance_and_running_the_same_date_again_adds_nothing()
    {
        var (accountId, _) = await _bed.AccountAsync(funding: 1000m);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await AddCreditBandAsync(_bed.Savings.ProductId, 5m);

        using var scope = _bed.Host.Factory.Services.CreateScope();
        var accrual = scope.ServiceProvider.GetRequiredService<InterestAccrual>();

        var first = await accrual.AccrueAsync(_bed.HomeBranchId, today.AddDays(2), CancellationToken.None);
        first.DaysAccrued.ShouldBeGreaterThanOrEqualTo(1);

        await using (var context = _bed.Host.NewContext())
        {
            var funded = await context.Set<AccountInterestAccrual>()
                .Where(row => row.AccountId == accountId && row.Balance == 1000m)
                .OrderBy(row => row.AccrualDate)
                .ToListAsync();

            funded.Count.ShouldBeGreaterThanOrEqualTo(3);
            funded.ShouldAllBe(row => row.Rate == 5m && row.Amount == 0.136986m);
            funded.Last().AccrualDate.ShouldBe(today.AddDays(2));
        }

        var second = await accrual.AccrueAsync(_bed.HomeBranchId, today.AddDays(2), CancellationToken.None);
        second.DaysAccrued.ShouldBe(0);
    }

    private async Task AddCreditBandAsync(int productId, decimal rate)
    {
        await using var context = _bed.Host.NewContext();
        var creditId = await context.Set<RateType>().Where(row => row.Code == LookupCodes.RateTypes.Credit).Select(row => row.Id).SingleAsync();
        context.Set<ProductInterestRate>().Add(new ProductInterestRate
        {
            ProductId = productId,
            RateTypeId = creditId,
            InterestRate = rate,
            EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30),
            CreatedOn = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
    }
}
