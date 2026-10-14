using System.Text.Json;
using Banking.Application.Abstractions;
using Banking.Domain.Auditing;
using Banking.Domain.Security;
using Banking.Infrastructure.Persistence.Auditing;
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

        List<DataChangeAuditLog>? rows = null;
        try
        {
            rows = BuildRows();
            if (rows.Count > 0)
            {
                eventData.Context.Set<DataChangeAuditLog>().AddRange(rows);
                _writingAudit = true;
                eventData.Context.SaveChanges();
                Detach(eventData.Context, rows);
            }

            _ownedTransaction?.Commit();
        }
        catch
        {
            Detach(eventData.Context, rows);
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

        List<DataChangeAuditLog>? rows = null;
        try
        {
            rows = BuildRows();
            if (rows.Count > 0)
            {
                eventData.Context.Set<DataChangeAuditLog>().AddRange(rows);
                _writingAudit = true;
                await eventData.Context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                Detach(eventData.Context, rows);
            }

            if (_ownedTransaction is not null)
            {
                await _ownedTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            Detach(eventData.Context, rows);
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

    private static void Detach(DbContext context, List<DataChangeAuditLog>? rows)
    {
        if (rows is null)
        {
            return;
        }

        foreach (var row in rows)
        {
            context.Entry(row).State = EntityState.Detached;
        }
    }

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
