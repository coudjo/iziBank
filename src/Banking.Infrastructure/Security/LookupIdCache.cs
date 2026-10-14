using Banking.Domain.Lookups;
using Banking.Domain.Security;
using Banking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Banking.Infrastructure.Security;

/// <summary>Resolves seeded lookup and event-type codes to ids. Codes are immutable, so ids are cached for the process.</summary>
public sealed class LookupIdCache
{
    private readonly IMemoryCache _cache;

    public LookupIdCache(IMemoryCache cache)
    {
        _cache = cache;
    }

    public async Task<int> GetIdAsync<TLookup>(BankingDbContext db, string code, CancellationToken cancellationToken)
        where TLookup : Lookup
    {
        ArgumentNullException.ThrowIfNull(db);

        var key = $"lookup:{typeof(TLookup).Name}:{code}";
        if (_cache.TryGetValue(key, out int id))
        {
            return id;
        }

        id = await db.Set<TLookup>()
            .AsNoTracking()
            .Where(row => row.Code == code)
            .Select(row => row.Id)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        _cache.Set(key, id);
        return id;
    }

    public async Task<int> GetEventTypeIdAsync(BankingDbContext db, string eventCode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        var map = await _cache.GetOrCreateAsync("event-types", async _ =>
                await db.Set<EventType>()
                    .AsNoTracking()
                    .ToDictionaryAsync(row => row.EventCode, row => row.EventTypeId, StringComparer.Ordinal, cancellationToken)
                    .ConfigureAwait(false))
            .ConfigureAwait(false);

        return map![eventCode];
    }

    public int GetEventTypeId(BankingDbContext db, string eventCode)
    {
        ArgumentNullException.ThrowIfNull(db);

        var map = _cache.GetOrCreate("event-types", _ =>
            db.Set<EventType>()
                .AsNoTracking()
                .ToDictionary(row => row.EventCode, row => row.EventTypeId, StringComparer.Ordinal));

        return map![eventCode];
    }
}
