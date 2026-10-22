using Banking.Domain.Auditing;
using Banking.Domain.Institution;
using Banking.Domain.Ledger;
using Banking.Domain.Security;

namespace Banking.Domain.Transactions;

/// <summary>Plan B 2.3: where a till session stands.</summary>
public static class TillSessionStatuses
{
    public const string Open = "OPEN";

    /// <summary>Counted and waiting for the supervisor to approve the close posting.</summary>
    public const string Closing = "CLOSING";

    public const string Closed = "CLOSED";

    public static readonly IReadOnlyList<string> All = [Open, Closing, Closed];
}

/// <summary>
/// Plan B 2.3 and 6.4: one teller's till for one business day. The till's cash is not stored here: it is the till
/// ledger account's entries, which start the session at zero and are sold back to the vault at close.
/// </summary>
public sealed class TillSession : AuditableEntity
{
    public long TillSessionId { get; set; }

    public long StaffUserId { get; set; }

    public int BranchId { get; set; }

    public DateOnly WorkingDate { get; set; }

    public long TellerLedgerAccountId { get; set; }

    public string Status { get; set; } = TillSessionStatuses.Open;

    public DateTime OpenedOn { get; set; }

    public DateTime? ClosedOn { get; set; }

    /// <summary>What the teller counted at close.</summary>
    public decimal? CountedAmount { get; set; }

    /// <summary>The till balance when the teller counted.</summary>
    public decimal? ExpectedAmount { get; set; }

    /// <summary>The posting that returns the cash to the vault and books any difference.</summary>
    public long? ClosePostingRequestId { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public StaffUser? StaffUser { get; set; }

    public Branch? Branch { get; set; }

    public LedgerAccount? TellerLedgerAccount { get; set; }
}

/// <summary>Plan B 6.4: the close posting. Lines that would be zero are left out.</summary>
public static class TillCloseRules
{
    /// <summary>
    /// The lines that empty the till: the counted cash goes to the vault, a shortage to cash short (Dr), an overage to
    /// cash over (Cr), and the till is credited with what it held. Null when there is nothing to post.
    /// </summary>
    public static (string TransactionCode, decimal Amount, IReadOnlyList<LedgerLine> Lines)? Plan(
        decimal expected,
        decimal counted,
        int branchId,
        long tillAccountId,
        long vaultAccountId,
        long? cashShortAccountId,
        long? cashOverAccountId)
    {
        if (expected == 0m && counted == 0m)
        {
            return null;
        }

        var lines = new List<LedgerLine>();
        if (counted > 0m)
        {
            lines.Add(new LedgerLine(vaultAccountId, branchId, EntrySides.Debit, counted));
        }

        if (expected > 0m)
        {
            lines.Add(new LedgerLine(tillAccountId, branchId, EntrySides.Credit, expected));
        }

        string code;
        if (counted < expected)
        {
            lines.Add(new LedgerLine(cashShortAccountId ?? throw new ArgumentNullException(nameof(cashShortAccountId)), branchId, EntrySides.Debit, expected - counted));
            code = TransactionCodes.TillShortage;
        }
        else if (counted > expected)
        {
            lines.Add(new LedgerLine(cashOverAccountId ?? throw new ArgumentNullException(nameof(cashOverAccountId)), branchId, EntrySides.Credit, counted - expected));
            code = TransactionCodes.TillOverage;
        }
        else
        {
            code = TransactionCodes.TillSell;
        }

        return (code, Math.Max(expected, counted), lines);
    }
}
