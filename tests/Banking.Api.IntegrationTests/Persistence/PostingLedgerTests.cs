using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Domain.Ledger;
using Banking.Domain.Lookups;
using Banking.Domain.Security;
using Banking.Domain.Transactions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Banking.Api.IntegrationTests.Persistence;

/// <summary>B0-01: the posting ledgers are append-only in the database and in EF; system transaction codes are seeded.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class PostingLedgerTests : IAsyncLifetime
{
    private readonly MsSqlContainerFixture _fixture;
    private ApiTestHost _host = null!;

    public PostingLedgerTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync() => _host = await ApiTestHost.CreateAsync(_fixture, "ledgers");

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task System_transaction_codes_are_seeded_once()
    {
        await using var context = _host.NewContext();
        var codes = await context.Set<TransactionCode>().ToListAsync();

        codes.Select(row => row.Code).ShouldBe(TransactionCodes.All.Select(row => row.Code), ignoreOrder: true);
        codes.ShouldAllBe(row => row.IsSystemCode && row.IsActive && TransactionCategories.All.Contains(row.Category));
    }

    [Fact]
    public async Task Posted_rows_cannot_be_updated_or_deleted_even_with_raw_sql()
    {
        var (transactionId, entryId) = await PostOneAsync();

        await using var connection = new SqlConnection(_host.ConnectionString);
        await connection.OpenAsync();

        await ShouldFailAsync(connection, "UPDATE [dbo].[PostedTransaction] SET [Narration] = N'changed' WHERE [PostedTransactionId] = @id", ("id", transactionId));
        await ShouldFailAsync(connection, "DELETE FROM [dbo].[PostedTransaction] WHERE [PostedTransactionId] = @id", ("id", transactionId));
        await ShouldFailAsync(connection, "UPDATE [dbo].[LedgerEntry] SET [Amount] = 1 WHERE [LedgerEntryId] = @id", ("id", entryId));
        await ShouldFailAsync(connection, "DELETE FROM [dbo].[LedgerEntry] WHERE [LedgerEntryId] = @id", ("id", entryId));

        // AccountLedgerEntry needs an account to insert into; its table type is checked instead.
        await using var command = new SqlCommand(
            "SELECT ledger_type_desc FROM sys.tables WHERE name = 'AccountLedgerEntry'",
            connection);
        var type = (string?)await command.ExecuteScalarAsync();
        type.ShouldNotBeNull();
        type.ShouldContain("APPEND_ONLY");
    }

    [Fact]
    public async Task Entity_framework_refuses_to_modify_a_posted_row()
    {
        var (transactionId, _) = await PostOneAsync();

        await using var context = _host.NewContext();
        var row = await context.Set<PostedTransaction>().SingleAsync(item => item.PostedTransactionId == transactionId);
        row.Narration = "changed";

        var failure = await Should.ThrowAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        failure.Message.ShouldContain(nameof(PostedTransaction));
    }

    [Fact]
    public async Task Ledger_lines_must_be_positive_and_on_one_side()
    {
        var (transactionId, _) = await PostOneAsync();
        var ledgerAccountId = await LedgerAccountAsync("1101099");

        await using var connection = new SqlConnection(_host.ConnectionString);
        await connection.OpenAsync();
        const string Insert =
            "INSERT INTO [dbo].[LedgerEntry] ([PostedTransactionId], [LedgerAccountId], [BranchId], [EntryDate], [CrDb], [Amount]) VALUES (@transaction, @account, @branch, '2026-10-21', @side, @amount)";
        await ShouldFailAsync(connection, Insert, ("transaction", transactionId), ("account", ledgerAccountId), ("branch", _host.HomeBranchId), ("side", "D"), ("amount", 0m));
        await ShouldFailAsync(connection, Insert, ("transaction", transactionId), ("account", ledgerAccountId), ("branch", _host.HomeBranchId), ("side", "X"), ("amount", 10m));
    }

    /// <summary>Writes one posted transaction with one GL line through EF, as the posting engine will.</summary>
    private async Task<(long TransactionId, long EntryId)> PostOneAsync()
    {
        var ledgerAccountId = await LedgerAccountAsync("1101001");
        await using var context = _host.NewContext();
        var codeId = await context.Set<TransactionCode>().Where(row => row.Code == TransactionCodes.Journal).Select(row => row.TransactionCodeId).SingleAsync();
        var systemId = await context.Set<StaffUser>().Where(row => row.StaffCode == StaffCodes.System).Select(row => row.StaffUserId).SingleAsync();

        var transaction = new PostedTransaction
        {
            Reference = $"001-261021-{Guid.NewGuid():N}"[..30],
            BranchId = _host.HomeBranchId,
            EntryDate = new DateOnly(2026, 10, 21),
            TransactionCodeId = codeId,
            Narration = "Test",
            OperatorStaffUserId = systemId,
            PostedOn = DateTime.UtcNow
        };
        transaction.LedgerEntries.Add(new LedgerEntry
        {
            LedgerAccountId = ledgerAccountId,
            BranchId = _host.HomeBranchId,
            EntryDate = transaction.EntryDate,
            CrDb = EntrySides.Debit,
            Amount = 10m
        });
        context.Set<PostedTransaction>().Add(transaction);
        await context.SaveChangesAsync();
        return (transaction.PostedTransactionId, transaction.LedgerEntries.Single().LedgerEntryId);
    }

    private async Task<long> LedgerAccountAsync(string code)
    {
        await using var context = _host.NewContext();
        var existing = await context.Set<LedgerAccount>().Where(row => row.LedgerAccountCode == code).Select(row => (long?)row.LedgerAccountId).SingleOrDefaultAsync();
        if (existing is { } id)
        {
            return id;
        }

        var asset = await context.Set<LedgerAccountType>().Where(row => row.Code == LookupCodes.LedgerAccountTypes.Asset).Select(row => row.Id).SingleAsync();
        var account = new LedgerAccount
        {
            BranchId = _host.HomeBranchId,
            LedgerAccountCode = code,
            AccountName = "Test cash",
            LedgerAccountTypeId = asset,
            IsSupervisionRequired = false,
            CreatedOn = DateTime.UtcNow
        };
        context.Add(account);
        await context.SaveChangesAsync();
        return account.LedgerAccountId;
    }

    private static async Task ShouldFailAsync(SqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = new SqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await Should.ThrowAsync<SqlException>(() => command.ExecuteNonQueryAsync());
    }
}
