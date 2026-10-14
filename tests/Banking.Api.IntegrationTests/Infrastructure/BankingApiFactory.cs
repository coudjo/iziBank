using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Banking.Api.IntegrationTests.Infrastructure;

public class BankingApiFactory : WebApplicationFactory<Program>
{
    public const string TestSigningKey = "integration-tests-only-signing-key-0123456789";

    private readonly string? _connectionString;

    public BankingApiFactory()
    {
    }

    // Internal so xUnit sees a single public constructor when this is used as a class fixture.
    internal BankingApiFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        // UseSetting, not ConfigureAppConfiguration: Program reads these before the host is built,
        // and only host settings are visible that early with minimal hosting.
        builder.UseSetting("Supervision:AllowSelfApproval", "true");
        builder.UseSetting("Jwt:SigningKey", TestSigningKey);
        builder.UseSetting("Sessions:SweeperEnabled", "false");
        builder.UseSetting(
            "ConnectionStrings:Banking",
            _connectionString ?? "Server=unused;Database=unused;TrustServerCertificate=True");
    }
}
