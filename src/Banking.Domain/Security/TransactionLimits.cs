using Banking.Domain.Auditing;

namespace Banking.Domain.Security;

/// <summary>Plan B 2.2: what a transaction limit covers.</summary>
public static class TransactionLimitCodes
{
    public const string CashDeposit = "CASH_DEPOSIT";
    public const string CashWithdrawal = "CASH_WITHDRAWAL";
    public const string Transfer = "TRANSFER";

    public static readonly IReadOnlyList<string> All = [CashDeposit, CashWithdrawal, Transfer];
}

/// <summary>
/// Plan B 2.2: the largest amount members of a role group may post without approval. No row means every posting of that
/// kind waits for a supervisor. Written only when a role-group change request is approved.
/// </summary>
public sealed class RoleGroupTransactionLimit : AuditableEntity
{
    public long RoleGroupTransactionLimitId { get; set; }

    public int RoleGroupId { get; set; }

    public string LimitCode { get; set; } = string.Empty;

    public decimal MaxAmount { get; set; }

    public RoleGroup? RoleGroup { get; set; }
}

/// <summary>One limit as requested or shown.</summary>
public sealed record TransactionLimitValue(string LimitCode, decimal MaxAmount);

/// <summary>Plan B 2.2: limits are a union, like permissions; the largest across a person's approved groups wins.</summary>
public static class TransactionLimitRules
{
    /// <summary>The effective limit for one code, or null when none of the groups has one.</summary>
    public static decimal? Effective(IEnumerable<TransactionLimitValue> limits, string limitCode)
    {
        ArgumentNullException.ThrowIfNull(limits);
        decimal? best = null;
        foreach (var limit in limits.Where(limit => limit.LimitCode == limitCode))
        {
            best = best is null ? limit.MaxAmount : Math.Max(best.Value, limit.MaxAmount);
        }

        return best;
    }

    /// <summary>Field errors for a replacement set of limits, keyed for problem details.</summary>
    public static IReadOnlyDictionary<string, string[]> Validate(IReadOnlyList<TransactionLimitValue> limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        var problems = new List<string>();
        foreach (var limit in limits)
        {
            if (!TransactionLimitCodes.All.Contains(limit.LimitCode))
            {
                problems.Add($"Unknown limit {limit.LimitCode}.");
            }

            if (limit.MaxAmount <= 0 || decimal.Round(limit.MaxAmount, 2) != limit.MaxAmount || limit.MaxAmount > 999_999_999_999m)
            {
                problems.Add($"{limit.LimitCode}: enter an amount above zero with at most 2 decimal places.");
            }
        }

        if (limits.Select(limit => limit.LimitCode).Distinct(StringComparer.Ordinal).Count() != limits.Count)
        {
            problems.Add("Each limit can appear only once.");
        }

        return problems.Count == 0
            ? new Dictionary<string, string[]>(StringComparer.Ordinal)
            : new Dictionary<string, string[]>(StringComparer.Ordinal) { ["limits"] = [.. problems] };
    }
}
