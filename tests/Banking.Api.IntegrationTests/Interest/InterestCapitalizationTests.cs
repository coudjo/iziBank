using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Api.IntegrationTests.Transactions;
using Banking.Domain.Interest;
using Banking.Domain.Ledger;
using Banking.Domain.Lookups;
using Banking.Domain.Products;
using Banking.Infrastructure.Interest;
using Banking.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Banking.Api.IntegrationTests.Interest;

/// <summary>B5-04: a monthly period capitalizes the accrued interest once it has ended, and never before.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class InterestCapitalizationTests : IAsyncLifetime
{
    private readonly MsSqlContainerFixture _fixture;
    private PostingTestBed _bed = null!;

    public InterestCapitalizationTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync() => _bed = await PostingTestBed.CreateAsync(_fixture, "capitalize");

    public async Task DisposeAsync() => await _bed.Host.DisposeAsync();

    [Fact]
    public async Task The_accrued_interest_is_credited_once_the_month_has_ended()
    {
        var (accountId, _) = await _bed.AccountAsync(funding: 1000m);
        var monthEnd = MonthEnd(DateOnly.FromDateTime(DateTime.UtcNow));
        await SetUpProductAsync(_bed.Savings.ProductId, rate: 5m);

        using var scope = _bed.Host.Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<InterestAccrual>().AccrueAsync(_bed.HomeBranchId, monthEnd, CancellationToken.None);
        var capitalization = scope.ServiceProvider.GetRequiredService<InterestCapitalization>();
        var actor = new PostingActor(_bed.SetupStaffId, _bed.HomeBranchId);

        var run = await capitalization.CapitalizeAsync(actor, monthEnd, CancellationToken.None);
        run.ProblemCode.ShouldBeNull();

        await using (var context = _bed.Host.NewContext())
        {
            var rows = await context.Set<AccountInterestCapitalization>().Where(row => row.AccountId == accountId).ToListAsync();
            rows.Count.ShouldBe(1);
            rows[0].ToDate.ShouldBe(monthEnd);
            rows[0].TaxAmount.ShouldBe(0m);

            var accrued = await context.Set<AccountInterestAccrual>()
                .Where(row => row.AccountId == accountId && row.AccrualDate <= monthEnd)
                .SumAsync(row => row.Amount);
            rows[0].Amount.ShouldBe(Math.Round(accrued, 2, MidpointRounding.AwayFromZero));
        }

        var balance = await _bed.BalanceAsync(accountId);
        balance.ClearBalance.ShouldBe(1000m + (await CapitalizedAsync(accountId)));

        var again = await capitalization.CapitalizeAsync(actor, monthEnd, CancellationToken.None);
        again.ProblemCode.ShouldBeNull();
        (await CapitalizedRowsAsync(accountId)).ShouldBe(1);
    }

    [Fact]
    public async Task A_period_that_has_not_ended_is_not_capitalized()
    {
        var (accountId, _) = await _bed.AccountAsync(funding: 1000m);
        var monthEnd = MonthEnd(DateOnly.FromDateTime(DateTime.UtcNow));
        await SetUpProductAsync(_bed.Savings.ProductId, rate: 5m);

        using var scope = _bed.Host.Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<InterestAccrual>().AccrueAsync(_bed.HomeBranchId, monthEnd, CancellationToken.None);
        var actor = new PostingActor(_bed.SetupStaffId, _bed.HomeBranchId);

        var run = await scope.ServiceProvider.GetRequiredService<InterestCapitalization>()
            .CapitalizeAsync(actor, monthEnd.AddDays(-1), CancellationToken.None);

        run.ProblemCode.ShouldBeNull();
        (await CapitalizedRowsAsync(accountId)).ShouldBe(0);
    }

    private static DateOnly MonthEnd(DateOnly day) => new(day.Year, day.Month, DateTime.DaysInMonth(day.Year, day.Month));

    private async Task<int> CapitalizedRowsAsync(long accountId)
    {
        await using var context = _bed.Host.NewContext();
        return await context.Set<AccountInterestCapitalization>().CountAsync(row => row.AccountId == accountId);
    }

    private async Task<decimal> CapitalizedAsync(long accountId)
    {
        await using var context = _bed.Host.NewContext();
        return await context.Set<AccountInterestCapitalization>().Where(row => row.AccountId == accountId).SumAsync(row => row.Amount);
    }

    /// <summary>A monthly product with a 5% credit band, and a payable account for the capitalized interest.</summary>
    private async Task SetUpProductAsync(int productId, decimal rate)
    {
        await using var context = _bed.Host.NewContext();
        var creditId = await context.Set<RateType>().Where(row => row.Code == LookupCodes.RateTypes.Credit).Select(row => row.Id).SingleAsync();
        var monthlyId = await context.Set<CapitalizationFrequencyType>().Where(row => row.Code == LookupCodes.CapitalizationFrequencies.Monthly).Select(row => row.Id).SingleAsync();
        var payableId = await PostingTestBed.LedgerAccountAsync(context, "2201-01", LookupCodes.LedgerAccountTypes.Liability, _bed.HomeBranchId);

        var product = await context.Set<Product>().SingleAsync(row => row.ProductId == productId);
        product.InterestCapitalizationFrequencyId = monthlyId;
        context.Set<ProductInterestRate>().Add(new ProductInterestRate
        {
            ProductId = productId,
            RateTypeId = creditId,
            InterestRate = rate,
            EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30),
            CreatedOn = DateTime.UtcNow
        });
        context.Set<ProductLedgerSetting>().Add(new ProductLedgerSetting
        {
            ProductId = productId,
            SettingCode = LedgerSettingCodes.InterestPayable,
            LedgerAccountId = payableId,
            CreatedOn = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
    }
}
