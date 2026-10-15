namespace Banking.Application.Parties;

/// <summary>Configuration section <c>PartyNumber</c> (plan section 4).</summary>
public sealed class PartyNumberOptions
{
    public const string SectionName = "PartyNumber";

    /// <summary>Digits in a customer number, zero padded.</summary>
    public int Length { get; set; } = 8;
}

/// <summary>
/// Configuration section <c>Kyc</c>. Placeholders until compliance confirms the Bank of Ghana requirements (Q6).
/// </summary>
public sealed class KycOptions
{
    public const string SectionName = "Kyc";

    public decimal BeneficialOwnerThresholdPercent { get; set; } = 25m;

    public int MinimumGroupMembers { get; set; } = 5;

    /// <summary>Minimum age of an individual customer at creation (plan 6.2).</summary>
    public int MinimumAgeYears { get; set; } = 18;
}
