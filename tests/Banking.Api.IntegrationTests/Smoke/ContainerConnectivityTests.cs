using Banking.Api.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Banking.Api.IntegrationTests.Smoke;

[Collection(SqlServerCollection.Name)]
public sealed class ContainerConnectivityTests
{
    private readonly MsSqlContainerFixture _fixture;

    public ContainerConnectivityTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Sql_server_accepts_a_query_on_a_fresh_database()
    {
        var databaseName = $"iziBank_a0_{Guid.NewGuid():N}";
        var connectionString = await _fixture.CreateDatabaseAsync(databaseName);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        var result = await command.ExecuteScalarAsync();

        Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture).ShouldBe(1);
    }
}
