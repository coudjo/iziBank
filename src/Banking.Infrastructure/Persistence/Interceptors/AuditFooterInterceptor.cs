using Banking.Application.Abstractions;
using Banking.Domain.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Banking.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Plan 5.2: fills CreatedOn/By on insert and ModifiedOn/By on update. Created values can never be changed.
/// With no signed-in user (system work) the By columns stay NULL.
/// </summary>
public sealed class AuditFooterInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;

    public AuditFooterInterceptor(ICurrentUser currentUser, TimeProvider timeProvider)
    {
        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var userId = _currentUser.StaffUserId;

        foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedOn = now;
                    entry.Entity.CreatedByUserId = userId;
                    entry.Entity.ModifiedOn = null;
                    entry.Entity.ModifiedByUserId = null;
                    break;

                case EntityState.Modified:
                    entry.Property(nameof(IAuditable.CreatedOn)).IsModified = false;
                    entry.Property(nameof(IAuditable.CreatedByUserId)).IsModified = false;
                    entry.Entity.ModifiedOn = now;
                    entry.Entity.ModifiedByUserId = userId;
                    break;

                default:
                    break;
            }
        }
    }
}
