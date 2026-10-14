namespace Banking.Domain.Security;

/// <summary>Password rules (plan section 4): minimum length, upper, lower, digit, not the staff code.</summary>
public static class SecretKeyRules
{
    public const string TooShort = "min-length";
    public const string NeedsUpper = "upper";
    public const string NeedsLower = "lower";
    public const string NeedsDigit = "digit";
    public const string SameAsStaffCode = "same-as-staff-code";

    public static IReadOnlyList<string> Check(string secretKey, string staffCode, int minLength)
    {
        ArgumentNullException.ThrowIfNull(secretKey);
        ArgumentNullException.ThrowIfNull(staffCode);

        var failures = new List<string>();

        if (secretKey.Length < minLength)
        {
            failures.Add(TooShort);
        }

        if (!secretKey.Any(char.IsUpper))
        {
            failures.Add(NeedsUpper);
        }

        if (!secretKey.Any(char.IsLower))
        {
            failures.Add(NeedsLower);
        }

        if (!secretKey.Any(char.IsDigit))
        {
            failures.Add(NeedsDigit);
        }

        if (string.Equals(secretKey, staffCode, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(SameAsStaffCode);
        }

        return failures;
    }

    /// <summary>NULL or 0 expiry days means the key never expires.</summary>
    public static bool IsExpired(DateTime? changedOnUtc, short? expiryDays, DateTime nowUtc)
    {
        if (expiryDays is null or <= 0)
        {
            return false;
        }

        return changedOnUtc is null || changedOnUtc.Value.AddDays(expiryDays.Value) <= nowUtc;
    }
}
