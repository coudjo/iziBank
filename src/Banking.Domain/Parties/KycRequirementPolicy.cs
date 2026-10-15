using System.Globalization;
using Banking.Domain.Lookups;

namespace Banking.Domain.Parties;

/// <summary>What is known about a party when its KYC checklist is built.</summary>
public sealed record KycFacts
{
    public bool HasCreationFields { get; init; }

    public bool HasApprovedPhoto { get; init; }

    public bool HasDocumentWithImage { get; init; }

    public int NextOfKinCount { get; init; }

    public bool HasSourceOfFunds { get; init; }

    public bool HasOccupationOrSector { get; init; }

    public bool HasTaxNumber { get; init; }

    public int BeneficialOwnerCount { get; init; }

    /// <summary>Owners at or above the threshold whose own KYC is not verified.</summary>
    public int UnverifiedMajorOwnerCount { get; init; }

    public bool HasChairperson { get; init; }

    public int ActiveMemberCount { get; init; }

    public int MinimumMembers { get; init; }

    public int UnverifiedMemberCount { get; init; }

    public bool HasEffectiveSignature { get; init; }
}

/// <summary>
/// One line of the checklist. <see cref="RequiredForKyc"/> items must all be done before verification; the others
/// are shown for information (the specimen signature is needed for accounts, not for KYC).
/// </summary>
public sealed record KycChecklistItem(string Code, string Label, bool Done, bool RequiredForKyc = true);

/// <summary>
/// Plan 6.5: what each party type needs before KYC can be verified. Placeholder lists until the Bank of Ghana field
/// list is confirmed (Q6).
/// </summary>
public static class KycRequirementPolicy
{
    public static IReadOnlyList<KycChecklistItem> For(string partyTypeCode, KycFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        return partyTypeCode switch
        {
            LookupCodes.PartyTypes.Individual =>
            [
                new("CREATION_FIELDS", "Identity, contact and ID details", facts.HasCreationFields),
                new("PHOTO", "Approved photo", facts.HasApprovedPhoto),
                new("ID_DOCUMENT_IMAGE", "Identification document with an image", facts.HasDocumentWithImage),
                new("NEXT_OF_KIN", "At least one next of kin", facts.NextOfKinCount > 0),
                new("SOURCE_OF_FUNDS", "Source of funds", facts.HasSourceOfFunds),
                new("OCCUPATION", "Occupation or sector", facts.HasOccupationOrSector),
                new("SIGNATURE", "Approved specimen signature (needed to open accounts)", facts.HasEffectiveSignature, RequiredForKyc: false)
            ],
            LookupCodes.PartyTypes.Business =>
            [
                new("CREATION_FIELDS", "Business, contact and funds details", facts.HasCreationFields),
                new("ID_DOCUMENT_IMAGE", "Business document with an image", facts.HasDocumentWithImage),
                new("TIN", "Tax identification number", facts.HasTaxNumber),
                new("BENEFICIAL_OWNERS", "At least one beneficial owner", facts.BeneficialOwnerCount > 0),
                new("MAJOR_OWNERS_VERIFIED", "Major beneficial owners are KYC verified", facts.UnverifiedMajorOwnerCount == 0)
            ],
            LookupCodes.PartyTypes.Group =>
            [
                new("CHAIRPERSON", "Chairperson", facts.HasChairperson),
                new("MINIMUM_MEMBERS", string.Create(CultureInfo.InvariantCulture, $"At least {facts.MinimumMembers} active members"), facts.ActiveMemberCount >= facts.MinimumMembers),
                new("MEMBERS_VERIFIED", "Every active member is KYC verified", facts.ActiveMemberCount > 0 && facts.UnverifiedMemberCount == 0)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(partyTypeCode), partyTypeCode, "Unknown party type.")
        };
    }

    public static bool IsComplete(IReadOnlyList<KycChecklistItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items.All(item => item.Done || !item.RequiredForKyc);
    }

    /// <summary>
    /// Plan 6.5: EXPIRED is computed. A stored VERIFIED status reads as EXPIRED while the required identification
    /// document has expired.
    /// </summary>
    public static string EffectiveStatus(string? storedStatus, bool requiredDocumentExpired) =>
        storedStatus == LookupCodes.KycStatuses.Verified && requiredDocumentExpired
            ? LookupCodes.KycStatuses.Expired
            : storedStatus ?? LookupCodes.KycStatuses.Pending;
}
