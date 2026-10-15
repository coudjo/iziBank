using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Banking.Application.Abstractions;
using Banking.Domain.Auditing;
using Banking.Domain.Security;
using Banking.Infrastructure.Persistence.Auditing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace Banking.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Plan 5.3: every insert, update and delete writes a <see cref="DataChangeAuditLog"/> row in the same
/// transaction as the change. If the audit rows cannot be written, the business change is rolled back.
/// One instance per DbContext (scoped).
/// </summary>
public sealed class DataChangeAuditInterceptor : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;

    private List<PendingAudit>? _pending;
    private IDbContextTransaction? _ownedTransaction;
    private bool _writingAudit;

    public DataChangeAuditInterceptor(ICurrentUser currentUser, TimeProvider timeProvider)
    {
        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (!_writingAudit && eventData.Context is { } context && Capture(context))
        {
            if (context.Database.CurrentTransaction is null)
            {
                _ownedTransaction = context.Database.BeginTransaction();
            }
        }

        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (!_writingAudit && eventData.Context is { } context && Capture(context))
        {
            if (context.Database.CurrentTransaction is null)
            {
                _ownedTransaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (_writingAudit || eventData.Context is null || _pending is null)
        {
            return base.SavedChanges(eventData, result);
        }

        try
        {
            _writingAudit = true;
            foreach (var (sql, parameters) in InsertCommands(BuildRows()))
            {
                try
                {
                    eventData.Context.Database.ExecuteSqlRaw(sql, parameters);
                }
                catch (SqlException exception)
                {
                    throw AuditWriteFailed(exception);
                }
            }

            _ownedTransaction?.Commit();
        }
        catch
        {
            _ownedTransaction?.Rollback();
            throw;
        }
        finally
        {
            _writingAudit = false;
            Reset();
        }

        return base.SavedChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (_writingAudit || eventData.Context is null || _pending is null)
        {
            return await base.SavedChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            _writingAudit = true;
            foreach (var (sql, parameters) in InsertCommands(BuildRows()))
            {
                try
                {
                    await eventData.Context.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken).ConfigureAwait(false);
                }
                catch (SqlException exception)
                {
                    throw AuditWriteFailed(exception);
                }
            }

            if (_ownedTransaction is not null)
            {
                await _ownedTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            if (_ownedTransaction is not null)
            {
                await _ownedTransaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }

            throw;
        }
        finally
        {
            _writingAudit = false;
            await ResetAsync().ConfigureAwait(false);
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (!_writingAudit)
        {
            _ownedTransaction?.Rollback();
            Reset();
        }

        base.SaveChangesFailed(eventData);
    }

    public override async Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (!_writingAudit)
        {
            if (_ownedTransaction is not null)
            {
                await _ownedTransaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }

            await ResetAsync().ConfigureAwait(false);
        }

        await base.SaveChangesFailedAsync(eventData, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Audited: everything except the ledger tables, sessions and idempotency results.</summary>
    internal static bool IsAudited(Type clrType) =>
        !typeof(ILedgerRecord).IsAssignableFrom(clrType)
        && clrType != typeof(UserSession)
        && clrType != typeof(IdempotencyKey);

    private bool Capture(DbContext context)
    {
        var pending = new List<PendingAudit>();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (!IsAudited(entry.Metadata.ClrType))
            {
                continue;
            }

            switch (entry.State)
            {
                case EntityState.Added:
                    pending.Add(new PendingAudit(entry, "INSERT", null, null));
                    break;

                case EntityState.Modified:
                    var oldValues = new Dictionary<string, object?>(StringComparer.Ordinal);
                    var newValues = new Dictionary<string, object?>(StringComparer.Ordinal);
                    foreach (var property in entry.Properties)
                    {
                        var name = property.Metadata.Name;
                        if (!property.IsModified || AuditRedaction.IsExcluded(name)
                            || (property.OriginalValue is not byte[] && Equals(property.OriginalValue, property.CurrentValue)))
                        {
                            continue;
                        }

                        oldValues[name] = AuditRedaction.ValueFor(name, property.OriginalValue);
                        newValues[name] = AuditRedaction.ValueFor(name, property.CurrentValue);
                    }

                    if (newValues.Count > 0)
                    {
                        pending.Add(new PendingAudit(entry, "UPDATE", oldValues, newValues) { RecordId = KeyOf(entry) });
                    }

                    break;

                case EntityState.Deleted:
                    pending.Add(new PendingAudit(entry, "DELETE", AllValues(entry, original: true), null) { RecordId = KeyOf(entry) });
                    break;

                default:
                    break;
            }
        }

        _pending = pending.Count > 0 ? pending : null;
        return _pending is not null;
    }

    private List<DataChangeAuditLog> BuildRows()
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var rows = new List<DataChangeAuditLog>(_pending!.Count);

        foreach (var item in _pending!)
        {
            var newValues = item.Action == "INSERT" ? AllValues(item.Entry, original: false) : item.NewValues;

            rows.Add(new DataChangeAuditLog
            {
                StaffUserId = _currentUser.StaffUserId,
                BranchId = _currentUser.BranchId,
                TableName = item.Entry.Metadata.GetTableName() ?? item.Entry.Metadata.ClrType.Name,
                RecordId = item.RecordId ?? KeyOf(item.Entry),
                ActionType = item.Action,
                OldValueJson = item.OldValues is null ? null : JsonSerializer.Serialize(item.OldValues, JsonOptions),
                NewValueJson = newValues is null ? null : JsonSerializer.Serialize(newValues, JsonOptions),
                LoggedOn = now
            });
        }

        return rows;
    }

    private static Dictionary<string, object?> AllValues(EntityEntry entry, bool original)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in entry.Properties)
        {
            var name = property.Metadata.Name;
            if (AuditRedaction.IsExcluded(name))
            {
                continue;
            }

            values[name] = AuditRedaction.ValueFor(name, original ? property.OriginalValue : property.CurrentValue);
        }

        return values;
    }

    private static string? KeyOf(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null)
        {
            return null;
        }

        var parts = key.Properties.Select(property => Convert.ToString(
            entry.Property(property.Name).CurrentValue,
            System.Globalization.CultureInfo.InvariantCulture));
        var recordId = string.Join('|', parts);
        return recordId.Length <= 50 ? recordId : recordId[..50];
    }

    /// <summary>
    /// Plain parameterised INSERTs. EF writes four or more rows as one MERGE, which an append-only ledger table
    /// rejects ("Updates are not allowed"), and the generated ids are not needed here.
    /// </summary>
    private static IEnumerable<(string Sql, object[] Parameters)> InsertCommands(List<DataChangeAuditLog> rows)
    {
        const int RowsPerCommand = 200; // 8 parameters per row, well under SQL Server's 2,100 limit.

        for (var start = 0; start < rows.Count; start += RowsPerCommand)
        {
            var sql = new StringBuilder(
                "INSERT INTO [dbo].[DataChangeAuditLog] ([StaffUserId], [BranchId], [TableName], [RecordId], [ActionType], [OldValueJson], [NewValueJson], [LoggedOn]) VALUES ");
            var parameters = new List<object>();
            var chunk = rows.Skip(start).Take(RowsPerCommand).ToList();

            for (var index = 0; index < chunk.Count; index++)
            {
                var row = chunk[index];
                var first = parameters.Count;
                parameters.Add(Parameter(first, row.StaffUserId, SqlDbType.BigInt));
                parameters.Add(Parameter(first + 1, row.BranchId, SqlDbType.Int));
                parameters.Add(Parameter(first + 2, row.TableName, SqlDbType.VarChar));
                parameters.Add(Parameter(first + 3, row.RecordId, SqlDbType.VarChar));
                parameters.Add(Parameter(first + 4, row.ActionType, SqlDbType.VarChar));
                parameters.Add(Parameter(first + 5, row.OldValueJson, SqlDbType.NVarChar));
                parameters.Add(Parameter(first + 6, row.NewValueJson, SqlDbType.NVarChar));
                parameters.Add(Parameter(first + 7, row.LoggedOn, SqlDbType.DateTime2));

                sql.Append(index == 0 ? "(" : ", (");
                for (var offset = 0; offset < 8; offset++)
                {
                    sql.Append(offset == 0 ? "@a" : ", @a").Append((first + offset).ToString(CultureInfo.InvariantCulture));
                }

                sql.Append(')');
            }

            yield return (sql.ToString(), parameters.ToArray());
        }
    }

    /// <summary>Callers see the same exception type as for any other failed save.</summary>
    private static DbUpdateException AuditWriteFailed(SqlException exception) =>
        new("The data change audit rows could not be written, so the change was rolled back.", exception);

    private static SqlParameter Parameter(int index, object? value, SqlDbType type) =>
        new("@a" + index.ToString(CultureInfo.InvariantCulture), type)
        {
            Value = value ?? DBNull.Value,
            Size = type is SqlDbType.VarChar or SqlDbType.NVarChar ? -1 : 0
        };

    private void Reset()
    {
        _pending = null;
        _ownedTransaction?.Dispose();
        _ownedTransaction = null;
    }

    private async Task ResetAsync()
    {
        _pending = null;
        if (_ownedTransaction is not null)
        {
            await _ownedTransaction.DisposeAsync().ConfigureAwait(false);
            _ownedTransaction = null;
        }
    }

    private sealed record PendingAudit(
        EntityEntry Entry,
        string Action,
        Dictionary<string, object?>? OldValues,
        Dictionary<string, object?>? NewValues)
    {
        public string? RecordId { get; init; }
    }
}
