namespace Banking.Api.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<MsSqlContainerFixture>
{
    public const string Name = "sql-server";
}
