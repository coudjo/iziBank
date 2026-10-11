using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace Banking.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Shared SQL Server 2022 container. Each test class should call
/// <see cref="CreateDatabaseAsync"/> so cleanup never needs to delete
/// append-only ledger rows.
/// </summary>
public sealed class MsSqlContainerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder(
            "mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public string MasterConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync().ConfigureAwait(true);
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync().ConfigureAwait(true);
    }

    public async Task<string> CreateDatabaseAsync(string databaseName)
    {
        await using var connection = new SqlConnection(MasterConnectionString);
        await connection.OpenAsync().ConfigureAwait(true);

        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{databaseName}];";
        await command.ExecuteNonQueryAsync().ConfigureAwait(true);

        var builder = new SqlConnectionStringBuilder(MasterConnectionString)
        {
            InitialCatalog = databaseName
        };

        return builder.ConnectionString;
    }
}
