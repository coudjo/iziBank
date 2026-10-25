namespace Banking.Application.Charges;

/// <summary>Configuration section <c>Charges</c> (plan B section 4).</summary>
public sealed class ChargeOptions
{
    public const string SectionName = "Charges";

    /// <summary>
    /// The shortfall policy when a product charge does not set its own: <c>COLLECT_AVAILABLE</c>, <c>CHARGE_FULL</c>
    /// or <c>DEFER</c> (owner decision Q7).
    /// </summary>
    public string ShortfallPolicy { get; set; } = "DEFER";
}
