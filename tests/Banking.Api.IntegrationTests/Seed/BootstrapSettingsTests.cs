using Banking.Migrator;
using Microsoft.Extensions.Configuration;

namespace Banking.Api.IntegrationTests.Seed;

public sealed class BootstrapSettingsTests
{
    [Fact]
    public void From_configuration_requires_connection_string_and_bootstrap_values()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var exception = Should.Throw<InvalidOperationException>(() => BootstrapSettings.From(configuration));
        exception.Message.ShouldContain("ConnectionStrings:Banking");
    }

    [Fact]
    public void Create_rejects_system_as_admin_code()
    {
        var exception = Should.Throw<InvalidOperationException>(() => BootstrapSettings.Create(
            "Server=localhost;Database=iziBank;TrustServerCertificate=True",
            "SYSTEM",
            "ReplaceWithAStrongSecret1",
            "001",
            "Head Office"));

        exception.Message.ShouldContain("SYSTEM");
    }

    [Fact]
    public void Create_accepts_env_shaped_values()
    {
        var settings = BootstrapSettings.Create(
            "Server=localhost,14334;Database=iziBank;TrustServerCertificate=True",
            "ADMIN",
            "ReplaceWithAStrongSecret1",
            "001",
            "Head Office");

        settings.AdminCode.ShouldBe("ADMIN");
        settings.BranchCode.ShouldBe("001");
        settings.BranchName.ShouldBe("Head Office");
    }
}
