using System.Net;
using System.Net.Http.Json;
using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Application.Errors;
using Banking.Domain.Institution;
using Banking.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Banking.Api.IntegrationTests.Reference;

/// <summary>A3-01: institution profile singleton, immutable branch codes, exactly one local currency, holidays.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class ReferenceDataTests : IAsyncLifetime
{
    private readonly MsSqlContainerFixture _fixture;
    private ApiTestHost _host = null!;
    private string _admin = null!;

    public ReferenceDataTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _host = await ApiTestHost.CreateAsync(_fixture, "reference");
        _admin = await _host.AdminTokenAsync();
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task The_institution_profile_is_a_singleton_that_can_only_be_read_and_updated()
    {
        await _host.CreateStaffAsync("TELLER31", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.Teller));
        using var teller = _host.Client(await _host.TokenAsync("TELLER31"));
        using var client = _host.Client(_admin);

        using var initial = await teller.GetAsync(new Uri("/api/v1/institution-profile", UriKind.Relative));
        initial.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ApiTestHost.ReadJsonAsync(initial)).GetProperty("isComplete").GetBoolean().ShouldBeFalse();

        using var categories = await client.GetAsync(new Uri("/api/v1/lookups/regulatory-categories", UriKind.Relative));
        var categoryId = (await ApiTestHost.ReadJsonAsync(categories)).EnumerateArray().First().GetProperty("id").GetInt32();

        var body = new
        {
            institutionName = "Akwaaba Savings and Loans",
            licenseNumber = "SL-0042",
            regulatoryCategoryId = categoryId,
            incorporationDate = "2019-05-01",
            headOfficeAddress = "12 Ring Road, Accra"
        };

        (await teller.PutAsJsonAsync("/api/v1/institution-profile", body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var updated = await client.PutAsJsonAsync("/api/v1/institution-profile", body);
        updated.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var read = await client.GetAsync(new Uri("/api/v1/institution-profile", UriKind.Relative));
        var profile = await ApiTestHost.ReadJsonAsync(read);
        profile.GetProperty("institutionName").GetString().ShouldBe("Akwaaba Savings and Loans");
        profile.GetProperty("isComplete").GetBoolean().ShouldBeTrue();

        using var future = await client.PutAsJsonAsync("/api/v1/institution-profile", body with { incorporationDate = "2999-01-01" });
        future.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await client.PostAsJsonAsync("/api/v1/institution-profile", body)).StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        (await client.DeleteAsync(new Uri("/api/v1/institution-profile", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);

        await using var context = _host.NewContext();
        (await context.Set<InstitutionProfile>().CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Branch_codes_are_unique_and_never_change()
    {
        using var client = _host.Client(_admin);

        using var created = await client.PostAsJsonAsync("/api/v1/branches", new { branchCode = "kum1", branchName = "Kumasi", clearingDays = 2 });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await ApiTestHost.ReadJsonAsync(created)).GetProperty("id").GetInt32();

        using var duplicate = await client.PostAsJsonAsync("/api/v1/branches", new { branchCode = "KUM1", branchName = "Kumasi again" });
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApiTestHost.ProblemTypeAsync(duplicate)).ShouldBe(ProblemCodes.ReferenceCodeTaken);

        using var recode = await client.PutAsJsonAsync($"/api/v1/branches/{id}", new { branchCode = "KUM2", branchName = "Kumasi" });
        recode.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiTestHost.ProblemTypeAsync(recode)).ShouldBe(ProblemCodes.ReferenceCodeImmutable);

        using var renamed = await client.PutAsJsonAsync($"/api/v1/branches/{id}", new { branchCode = "KUM1", branchName = "Kumasi Adum", clearingDays = 1, isActive = false });
        renamed.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var active = await client.GetAsync(new Uri("/api/v1/branches", UriKind.Relative));
        (await ApiTestHost.ReadJsonAsync(active)).EnumerateArray().ShouldNotContain(row => row.GetProperty("branchCode").GetString() == "KUM1");

        using var one = await client.GetAsync(new Uri($"/api/v1/branches/{id}", UriKind.Relative));
        (await ApiTestHost.ReadJsonAsync(one)).GetProperty("branchName").GetString().ShouldBe("Kumasi Adum");
    }

    [Fact]
    public async Task There_is_always_exactly_one_local_currency()
    {
        using var client = _host.Client(_admin);

        int ghsId;
        await using (var context = _host.NewContext())
        {
            ghsId = await context.Set<Currency>().Where(row => row.IsLocalCurrency).Select(row => row.CurrencyId).SingleAsync();
        }

        using var usd = await client.PostAsJsonAsync("/api/v1/currencies", new { currencyCode = "usd", currencyName = "US Dollar", exchangeRate = 15.2m });
        usd.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var duplicate = await client.PostAsJsonAsync("/api/v1/currencies", new { currencyCode = "USD", currencyName = "Dollar", exchangeRate = 15m });
        (await ApiTestHost.ProblemTypeAsync(duplicate)).ShouldBe(ProblemCodes.ReferenceCodeTaken);

        using var unsetLocal = await client.PutAsJsonAsync($"/api/v1/currencies/{ghsId}", new { currencyName = "Ghana Cedi", isLocalCurrency = false, exchangeRate = 1m });
        unsetLocal.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApiTestHost.ProblemTypeAsync(unsetLocal)).ShouldBe(ProblemCodes.LocalCurrencyRequired);

        using var eur = await client.PostAsJsonAsync("/api/v1/currencies", new { currencyCode = "EUR", currencyName = "Euro", isLocalCurrency = true });
        eur.StatusCode.ShouldBe(HttpStatusCode.Created);
        var eurId = (await ApiTestHost.ReadJsonAsync(eur)).GetProperty("id").GetInt32();

        await using (var context = _host.NewContext())
        {
            var local = await context.Set<Currency>().Where(row => row.IsLocalCurrency).ToListAsync();
            local.Count.ShouldBe(1);
            local[0].CurrencyId.ShouldBe(eurId);
            local[0].ExchangeRate.ShouldBe(1m);
        }

        using var back = await client.PutAsJsonAsync($"/api/v1/currencies/{ghsId}", new { currencyCode = "GHS", currencyName = "Ghana Cedi", isLocalCurrency = true });
        back.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var list = await client.GetAsync(new Uri("/api/v1/currencies", UriKind.Relative));
        var rows = (await ApiTestHost.ReadJsonAsync(list)).EnumerateArray().ToList();
        rows.Count(row => row.GetProperty("isLocalCurrency").GetBoolean()).ShouldBe(1);
        rows[0].GetProperty("currencyCode").GetString().ShouldBe("GHS");

        using var recode = await client.PutAsJsonAsync($"/api/v1/currencies/{eurId}", new { currencyCode = "EUX", currencyName = "Euro", exchangeRate = 17m });
        (await ApiTestHost.ProblemTypeAsync(recode)).ShouldBe(ProblemCodes.ReferenceCodeImmutable);
    }

    [Fact]
    public async Task Holidays_are_unique_by_date_and_can_be_removed()
    {
        using var client = _host.Client(_admin);

        using var created = await client.PostAsJsonAsync("/api/v1/holidays", new { holidayDate = "2031-07-01", remarks = "Republic Day" });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await ApiTestHost.ReadJsonAsync(created)).GetProperty("id").GetInt32();

        using var duplicate = await client.PostAsJsonAsync("/api/v1/holidays", new { holidayDate = "2031-07-01" });
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApiTestHost.ProblemTypeAsync(duplicate)).ShouldBe(ProblemCodes.HolidayExists);

        using var year = await client.GetAsync(new Uri("/api/v1/holidays?year=2031", UriKind.Relative));
        (await ApiTestHost.ReadJsonAsync(year)).GetArrayLength().ShouldBe(1);

        (await client.DeleteAsync(new Uri($"/api/v1/holidays/{id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.DeleteAsync(new Uri($"/api/v1/holidays/{id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
