using Banking.Application.Abstractions;
using Banking.Domain.Auditing;
using Banking.Domain.Lookups;
using Banking.Domain.Security;
using Banking.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Banking.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Plan 5.6: for a mutating request, the activity row is saved in the same transaction as the business change,
/// so a change cannot commit without its log row.
/// </summary>
public sealed class ActivityLogInterceptor : SaveChangesInterceptor
{
    private readonly IActivityContext _activity;
    private readonly ICurrentUser _currentUser;
    private readonly LookupIdCache _lookups;
    private readonly ActivityLogWriter _writer;
    private UserActivityLog? _added;

    public ActivityLogInterceptor(
        IActivityContext activity,
        ICurrentUser currentUser,
        LookupIdCache lookups,
        ActivityLogWriter writer)
    {
        _activity = activity;
        _currentUser = currentUser;
        _lookups = lookups;
        _writer = writer;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (eventData.Context is BankingDbContext db && ShouldAttach(db))
        {
            Attach(db, _lookups.GetEventTypeId(db, EventCodeFor()));
        }

        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (eventData.Context is BankingDbContext db && ShouldAttach(db))
        {
            var eventTypeId = await _lookups.GetEventTypeIdAsync(db, EventCodeFor(), cancellationToken).ConfigureAwait(false);
            Attach(db, eventTypeId);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Undo(eventData.Context);
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Undo(eventData.Context);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private bool ShouldAttach(BankingDbContext db) =>
        _activity.IsMutatingRequest
        && !_activity.Written
        && _currentUser.StaffUserId is not null
        && db.ChangeTracker.Entries().Any(static entry =>
            entry.Entity is not ILedgerRecord
            && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);

    private string EventCodeFor() => _activity.EventCode ?? EventCodes.ApiRequest;

    private void Attach(BankingDbContext db, int eventTypeId)
    {
        var details = new Dictionary<string, object?>(_activity.Details, StringComparer.Ordinal)
        {
            ["method"] = _activity.HttpMethod,
            ["route"] = _activity.RouteTemplate
        };

        _added = _writer.Build(
            eventTypeId,
            new ActivityEntry(
                EventCodeFor(),
                ActivityOutcomes.Success,
                _currentUser.SessionId,
                _currentUser.BranchId,
                _activity.EntityType,
                _activity.EntityId,
                details));

        db.Set<UserActivityLog>().Add(_added);
        _activity.Written = true;
    }

    private void Undo(DbContext? db)
    {
        if (_added is null || db is null)
        {
            return;
        }

        db.Entry(_added).State = EntityState.Detached;
        _added = null;
        _activity.Written = false;
    }
}
