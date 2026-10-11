namespace Banking.Domain.Auditing;

/// <summary>
/// Append-only ledger row. EF must not mark these entities Modified or Deleted.
/// </summary>
public interface ILedgerRecord;
