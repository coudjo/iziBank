using Banking.Domain.Security;
using Banking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Banking.Infrastructure.Security;

public sealed record ActivityRow(
    long EventId,
    DateTime EventTimestamp,
    string EventCode,
    string Outcome,
    long? SessionId,
    long? StaffUserId,
    string? StaffCode,
    int? BranchId,
    string? EntityType,
    string? EntityId,
    string? RequestDetailsJson);

public sealed record ChangeRow(
    long AuditId,
    DateTime LoggedOn,
    string TableName,
    string? RecordId,
    string ActionType,
    long? StaffUserId,
    string? StaffCode,
    int? BranchId,
    string? OldValueJson,
    string? NewValueJson);

public sealed record ActivityFilter(long? StaffUserId, long? SessionId, string? EventCode, string? EntityType, string? EntityId, DateTime? From, DateTime? To);

public sealed record ChangeFilter(string? TableName, string? RecordId, long? StaffUserId, DateTime? From, DateTime? To);

/// <summary>Read-only audit viewers (A2-08). The endpoints mark these as sensitive reads.</summary>
public sealed class AuditQueries
{
    private readonly BankingDbContext _db;

    public AuditQueries(BankingDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<ActivityRow>> ActivityAsync(ActivityFilter filter, int page, int pageSize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query =
            from activity in _db.Set<UserActivityLog>().AsNoTracking()
            join session in _db.Set<UserSession>().AsNoTracking() on activity.SessionId equals (long?)session.SessionId into sessions
            from session in sessions.DefaultIfEmpty()
            select new { activity, session };

        if (filter.StaffUserId is { } staffUserId)
        {
            query = query.Where(row => row.session != null && row.session.StaffUserId == staffUserId);
        }

        if (filter.SessionId is { } sessionId)
        {
            query = query.Where(row => row.activity.SessionId == sessionId);
        }

        if (!string.IsNullOrWhiteSpace(filter.EventCode))
        {
            query = query.Where(row => row.activity.EventType!.EventCode == filter.EventCode);
        }

        if (!string.IsNullOrWhiteSpace(filter.EntityType))
        {
            query = query.Where(row => row.activity.EntityType == filter.EntityType);
        }

        if (!string.IsNullOrWhiteSpace(filter.EntityId))
        {
            query = query.Where(row => row.activity.EntityId == filter.EntityId);
        }

        if (filter.From is { } from)
        {
            query = query.Where(row => row.activity.EventTimestamp >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(row => row.activity.EventTimestamp < to);
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query
            .OrderByDescending(row => row.activity.EventId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new ActivityRow(
                row.activity.EventId,
                row.activity.EventTimestamp,
                row.activity.EventType!.EventCode,
                row.activity.Outcome,
                row.activity.SessionId,
                row.session == null ? null : row.session.StaffUserId,
                row.session == null || row.session.StaffUser == null ? null : row.session.StaffUser.StaffCode,
                row.activity.BranchId,
                row.activity.EntityType,
                row.activity.EntityId,
                row.activity.RequestDetailsJson))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<ActivityRow>(items, page, pageSize, total);
    }

    public async Task<PagedResult<ChangeRow>> ChangesAsync(ChangeFilter filter, int page, int pageSize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = _db.Set<DataChangeAuditLog>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.TableName))
        {
            query = query.Where(row => row.TableName == filter.TableName);
        }

        if (!string.IsNullOrWhiteSpace(filter.RecordId))
        {
            query = query.Where(row => row.RecordId == filter.RecordId);
        }

        if (filter.StaffUserId is { } staffUserId)
        {
            query = query.Where(row => row.StaffUserId == staffUserId);
        }

        if (filter.From is { } from)
        {
            query = query.Where(row => row.LoggedOn >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(row => row.LoggedOn < to);
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query
            .OrderByDescending(row => row.AuditId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new ChangeRow(
                row.AuditId,
                row.LoggedOn,
                row.TableName,
                row.RecordId,
                row.ActionType,
                row.StaffUserId,
                row.StaffUser == null ? null : row.StaffUser.StaffCode,
                row.BranchId,
                row.OldValueJson,
                row.NewValueJson))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<ChangeRow>(items, page, pageSize, total);
    }
}
