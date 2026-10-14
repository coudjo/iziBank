using System.Text.Json;
using Banking.Domain.Security;
using Banking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Banking.Infrastructure.Security;

public static class ActivityOutcomes
{
    public const string Success = "Success";
    public const string Denied = "Denied";
    public const string Error = "Error";
}

public sealed record ActivityEntry(
    string EventCode,
    string Outcome,
    long? SessionId,
    int? BranchId,
    string? EntityType = null,
    string? EntityId = null,
    IReadOnlyDictionary<string, object?>? Details = null);

/// <summary>Builds and writes <see cref="UserActivityLog"/> rows (plan 5.6). Details are redacted here.</summary>
public sealed class ActivityLogWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private static readonly string[] SensitiveFragments = ["secret", "password", "otp", "token"];

    private readonly IServiceProvider _services;
    private readonly LookupIdCache _lookups;
    private readonly TimeProvider _timeProvider;

    public ActivityLogWriter(IServiceProvider services, LookupIdCache lookups, TimeProvider timeProvider)
    {
        _services = services;
        _lookups = lookups;
        _timeProvider = timeProvider;
    }

    public UserActivityLog Build(int eventTypeId, ActivityEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new UserActivityLog
        {
            EventTypeId = eventTypeId,
            SessionId = entry.SessionId,
            BranchId = entry.BranchId,
            EntityType = Truncate(entry.EntityType),
            EntityId = Truncate(entry.EntityId),
            Outcome = entry.Outcome,
            EventTimestamp = _timeProvider.GetUtcNow().UtcDateTime,
            RequestDetailsJson = SerializeDetails(entry.Details)
        };
    }

    /// <summary>Adds the row to an existing context without saving (it joins that context's transaction).</summary>
    public async Task AddAsync(BankingDbContext db, ActivityEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(entry);

        var eventTypeId = await _lookups.GetEventTypeIdAsync(db, entry.EventCode, cancellationToken).ConfigureAwait(false);
        db.Set<UserActivityLog>().Add(Build(eventTypeId, entry));
    }

    /// <summary>Writes the row on its own connection, independent of any failed business work.</summary>
    public async Task WriteAsync(ActivityEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var options = _services.GetRequiredService<DbContextOptions<BankingDbContext>>();
        var db = new BankingDbContext(options);
        await using var dbDisposal = db.ConfigureAwait(false);
        await AddAsync(db, entry, cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public static string? SerializeDetails(IReadOnlyDictionary<string, object?>? details)
    {
        if (details is null || details.Count == 0)
        {
            return null;
        }

        var safe = details
            .Where(static pair => !SensitiveFragments.Any(fragment =>
                pair.Key.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            .ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value is byte[] bytes ? $"[binary, {bytes.Length} bytes]" : pair.Value,
                StringComparer.Ordinal);

        return JsonSerializer.Serialize(safe, JsonOptions);
    }

    private static string? Truncate(string? value) => value is { Length: > 50 } ? value[..50] : value;
}
