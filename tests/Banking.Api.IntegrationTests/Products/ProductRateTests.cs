using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Application.Errors;
using Banking.Domain.Institution;
using Banking.Domain.Ledger;
using Banking.Domain.Products;

namespace Banking.Api.IntegrationTests.Products;

/// <summary>A5-02: effective-dated product rates. Overlaps are refused, started rows are only closed (plan 6.8).</summary>
[Collection(SqlServerCollection.Name)]
public sealed class ProductRateTests : IAsyncLifetime
{
    private readonly MsSqlContainerFixture _fixture;
    private ApiTestHost _host = null!;
    private HttpClient _admin = null!;
    private Dictionary<string, int> _rateTypes = null!;
    private int _productTypeId;
    private int _ghsId;
    private long _controlId;
    private int _productCount;

    public ProductRateTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public async Task InitializeAsync()
    {
        _host = await ApiTestHost.CreateAsync(_fixture, "rates");
        _admin = _host.Client(await _host.AdminTokenAsync());
        _rateTypes = await LookupAsync("rate-types");
        _productTypeId = (await LookupAsync("product-types"))["SAVINGS"];
        var liability = (await LookupAsync("ledger-account-types"))["LIABILITY"];

        await using var context = _host.NewContext();
        _ghsId = context.Set<Currency>().Single(row => row.CurrencyCode == "GHS").CurrencyId;
        var control = new LedgerAccount
        {
            BranchId = _host.HomeBranchId,
            LedgerAccountCode = "2100-01",
            AccountName = "Savings deposits",
            LedgerAccountTypeId = liability,
            CurrencyId = _ghsId,
            IsSupervisionRequired = false
        };
        context.Add(control);
        await context.SaveChangesAsync();
        _controlId = control.LedgerAccountId;
    }

    public async Task DisposeAsync()
    {
        _admin.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task Adding_a_rate_closes_the_open_rate_of_the_same_band_the_day_before()
    {
        var productId = await CreateProductAsync();

        var first = await AddAsync(productId, Rate("CREDIT", 5m, Today));
        first.GetProperty("closedRateId").ValueKind.ShouldBe(JsonValueKind.Null);
        var firstId = first.GetProperty("productInterestRateId").GetInt64();

        var second = await AddAsync(productId, Rate("CREDIT", 6m, Today.AddDays(10)));
        second.GetProperty("closedRateId").GetInt64().ShouldBe(firstId);
        second.GetProperty("closedOn").GetString().ShouldBe(Today.AddDays(9).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

        var rates = await ListAsync(productId);
        rates.Count.ShouldBe(2);
        var current = rates.Single(row => row.GetProperty("productInterestRateId").GetInt64() == firstId);
        current.GetProperty("status").GetString().ShouldBe("CURRENT");
        current.GetProperty("isStarted").GetBoolean().ShouldBeTrue();
        rates.Single(row => row.GetProperty("productInterestRateId").GetInt64() != firstId)
            .GetProperty("status").GetString().ShouldBe("SCHEDULED");
    }

    [Fact]
    public async Task Rates_of_the_same_type_and_an_overlapping_band_cannot_overlap_in_time()
    {
        var productId = await CreateProductAsync();
        await AddAsync(productId, Rate("CREDIT", 3m, Today, minimum: 0m, maximum: 1000m));

        using var overlap = await PostAsync(productId, Rate("CREDIT", 4m, Today.AddDays(5), minimum: 500m, maximum: 5000m));
        await ExpectAsync(overlap, HttpStatusCode.Conflict, ProblemCodes.ProductRateOverlap);

        await AddAsync(productId, Rate("CREDIT", 4m, Today, minimum: 1000.01m, maximum: 5000m));
        await AddAsync(productId, Rate("DEBIT", 24m, Today, minimum: 0m, maximum: 1000m));

        using var past = await PostAsync(productId, Rate("CREDIT", 4m, Today.AddDays(-1), minimum: 6000m));
        await ExpectFieldAsync(past, "effectiveFrom");
        using var band = await PostAsync(productId, Rate("CREDIT", 4m, Today, minimum: 9000m, maximum: 8000m));
        await ExpectFieldAsync(band, "maximumAmount");
        using var tooHigh = await PostAsync(productId, Rate("CREDIT", 101m, Today, minimum: 9000m));
        await ExpectFieldAsync(tooHigh, "interestRate");

        (await ListAsync(productId)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_started_rate_is_never_edited_in_place_only_closed()
    {
        var productId = await CreateProductAsync();
        var started = (await AddAsync(productId, Rate("CREDIT", 5m, Today))).GetProperty("productInterestRateId").GetInt64();

        using var edit = await _admin.PutAsJsonAsync($"/api/v1/products/{productId}/interest-rates/{started}", Rate("CREDIT", 7m, Today));
        await ExpectAsync(edit, HttpStatusCode.Conflict, ProblemCodes.ProductRateStarted);
        using var delete = await _admin.DeleteAsync(new Uri($"/api/v1/products/{productId}/interest-rates/{started}", UriKind.Relative));
        await ExpectAsync(delete, HttpStatusCode.Conflict, ProblemCodes.ProductRateStarted);

        using var tooEarly = await CloseAsync(productId, started, Today.AddDays(-2));
        await ExpectFieldAsync(tooEarly, "effectiveTo");
        (await CloseAsync(productId, started, Today.AddDays(3))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var longer = await CloseAsync(productId, started, Today.AddDays(4));
        await ExpectFieldAsync(longer, "effectiveTo");
        (await CloseAsync(productId, started, Today)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var closed = (await ListAsync(productId)).Single();
        closed.GetProperty("effectiveTo").GetString().ShouldBe(Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        closed.GetProperty("interestRate").GetDecimal().ShouldBe(5m);

        // A rate that has not started can still be changed or removed.
        var scheduled = (await AddAsync(productId, Rate("CREDIT", 6m, Today.AddDays(20), to: Today.AddDays(30)))).GetProperty("productInterestRateId").GetInt64();
        using var change = await _admin.PutAsJsonAsync($"/api/v1/products/{productId}/interest-rates/{scheduled}", Rate("CREDIT", 6.5m, Today.AddDays(21), to: Today.AddDays(30)));
        change.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await _admin.DeleteAsync(new Uri($"/api/v1/products/{productId}/interest-rates/{scheduled}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ListAsync(productId)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Parallel_identical_rates_end_with_exactly_one_row()
    {
        var productId = await CreateProductAsync();
        var body = Rate("CREDIT", 5m, Today.AddDays(1), minimum: 0m, maximum: 100m, to: Today.AddDays(2));

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => PostAsync(productId, body)));
        try
        {
            responses.Count(response => response.StatusCode == HttpStatusCode.Created).ShouldBe(1);
            responses.Count(response => response.StatusCode == HttpStatusCode.Conflict).ShouldBe(7);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        await using var context = _host.NewContext();
        context.Set<ProductInterestRate>().Count(row => row.ProductId == productId).ShouldBe(1);
    }

    [Fact]
    public async Task Rates_of_an_unknown_product_are_not_found()
    {
        (await _admin.GetAsync(new Uri("/api/v1/products/999999/interest-rates", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var add = await PostAsync(999999, Rate("CREDIT", 5m, Today));
        add.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private object Rate(string type, decimal rate, DateOnly from, decimal? minimum = null, decimal? maximum = null, DateOnly? to = null) =>
        new { rateTypeId = _rateTypes[type], interestRate = rate, effectiveFrom = from, effectiveTo = to, minimumAmount = minimum, maximumAmount = maximum };

    private async Task<int> CreateProductAsync()
    {
        var number = Interlocked.Increment(ref _productCount);
        using var response = await _admin.PostAsJsonAsync("/api/v1/products", new
        {
            productCode = $"RT{number:D2}",
            productName = $"Rated savings {number}",
            productTypeId = _productTypeId,
            accountPrefix = "10",
            currencyId = _ghsId,
            ledgerControlAccountId = _controlId
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await ApiTestHost.ReadJsonAsync(response)).GetProperty("id").GetInt32();
    }

    private Task<HttpResponseMessage> PostAsync(int productId, object body) =>
        _admin.PostAsJsonAsync($"/api/v1/products/{productId}/interest-rates", body);

    private Task<HttpResponseMessage> CloseAsync(int productId, long rateId, DateOnly to) =>
        _admin.PostAsJsonAsync($"/api/v1/products/{productId}/interest-rates/{rateId}/close", new { effectiveTo = to });

    private async Task<JsonElement> AddAsync(int productId, object body)
    {
        using var response = await PostAsync(productId, body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await ApiTestHost.ReadJsonAsync(response);
    }

    private async Task<List<JsonElement>> ListAsync(int productId)
    {
        using var response = await _admin.GetAsync(new Uri($"/api/v1/products/{productId}/interest-rates", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await ApiTestHost.ReadJsonAsync(response)).EnumerateArray().ToList();
    }

    private async Task<Dictionary<string, int>> LookupAsync(string slug)
    {
        using var response = await _admin.GetAsync(new Uri($"/api/v1/lookups/{slug}", UriKind.Relative));
        return (await ApiTestHost.ReadJsonAsync(response)).EnumerateArray()
            .ToDictionary(row => row.GetProperty("code").GetString()!, row => row.GetProperty("id").GetInt32(), StringComparer.Ordinal);
    }

    private static async Task ExpectFieldAsync(HttpResponseMessage response, string field)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiTestHost.ReadJsonAsync(response)).GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue(field);
    }

    private static async Task ExpectAsync(HttpResponseMessage response, HttpStatusCode status, string problemCode)
    {
        response.StatusCode.ShouldBe(status);
        (await ApiTestHost.ProblemTypeAsync(response)).ShouldBe(problemCode);
    }
}
