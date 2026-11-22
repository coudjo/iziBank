using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Api.IntegrationTests.Transactions;
using Banking.Application.Errors;
using Banking.Domain.Charges;
using Banking.Domain.Lookups;
using Banking.Infrastructure.Charges;
using Banking.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Banking.Api.IntegrationTests.Charges;

/// <summary>B5-05: the monthly maintenance fee, charged once per month, with the shortfall policy applied.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class PeriodicChargesTests : IAsyncLifetime
{
    private readonly MsSqlContainerFixture _fixture;
    private PostingTestBed _bed = null!;

    public PeriodicChargesTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync() => _bed = await PostingTestBed.CreateAsync(_fixture, "periodic");

    public async Task DisposeAsync() => await _bed.Host.DisposeAsync();

    [Fact]
    public async Task A_monthly_fee_is_charged_once_for_the_month()
    {
        var (accountId, _) = await _bed.AccountAsync(funding: 1000m);
        await SetUpMonthlyFeeAsync(10m, LookupCodes.ChargeShortfallPolicies.ChargeFull);
        var workingDate = PeriodicFeeRules.PeriodEndOf(DateOnly.FromDateTime(DateTime.UtcNow));

        var first = await RunAsync(workingDate);
        first.Charged.ShouldBe(1);
        first.Amount.ShouldBe(10m);

        var second = await RunAsync(workingDate);
        second.Charged.ShouldBe(0);

        (await _bed.BalanceAsync(accountId)).ClearBalance.ShouldBe(990m);
        await using var context = _bed.Host.NewContext();
        (await context.Set<AccountChargePeriod>().CountAsync(row => row.AccountId == accountId)).ShouldBe(1);
    }

    [Fact]
    public async Task A_partly_funded_account_pays_what_it_has_and_the_rest_becomes_an_arrear()
    {
        var (accountId, _) = await _bed.AccountAsync(funding: 4m);
        await SetUpMonthlyFeeAsync(10m, LookupCodes.ChargeShortfallPolicies.Defer);
        var workingDate = PeriodicFeeRules.PeriodEndOf(DateOnly.FromDateTime(DateTime.UtcNow));

        var run = await RunAsync(workingDate);
        run.Charged.ShouldBe(1);
        run.Amount.ShouldBe(4m);

        (await _bed.BalanceAsync(accountId)).ClearBalance.ShouldBe(0m);
        await using var context = _bed.Host.NewContext();
        (await context.Set<AccountChargeArrear>().Where(row => row.AccountId == accountId).SumAsync(row => row.OutstandingAmount)).ShouldBe(6m);
        var period = await context.Set<AccountChargePeriod>().SingleAsync(row => row.AccountId == accountId);
        period.FeeAmount.ShouldBe(10m);
        period.ChargedAmount.ShouldBe(4m);
    }

    [Fact]
    public async Task A_month_with_no_balance_takes_nothing_and_is_not_recorded()
    {
        var (accountId, _) = await _bed.AccountAsync(funding: 0m);
        await SetUpMonthlyFeeAsync(10m, LookupCodes.ChargeShortfallPolicies.Defer);
        var workingDate = PeriodicFeeRules.PeriodEndOf(DateOnly.FromDateTime(DateTime.UtcNow));

        var run = await RunAsync(workingDate);
        run.Charged.ShouldBe(0);
        run.NotCharged.ShouldBe(1);

        await using var context = _bed.Host.NewContext();
        (await context.Set<AccountChargePeriod>().CountAsync(row => row.AccountId == accountId)).ShouldBe(0);
        (await context.Set<AccountChargeArrear>().Where(row => row.AccountId == accountId).SumAsync(row => row.OutstandingAmount)).ShouldBe(0m);
    }

    [Fact]
    public async Task A_percent_periodic_fee_is_refused()
    {
        await _bed.AccountAsync(funding: 100m);
        await SetUpMonthlyFeeAsync(2m, LookupCodes.ChargeShortfallPolicies.ChargeFull, LookupCodes.ChargeBases.Percent);
        var workingDate = PeriodicFeeRules.PeriodEndOf(DateOnly.FromDateTime(DateTime.UtcNow));

        using var scope = _bed.Host.Factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<PeriodicCharges>()
            .ChargeAsync(new PostingActor(_bed.SetupStaffId, _bed.HomeBranchId), workingDate, CancellationToken.None);
        result.ProblemCode.ShouldBe(ProblemCodes.Validation);
    }

    private async Task<PeriodicChargeRun> RunAsync(DateOnly workingDate)
    {
        using var scope = _bed.Host.Factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<PeriodicCharges>()
            .ChargeAsync(new PostingActor(_bed.SetupStaffId, _bed.HomeBranchId), workingDate, CancellationToken.None);
        result.ProblemCode.ShouldBeNull();
        return result.Value!;
    }

    /// <summary>A supervision-free monthly maintenance charge on the savings product, with one band.</summary>
    private async Task SetUpMonthlyFeeAsync(decimal amount, string shortfallCode, string basisCode = LookupCodes.ChargeBases.Flat)
    {
        await using var context = _bed.Host.NewContext();
        var categoryId = await context.Set<ChargeCategoryType>().Where(row => row.Code == LookupCodes.ChargeCategories.Periodic).Select(row => row.Id).SingleAsync();
        var conditionId = await context.Set<ChargeConditionType>().Where(row => row.Code == LookupCodes.ChargeConditions.MonthlyMaintenance).Select(row => row.Id).SingleAsync();
        var basisId = await context.Set<ChargeBasisType>().Where(row => row.Code == basisCode).Select(row => row.Id).SingleAsync();
        var policyId = await context.Set<ChargeShortfallPolicyType>().Where(row => row.Code == shortfallCode).Select(row => row.Id).SingleAsync();

        var code = new ChargeCode { Code = "MTHFEE", Description = "Monthly fee", ChargeCategoryId = categoryId, IsActive = true, CreatedOn = DateTime.UtcNow };
        context.Add(code);
        await context.SaveChangesAsync();

        var charge = new ProductCharge
        {
            ProductId = _bed.Savings.ProductId,
            ChargeId = code.ChargeId,
            ChargeConditionId = conditionId,
            ShortfallPolicyId = policyId,
            EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-60),
            IsSupervisionRequired = false,
            CreatedOn = DateTime.UtcNow
        };
        context.Add(charge);
        await context.SaveChangesAsync();

        context.Add(new ProductChargeTier
        {
            ProductChargeId = charge.ProductChargeId,
            TierNumber = 1,
            ChargeBasisId = basisId,
            AmountOrPercentage = amount
        });
        await context.SaveChangesAsync();
    }
}
