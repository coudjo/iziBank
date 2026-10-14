namespace Banking.Infrastructure.Persistence.Auditing;

/// <summary>
/// Plan 5.3: secrets are never written to the change audit; binary values are written as their size only.
/// </summary>
public static class AuditRedaction
{
    public const string Redacted = "[redacted]";

    /// <summary>Audit footer and concurrency columns are carried by the audit row itself, so they are left out.</summary>
    public static bool IsExcluded(string propertyName)
    {
        ArgumentNullException.ThrowIfNull(propertyName);

        return propertyName is "CreatedOn" or "CreatedByUserId" or "ModifiedOn" or "ModifiedByUserId" or "RowVersion";
    }

    public static bool IsSecret(string propertyName)
    {
        ArgumentNullException.ThrowIfNull(propertyName);

        return propertyName.EndsWith("Hash", StringComparison.OrdinalIgnoreCase)
            || propertyName.Contains("Secret", StringComparison.OrdinalIgnoreCase)
            || propertyName.Contains("Password", StringComparison.OrdinalIgnoreCase);
    }

    public static object? ValueFor(string propertyName, object? value)
    {
        ArgumentNullException.ThrowIfNull(propertyName);

        if (IsSecret(propertyName))
        {
            return value is null ? null : Redacted;
        }

        return value is byte[] bytes ? $"[binary, {bytes.Length} bytes]" : value;
    }
}
