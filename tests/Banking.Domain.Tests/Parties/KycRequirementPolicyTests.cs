using Banking.Domain.Lookups;
using Banking.Domain.Parties;

namespace Banking.Domain.Tests.Parties;

/// <summary>A4-07: one policy per party type; the signature is shown but does not block KYC.</summary>
public sealed class KycRequirementPolicyTests
{
    private static readonly KycFacts CompleteIndividual = new()
    {
        HasCreationFields = true,
        HasApprovedPhoto = true,
        HasDocumentWithImage = true,
        NextOfKinCount = 1,
        HasSourceOfFunds = true,
        HasOccupationOrSector = true
    };

    [Fact]
    public void An_individual_needs_photo_document_next_of_kin_and_funds_but_not_a_signature()
    {
        var items = KycRequirementPolicy.For(LookupCodes.PartyTypes.Individual, CompleteIndividual);
        KycRequirementPolicy.IsComplete(items).ShouldBeTrue();
        items.Single(item => item.Code == "SIGNATURE").Done.ShouldBeFalse();

        foreach (var missing in new[]
                 {
                     CompleteIndividual with { HasApprovedPhoto = false },
                     CompleteIndividual with { HasDocumentWithImage = false },
                     CompleteIndividual with { NextOfKinCount = 0 },
                     CompleteIndividual with { HasSourceOfFunds = false },
                     CompleteIndividual with { HasOccupationOrSector = false },
                     CompleteIndividual with { HasCreationFields = false }
                 })
        {
            KycRequirementPolicy.IsComplete(KycRequirementPolicy.For(LookupCodes.PartyTypes.Individual, missing)).ShouldBeFalse();
        }
    }

    [Fact]
    public void A_business_needs_a_document_TIN_owners_and_verified_major_owners()
    {
        var complete = new KycFacts { HasCreationFields = true, HasDocumentWithImage = true, HasTaxNumber = true, BeneficialOwnerCount = 2 };
        KycRequirementPolicy.IsComplete(KycRequirementPolicy.For(LookupCodes.PartyTypes.Business, complete)).ShouldBeTrue();
        KycRequirementPolicy.IsComplete(KycRequirementPolicy.For(LookupCodes.PartyTypes.Business, complete with { HasTaxNumber = false })).ShouldBeFalse();
        KycRequirementPolicy.IsComplete(KycRequirementPolicy.For(LookupCodes.PartyTypes.Business, complete with { BeneficialOwnerCount = 0 })).ShouldBeFalse();
        KycRequirementPolicy.IsComplete(KycRequirementPolicy.For(LookupCodes.PartyTypes.Business, complete with { UnverifiedMajorOwnerCount = 1 })).ShouldBeFalse();
    }

    [Fact]
    public void A_group_needs_a_chairperson_enough_members_and_all_of_them_verified()
    {
        var complete = new KycFacts { HasChairperson = true, ActiveMemberCount = 5, MinimumMembers = 5 };
        KycRequirementPolicy.IsComplete(KycRequirementPolicy.For(LookupCodes.PartyTypes.Group, complete)).ShouldBeTrue();
        KycRequirementPolicy.IsComplete(KycRequirementPolicy.For(LookupCodes.PartyTypes.Group, complete with { ActiveMemberCount = 4 })).ShouldBeFalse();
        KycRequirementPolicy.IsComplete(KycRequirementPolicy.For(LookupCodes.PartyTypes.Group, complete with { UnverifiedMemberCount = 1 })).ShouldBeFalse();
        KycRequirementPolicy.IsComplete(KycRequirementPolicy.For(LookupCodes.PartyTypes.Group, complete with { HasChairperson = false })).ShouldBeFalse();
    }

    [Theory]
    [InlineData("VERIFIED", true, "EXPIRED")]
    [InlineData("VERIFIED", false, "VERIFIED")]
    [InlineData("PENDING", true, "PENDING")]
    [InlineData("REJECTED", true, "REJECTED")]
    [InlineData(null, false, "PENDING")]
    public void Expired_is_computed_from_a_verified_status_and_an_expired_document(string? stored, bool expired, string expected) =>
        KycRequirementPolicy.EffectiveStatus(stored, expired).ShouldBe(expected);
}
