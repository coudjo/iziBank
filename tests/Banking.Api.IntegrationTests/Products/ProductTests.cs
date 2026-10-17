using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Application.Errors;
using Banking.Domain.Institution;
using Banking.Domain.Ledger;
using Banking.Domain.Security;
using Banking.Domain.Supervision;

namespace Banking.Api.IntegrationTests.Products;

/// <summary>A5-01: products with four-eyes approval, locked fields and control-account rules (plan 6.8).</summary>
[Collection(SqlServerCollection.Name)]
public sealed class ProductTests : IAsyncLifetime
{
    private static readonly Dictionary<string, string?> FourEyes = new(StringComparer.Ordinal)
    {
        ["Supervision:AllowSelfApproval"] = "false"
    };

    private readonly MsSqlContainerFixture _fixture;
    private ApiTestHost _host = null!;
    private HttpClient _maker = null!;
    private HttpClient _checker = null!;
    private Dictionary<string, int> _productTypes = null!;
    private Dictionary<string, int> _ledgerTypes = null!;
    private Dictionary<string, int> _rateTypes = null!;
    private int _ghsId;
    private int _usdId;
    private long _savingsControl;
    private long _loanControl;

    public ProductTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _host = await ApiTestHost.CreateAsync(_fixture, "products", FourEyes);
        _maker = _host.Client(await _host.AdminTokenAsync());
        await _host.CreateStaffAsync("CHECKER1", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.Supervisor));
        _checker = _host.Client(await _host.TokenAsync("CHECKER1"));

        _productTypes = await LookupAsync("product-types");
        _ledgerTypes = await LookupAsync("ledger-account-types");
        _rateTypes = await LookupAsync("rate-types");

        await using (var context = _host.NewContext())
        {
            _ghsId = context.Set<Currency>().Single(row => row.CurrencyCode == "GHS").CurrencyId;
            var usd = new Currency { CurrencyCode = "USD", CurrencyName = "US dollar", ExchangeRate = 15m };
            context.Add(usd);
            await context.SaveChangesAsync();
            _usdId = usd.CurrencyId;
        }

        _savingsControl = await InsertLedgerAsync("2100-01", "Savings deposits", "LIABILITY", _ghsId, approved: true);
        _loanControl = await InsertLedgerAsync("1300-01", "Loans to customers", "ASSET", _ghsId, approved: true);
    }

    public async Task DisposeAsync()
    {
        _maker.Dispose();
        _checker.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task A_new_product_is_pending_until_a_different_supervisor_approves_it()
    {
        var id = await CreateAsync(Body("SAV01", "Regular savings", "SAVINGS", "10", _savingsControl));
        var created = await GetAsync(id);
        created.GetProperty("supervisionState").GetString().ShouldBe("PENDING");
        created.GetProperty("productTypeCode").GetString().ShouldBe("SAVINGS");
        created.GetProperty("ledgerControlAccountCode").GetString().ShouldBe("2100-01");
        created.GetProperty("isActive").GetBoolean().ShouldBeTrue();
        created.GetProperty("isFreezed").GetBoolean().ShouldBeFalse();

        using var duplicate = await _maker.PostAsJsonAsync("/api/v1/products", Body("sav01", "Again", "SAVINGS", "11", _savingsControl));
        await ExpectAsync(duplicate, HttpStatusCode.Conflict, ProblemCodes.ProductCodeTaken);

        await ExpectAsync(await ApproveAsync(_maker, id), HttpStatusCode.Conflict, SupervisionProblems.SelfApproval);
        (await ApproveAsync(_checker, id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GetAsync(id)).GetProperty("supervisionState").GetString().ShouldBe("APPROVED");
        await ExpectAsync(await ApproveAsync(_checker, id), HttpStatusCode.Conflict, SupervisionProblems.NotPending);
    }

    [Fact]
    public async Task The_control_account_must_be_an_approved_account_of_the_right_type_and_currency()
    {
        var usdLiability = await InsertLedgerAsync("2100-02", "USD savings", "LIABILITY", _usdId, approved: true);
        var pendingLiability = await InsertLedgerAsync("2100-03", "Pending deposits", "LIABILITY", _ghsId, approved: false);

        await ExpectFieldAsync(Body("BAD1", "Savings on an asset", "SAVINGS", "20", _loanControl), "ledgerControlAccountId");
        await ExpectFieldAsync(Body("BAD2", "Loan on a liability", "LOAN", "21", _savingsControl), "ledgerControlAccountId");
        await ExpectFieldAsync(Body("BAD3", "Cedi product, dollar account", "CURRENT", "22", usdLiability), "ledgerControlAccountId");
        await ExpectFieldAsync(Body("BAD4", "Pending account", "SAVINGS", "23", pendingLiability), "ledgerControlAccountId");
        await ExpectFieldAsync(Body("BAD5", "No account", "SAVINGS", "24", null), "ledgerControlAccountId");
        await ExpectFieldAsync(Body("BAD6", "Letters in prefix", "SAVINGS", "A1", _savingsControl), "accountPrefix");
        await ExpectFieldAsync(Body("BAD7", "Long prefix", "SAVINGS", "1234567", _savingsControl), "accountPrefix");

        (await CreateAsync(Body("LN01", "Business loan", "LOAN", "30", _loanControl))).ShouldBeGreaterThan(0);
        (await CreateAsync(Body("USD01", "Dollar current", "CURRENT", "31", usdLiability, _usdId))).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_pending_product_is_edited_in_place_and_can_be_rejected_with_its_rates()
    {
        var id = await CreateAsync(Body("CUR01", "Current", "CURRENT", "40", _savingsControl));

        using var edited = await _maker.PutAsJsonAsync($"/api/v1/products/{id}", Body(null, "Business current", "SAVINGS", "41", _savingsControl));
        edited.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var product = await GetAsync(id);
        product.GetProperty("productName").GetString().ShouldBe("Business current");
        product.GetProperty("productTypeCode").GetString().ShouldBe("SAVINGS");
        product.GetProperty("accountPrefix").GetString().ShouldBe("41");
        product.GetProperty("supervisionState").GetString().ShouldBe("PENDING");

        using var rate = await _maker.PostAsJsonAsync($"/api/v1/products/{id}/interest-rates", RateBody("CREDIT", 5m, Today));
        rate.StatusCode.ShouldBe(HttpStatusCode.Created);

        (await RejectAsync(_checker, id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await _maker.GetAsync(new Uri($"/api/v1/products/{id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_approved_product_keeps_its_type_prefix_and_currency_and_other_changes_need_approval()
    {
        var id = await CreateAsync(Body("SAV02", "Junior savings", "SAVINGS", "50", _savingsControl));
        (await ApproveAsync(_checker, id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var retype = await _maker.PutAsJsonAsync($"/api/v1/products/{id}", Body(null, "Junior savings", "CURRENT", "50", _savingsControl));
        await ExpectAsync(retype, HttpStatusCode.Conflict, ProblemCodes.ProductFieldLocked);
        using var reprefix = await _maker.PutAsJsonAsync($"/api/v1/products/{id}", Body(null, "Junior savings", "SAVINGS", "51", _savingsControl));
        await ExpectAsync(reprefix, HttpStatusCode.Conflict, ProblemCodes.ProductFieldLocked);
        using var recode = await _maker.PutAsJsonAsync($"/api/v1/products/{id}", Body("SAV03", "Junior savings", "SAVINGS", "50", _savingsControl));
        await ExpectAsync(recode, HttpStatusCode.BadRequest, ProblemCodes.ReferenceCodeImmutable);

        using var rename = await _maker.PutAsJsonAsync($"/api/v1/products/{id}", Body(null, "Kids savings", "SAVINGS", "50", _savingsControl, minimumBalance: 20m));
        rename.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var pending = await GetAsync(id);
        pending.GetProperty("productName").GetString().ShouldBe("Junior savings");
        pending.GetProperty("supervisionState").GetString().ShouldBe("APPROVED");
        var proposed = pending.GetProperty("pendingChange").GetProperty("proposed");
        proposed.GetProperty("productName").GetString().ShouldBe("Kids savings");
        proposed.GetProperty("minimumBalance").GetDecimal().ShouldBe(20m);

        using var second = await _maker.PutAsJsonAsync($"/api/v1/products/{id}", Body(null, "Other", "SAVINGS", "50", _savingsControl));
        await ExpectAsync(second, HttpStatusCode.Conflict, ProblemCodes.ChangePendingExists);

        await ExpectAsync(await ApproveAsync(_maker, id), HttpStatusCode.Conflict, SupervisionProblems.SelfApproval);
        (await ApproveAsync(_checker, id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var approved = await GetAsync(id);
        approved.GetProperty("productName").GetString().ShouldBe("Kids savings");
        approved.GetProperty("minimumBalance").GetDecimal().ShouldBe(20m);
        approved.GetProperty("pendingChange").ValueKind.ShouldBe(JsonValueKind.Null);

        // An approved product is never deleted; a pending change can be rejected and leaves the product alone.
        await ExpectAsync(await RejectAsync(_checker, id), HttpStatusCode.Conflict, SupervisionProblems.NotPending);
        (await _maker.PutAsJsonAsync($"/api/v1/products/{id}", Body(null, "Wrong", "SAVINGS", "50", _savingsControl, minimumBalance: 20m))).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await RejectAsync(_checker, id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GetAsync(id)).GetProperty("productName").GetString().ShouldBe("Kids savings");
    }

    [Fact]
    public async Task Freezing_applies_at_once_but_unfreezing_needs_approval_and_frozen_products_cannot_be_opened()
    {
        var id = await CreateAsync(Body("SAV04", "Susu savings", "SAVINGS", "60", _savingsControl));
        (await ApproveAsync(_checker, id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await OpenableCodesAsync()).ShouldContain("SAV04");

        using var freeze = await _maker.PutAsJsonAsync($"/api/v1/products/{id}", Body(null, "Susu savings", "SAVINGS", "60", _savingsControl, isFreezed: true));
        freeze.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GetAsync(id)).GetProperty("isFreezed").GetBoolean().ShouldBeTrue();
        (await OpenableCodesAsync()).ShouldNotContain("SAV04");

        using var unfreeze = await _maker.PutAsJsonAsync($"/api/v1/products/{id}", Body(null, "Susu savings", "SAVINGS", "60", _savingsControl, isFreezed: false));
        unfreeze.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await GetAsync(id)).GetProperty("isFreezed").GetBoolean().ShouldBeTrue();

        (await ApproveAsync(_checker, id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await OpenableCodesAsync()).ShouldContain("SAV04");

        // Pending and loan products are never offered for direct opening.
        await CreateAsync(Body("SAV05", "Not yet approved", "SAVINGS", "61", _savingsControl));
        var loan = await CreateAsync(Body("LN02", "Salary loan", "LOAN", "62", _loanControl));
        (await ApproveAsync(_checker, loan)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var openable = await OpenableCodesAsync();
        openable.ShouldNotContain("SAV05");
        openable.ShouldNotContain("LN02");
    }

    [Fact]
    public async Task Once_products_exist_the_local_currency_is_fixed()
    {
        await CreateAsync(Body("SAV06", "Fixed currency savings", "SAVINGS", "63", _savingsControl));

        using var moveLocal = await _maker.PutAsJsonAsync(
            $"/api/v1/currencies/{_usdId}",
            new { currencyName = "US dollar", exchangeRate = 1m, isLocalCurrency = true });
        await ExpectAsync(moveLocal, HttpStatusCode.Conflict, ProblemCodes.LocalCurrencyInUse);
    }

    [Fact]
    public async Task Tellers_can_read_products_but_not_change_them()
    {
        await _host.CreateStaffAsync("TELLER61", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.Teller));
        using var teller = _host.Client(await _host.TokenAsync("TELLER61"));
        (await teller.GetAsync(new Uri("/api/v1/products?openable=true", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await teller.PostAsJsonAsync("/api/v1/products", Body("SAV09", "Teller product", "SAVINGS", "69", _savingsControl))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private object RateBody(string type, decimal rate, DateOnly from) =>
        new { rateTypeId = _rateTypes[type], interestRate = rate, effectiveFrom = from };

    private object Body(
        string? code,
        string name,
        string type,
        string prefix,
        long? control,
        int? currencyId = null,
        decimal? minimumBalance = null,
        bool? isFreezed = null) =>
        new
        {
            productCode = code,
            productName = name,
            productTypeId = _productTypes[type],
            accountPrefix = prefix,
            currencyId = currencyId ?? _ghsId,
            ledgerControlAccountId = control,
            minimumBalance,
            isFreezed
        };

    private async Task<int> CreateAsync(object body)
    {
        using var response = await _maker.PostAsJsonAsync("/api/v1/products", body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await ApiTestHost.ReadJsonAsync(response)).GetProperty("id").GetInt32();
    }

    private async Task ExpectFieldAsync(object body, string field)
    {
        using var response = await _maker.PostAsJsonAsync("/api/v1/products", body);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiTestHost.ReadJsonAsync(response)).GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue(field);
    }

    private async Task<List<string>> OpenableCodesAsync()
    {
        using var response = await _maker.GetAsync(new Uri("/api/v1/products?openable=true&pageSize=100", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await ApiTestHost.ReadJsonAsync(response)).GetProperty("items").EnumerateArray()
            .Select(row => row.GetProperty("productCode").GetString()!)
            .ToList();
    }

    private async Task<long> InsertLedgerAsync(string code, string name, string type, int currencyId, bool approved)
    {
        await using var context = _host.NewContext();
        var account = new LedgerAccount
        {
            BranchId = _host.HomeBranchId,
            LedgerAccountCode = code,
            AccountName = name,
            LedgerAccountTypeId = _ledgerTypes[type],
            CurrencyId = currencyId,
            IsSupervisionRequired = !approved
        };
        context.Add(account);
        await context.SaveChangesAsync();
        return account.LedgerAccountId;
    }

    private async Task<Dictionary<string, int>> LookupAsync(string slug)
    {
        using var response = await _maker.GetAsync(new Uri($"/api/v1/lookups/{slug}", UriKind.Relative));
        return (await ApiTestHost.ReadJsonAsync(response)).EnumerateArray()
            .ToDictionary(row => row.GetProperty("code").GetString()!, row => row.GetProperty("id").GetInt32(), StringComparer.Ordinal);
    }

    private async Task<JsonElement> GetAsync(int id)
    {
        using var response = await _maker.GetAsync(new Uri($"/api/v1/products/{id}", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await ApiTestHost.ReadJsonAsync(response);
    }

    private static Task<HttpResponseMessage> ApproveAsync(HttpClient client, int id) =>
        client.PostAsync(new Uri($"/api/v1/products/{id}/supervise", UriKind.Relative), content: null);

    private static Task<HttpResponseMessage> RejectAsync(HttpClient client, int id) =>
        client.PostAsJsonAsync($"/api/v1/products/{id}/reject", new { reason = "Not needed" });

    private static async Task ExpectAsync(HttpResponseMessage response, HttpStatusCode status, string problemCode)
    {
        using (response)
        {
            response.StatusCode.ShouldBe(status);
            (await ApiTestHost.ProblemTypeAsync(response)).ShouldBe(problemCode);
        }
    }
}
