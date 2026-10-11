using System.Net.Http.Headers;

namespace Banking.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Builds an <see cref="HttpClient"/> against the test host.
/// JWT attachment is wired when authentication lands in A2-04.
/// </summary>
public static class AuthenticatedHttpClient
{
    public static HttpClient Create(BankingApiFactory factory)
    {
        return factory.CreateClient();
    }

    public static HttpClient Create(BankingApiFactory factory, string bearerToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", bearerToken);
        return client;
    }
}
