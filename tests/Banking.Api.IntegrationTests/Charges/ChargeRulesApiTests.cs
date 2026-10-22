using System.Net;
using System.Net.Http.Json;
using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Api.IntegrationTests.Transactions;
using Banking.Application.Errors;
using Banking.Domain.Charges;
using Banking.Domain.Lookups;
using Banking.Domain.Security;
using Banking.Domain.Supervision;
using Microsoft.EntityFrameworkCore;

namespace Banking.Api.IntegrationTests.Charges;

/// <summary>B0-03: charge codes and supervised product charges.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class ChargeRulesApiTests : IAsyncLifetime
{
    private readonly MsSqlContainerFixture _fixture;
    private PostingTestBed _bed = null!;
    private HttpClient _accountant = null!;
    private HttpClient _accountant2 = null!;
    private HttpClient _teller = null!;

    public ChargeRulesApiTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _bed = await PostingTestBed.CreateAsync(_fixture, "charges");
        await _bed.Host.CreateStaffAsync("CHG1", canSwitchBranch: false, (_bed.HomeBranchId, RoleGroupCatalog.Accountant));
        await _bed.Host.CreateStaffAsync("CHG2", canSwitchBranch: false, (_bed.HomeBranchId, RoleGroupCatalog.Accountant));
        await _bed.Host.CreateStaffAsync("CHGT", canSwitchBranch: false, (_bed.HomeBranchId, RoleGroupCatalog.Teller));
        _accountant = _bed.Host.Client(await _bed.Host.TokenAsync("CHG1"));
        _accountant2 = _bed.Host.Client(await _bed.Host.TokenAsync("CHG2"));
        _teller = _bed.Host.Client(await _bed.Host.TokenAsync("CHGT"));
    }

    public async Task DisposeAsync()
    {
        _accountant.Dispose();
        _accountant2.Dispose();
        _teller.Dispose();
        await _bed.Host.DisposeAsync();
    }

    [Fact]
    public async Task A_charge_code_is_unique_and_listed()
    {
        using (var created = await _accountant.PostAsJsonAsync("/api/v1/charge-codes", Code("WDLFEE", "Cash withdrawal fee")))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        using (var again = await _accountant.PostAsJsonAsync("/api/v1/charge-codes", Code("WDLFEE", "Again")))
        {
            await ExpectAsync(again, HttpStatusCode.Conflict, ProblemCodes.ReferenceCodeTaken);
        }

        using var list = await _accountant.GetAsync(new Uri("/api/v1/charge-codes", UriKind.Relative));
        var codes = await ApiTestHost.ReadJsonAsync(list);
        codes.EnumerateArray().Any(row => row.GetProperty("code").GetString() == "WDLFEE").ShouldBeTrue();
    }

    [Fact]
    public async Task A_product_charge_waits_for_a_different_supervisor_and_approval_ends_the_earlier_one()
    {
        await _accountant.PostAsJsonAsync("/api/v1/charge-codes", Code("WDLFEE", "Cash withdrawal fee"));
        var path = $"/api/v1/products/{_bed.Savings.ProductId}/charges";

        long first;
        using (var pending = await _accountant.PostAsJsonAsync(path, Charge(new DateOnly(2026, 1, 1))))
        {
            pending.StatusCode.ShouldBe(HttpStatusCode.Accepted);
            first = (await ApiTestHost.ReadJsonAsync(pending)).GetProperty("productChargeId").GetInt64();
        }

        using (var self = await _accountant.PostAsync(new Uri($"{path}/{first}/supervise", UriKind.Relative), null))
        {
            await ExpectAsync(self, HttpStatusCode.Conflict, SupervisionProblems.SelfApproval);
        }

        using (var approve = await _accountant2.PostAsync(new Uri($"{path}/{first}/supervise", UriKind.Relative), null))
        {
            approve.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        long second;
        using (var later = await _accountant.PostAsJsonAsync(path, Charge(new DateOnly(2026, 6, 1), [Flat(5m)])))
        {
            second = (await ApiTestHost.ReadJsonAsync(later)).GetProperty("productChargeId").GetInt64();
        }

        using (var approve = await _accountant2.PostAsync(new Uri($"{path}/{second}/supervise", UriKind.Relative), null))
        {
            approve.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using var listed = await _accountant.GetAsync(new Uri(path, UriKind.Relative));
        var rows = (await ApiTestHost.ReadJsonAsync(listed)).EnumerateArray().ToList();
        rows.Single(row => row.GetProperty("productChargeId").GetInt64() == first).GetProperty("effectiveTo").GetString().ShouldBe("2026-05-31");
        rows.Single(row => row.GetProperty("productChargeId").GetInt64() == second).GetProperty("state").GetString().ShouldBe("SUPERVISED");
    }

    [Fact]
    public async Task Bad_charges_are_refused_and_a_pending_one_can_be_rejected()
    {
        await _accountant.PostAsJsonAsync("/api/v1/charge-codes", Code("WDLFEE", "Cash withdrawal fee"));
        await _accountant.PostAsJsonAsync("/api/v1/charge-codes", Code("MTHFEE", "Monthly fee", LookupCodes.ChargeCategories.Periodic));
        var path = $"/api/v1/products/{_bed.Savings.ProductId}/charges";

        // A monthly fee is not a per-withdrawal charge: the code's category must match the condition's.
        using (var mismatch = await _accountant.PostAsJsonAsync(path, new
               {
                   chargeCode = "MTHFEE",
                   conditionCode = LookupCodes.ChargeConditions.PerCashWithdrawal,
                   effectiveFrom = new DateOnly(2026, 1, 1),
                   tiers = new[] { Flat(2m) }
               }))
        {
            mismatch.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        // A band that is not the last needs its limit.
        using (var bands = await _accountant.PostAsJsonAsync(path, Charge(new DateOnly(2026, 1, 1), [Flat(null, 2m), Flat(null, 3m)])))
        {
            bands.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        long pending;
        using (var created = await _accountant.PostAsJsonAsync(path, Charge(new DateOnly(2026, 1, 1))))
        {
            pending = (await ApiTestHost.ReadJsonAsync(created)).GetProperty("productChargeId").GetInt64();
        }

        using (var edit = await _accountant.PutAsJsonAsync($"{path}/{pending}", Charge(new DateOnly(2026, 2, 1))))
        {
            edit.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        using (var byTeller = await _teller.PostAsJsonAsync($"{path}/{pending}/reject", new { reason = "Wrong fee" }))
        {
            byTeller.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using (var reject = await _accountant2.PostAsJsonAsync($"{path}/{pending}/reject", new { reason = "Wrong fee" }))
        {
            reject.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        await using var context = _bed.Host.NewContext();
        (await context.Set<ProductCharge>().AnyAsync(row => row.ProductChargeId == pending)).ShouldBeFalse();
    }

    private static object Code(string code, string description, string category = LookupCodes.ChargeCategories.Transaction) =>
        new { code, description, categoryCode = category, isActive = true };

    private static object Flat(decimal amount) => Flat(null, amount);

    private static object Flat(decimal? upTo, decimal amount) =>
        new { basisCode = LookupCodes.ChargeBases.Flat, transactionAmount = upTo, amountOrPercentage = amount };

    private static object Charge(DateOnly from, object[]? tiers = null) => new
    {
        chargeCode = "WDLFEE",
        conditionCode = LookupCodes.ChargeConditions.PerCashWithdrawal,
        freeTransactions = 2,
        effectiveFrom = from,
        tiers = tiers ?? [Flat(2m)]
    };

    private static async Task ExpectAsync(HttpResponseMessage response, HttpStatusCode status, string problem)
    {
        response.StatusCode.ShouldBe(status);
        (await ApiTestHost.ProblemTypeAsync(response)).ShouldBe(problem);
    }
}
