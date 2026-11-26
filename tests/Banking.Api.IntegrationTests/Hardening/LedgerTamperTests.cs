using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Api.IntegrationTests.Transactions;
using Banking.Domain.Transactions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Banking.Api.IntegrationTests.Hardening;

/// <summary>
/// B8-02: ledger tamper. The customer sub-ledger is append-only in the database, so raw SQL cannot change or remove a
/// line. The other ledgers are covered in <c>PostingLedgerTests</c>.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class LedgerTamperTests : IAsyncLifetime
{
    private readonly MsSqlContainerFixture _fixture;
    private PostingTestBed _bed = null!;

    public LedgerTamperTests(MsSqlContainerFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => _bed = await PostingTestBed.CreateAsync(_fixture, "tamper");

    public async Task DisposeAsync() => await _bed.Host.DisposeAsync();

    [Fact]
    public async Task Account_ledger_lines_cannot_be_updated_or_deleted_with_raw_sql()
    {
        var (accountId, _) = await _bed.AccountAsync(funding: 1000m);

        long lineId;
        await using (var context = _bed.Host.NewContext())
        {
            lineId = await context.Set<AccountLedgerEntry>()
                .Where(entry => entry.AccountId == accountId)
                .Select(entry => entry.AccountLedgerEntryId)
                .FirstAsync();
        }

        await using var connection = new SqlConnection(_bed.Host.ConnectionString);
        await connection.OpenAsync();

        await ShouldFailAsync(connection, "UPDATE [dbo].[AccountLedgerEntry] SET [Amount] = 1 WHERE [AccountLedgerEntryId] = @id", lineId);
        await ShouldFailAsync(connection, "DELETE FROM [dbo].[AccountLedgerEntry] WHERE [AccountLedgerEntryId] = @id", lineId);

        // The line is still there, unchanged.
        await using var check = _bed.Host.NewContext();
        (await check.Set<AccountLedgerEntry>().SingleAsync(entry => entry.AccountLedgerEntryId == lineId)).Amount.ShouldBe(1000m);
    }

    private static async Task ShouldFailAsync(SqlConnection connection, string sql, long id)
    {
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", id);
        await Should.ThrowAsync<SqlException>(() => command.ExecuteNonQueryAsync());
    }
}
