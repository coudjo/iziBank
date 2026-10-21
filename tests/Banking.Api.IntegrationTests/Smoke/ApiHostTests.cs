using System.Net;
using Banking.Api.IntegrationTests.Infrastructure;

namespace Banking.Api.IntegrationTests.Smoke;

public sealed class ApiHostTests : IClassFixture<BankingApiFactory>
{
    private readonly BankingApiFactory _factory;

    public ApiHostTests(BankingApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Host_starts_and_anonymous_routes_other_than_login_are_not_found()
    {
        using var client = AuthenticatedHttpClient.Create(_factory);

        var response = await client.GetAsync(new Uri("/", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Responses_are_never_cached_and_never_sniffed()
    {
        using var client = AuthenticatedHttpClient.Create(_factory);

        foreach (var path in new[] { "/", "/api/v1/auth/me" })
        {
            using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

            response.Headers.CacheControl?.NoStore.ShouldBeTrue(path);
            response.Headers.GetValues("X-Content-Type-Options").ShouldContain("nosniff", path);
        }
    }

    [Fact]
    public async Task The_api_description_is_not_published_outside_development()
    {
        using var client = AuthenticatedHttpClient.Create(_factory);

        using var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
