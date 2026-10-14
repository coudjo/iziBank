namespace Banking.Application.Supervision;

/// <summary>Configuration section <c>Supervision</c> (plan section 4).</summary>
public sealed class SupervisionOptions
{
    public const string SectionName = "Supervision";

    public bool Enabled { get; set; } = true;

    public bool AllowSelfApproval { get; set; }

    /// <summary>
    /// Self-approval is allowed only in Development and Test. Any other environment refuses to start.
    /// </summary>
    public static void EnsureSafeFor(SupervisionOptions options, string environmentName)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environmentName);

        if (!options.AllowSelfApproval)
        {
            return;
        }

        if (string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)
            || string.Equals(environmentName, "Test", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Supervision:AllowSelfApproval must be false in the '{environmentName}' environment.");
    }
}
