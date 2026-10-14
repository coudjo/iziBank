namespace Banking.Application.Abstractions;

/// <summary>
/// Per-request activity record (plan 5.6). The HTTP layer fills the request facts; services enrich it with
/// <see cref="Record"/>. Exactly one <c>UserActivityLog</c> row is written per authenticated request.
/// </summary>
public interface IActivityContext
{
    string? EventCode { get; }

    string? EntityType { get; }

    string? EntityId { get; }

    IReadOnlyDictionary<string, object?> Details { get; }

    /// <summary>True for an authenticated POST, PUT, PATCH or DELETE: its row joins the business transaction.</summary>
    bool IsMutatingRequest { get; set; }

    string? HttpMethod { get; set; }

    string? RouteTemplate { get; set; }

    /// <summary>Set once the row for this request has been written.</summary>
    bool Written { get; set; }

    void Record(string eventCode, string? entityType = null, string? entityId = null, IReadOnlyDictionary<string, object?>? details = null);
}

public sealed class ActivityContext : IActivityContext
{
    private readonly Dictionary<string, object?> _details = new(StringComparer.Ordinal);

    public string? EventCode { get; private set; }

    public string? EntityType { get; private set; }

    public string? EntityId { get; private set; }

    public IReadOnlyDictionary<string, object?> Details => _details;

    public bool IsMutatingRequest { get; set; }

    public string? HttpMethod { get; set; }

    public string? RouteTemplate { get; set; }

    public bool Written { get; set; }

    public void Record(
        string eventCode,
        string? entityType = null,
        string? entityId = null,
        IReadOnlyDictionary<string, object?>? details = null)
    {
        ArgumentNullException.ThrowIfNull(eventCode);

        EventCode = eventCode;
        EntityType = entityType;
        EntityId = entityId;

        if (details is null)
        {
            return;
        }

        foreach (var pair in details)
        {
            _details[pair.Key] = pair.Value;
        }
    }
}
