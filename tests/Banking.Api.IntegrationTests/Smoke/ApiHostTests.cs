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
}
