namespace Banking.Domain.Auditing;

/// <summary>
/// Standard audit footer. Applied to every business table except lookups,
/// sessions, ledger logs, and balance snapshots. The interceptor (A2-01)
/// fills these; the SQL default on <see cref="CreatedOn"/> is the safety net.
/// </summary>
public interface IAuditable
{
    long? CreatedByUserId { get; set; }

    DateTime CreatedOn { get; set; }

    long? ModifiedByUserId { get; set; }

    DateTime? ModifiedOn { get; set; }
}
