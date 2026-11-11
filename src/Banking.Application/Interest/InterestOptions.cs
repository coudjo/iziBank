namespace Banking.Application.Interest;

/// <summary>Configuration section <c>Interest</c> (plan B section 4).</summary>
public sealed class InterestOptions
{
    public const string SectionName = "Interest";

    /// <summary>Percent of capitalized interest withheld when the product says so. Owner sets it (Q2); 0 until then.</summary>
    public decimal WithholdingTaxRate { get; set; }

    /// <summary>How capitalized interest is rounded to 2 dp: <c>HALF_AWAY_FROM_ZERO</c> (default), <c>HALF_EVEN</c> or <c>DOWN</c> (Q6).</summary>
    public string Rounding { get; set; } = "HALF_AWAY_FROM_ZERO";
}
