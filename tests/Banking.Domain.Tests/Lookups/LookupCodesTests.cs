using Banking.Domain.Lookups;

namespace Banking.Domain.Tests.Lookups;

public sealed class LookupCodesTests
{
    [Fact]
    public void Catalog_codes_are_unique_within_each_list_and_fit_varchar_thirty()
    {
        AssertUniqueAndFit(LookupCodes.RegulatoryCategories.All);
        AssertUniqueAndFit(LookupCodes.BusinessDayStatuses.All);
        AssertUniqueAndFit(LookupCodes.LogoutReasons.All);
        AssertUniqueAndFit(LookupCodes.PartyTypes.All);
        AssertUniqueAndFit(LookupCodes.KycStatuses.All);
        AssertUniqueAndFit(LookupCodes.Genders.All);
        AssertUniqueAndFit(LookupCodes.IdentificationDocumentTypes.All);
        AssertUniqueAndFit(LookupCodes.BusinessCategories.All);
        AssertUniqueAndFit(LookupCodes.GroupMemberRoles.All);
        AssertUniqueAndFit(LookupCodes.ProductTypes.All);
        AssertUniqueAndFit(LookupCodes.RateTypes.All);
        AssertUniqueAndFit(LookupCodes.OperatingModes.All);
        AssertUniqueAndFit(LookupCodes.OperatingInstructions.All);
        AssertUniqueAndFit(LookupCodes.AccountHolderRoles.All);
        AssertUniqueAndFit(LookupCodes.CloseReasons.All);
        AssertUniqueAndFit(LookupCodes.FreezeReleaseActions.All);
        AssertUniqueAndFit(LookupCodes.LedgerAccountTypes.All);
        AssertUniqueAndFit(EventCodes.All);
    }

    [Fact]
    public void Identification_document_types_map_to_individual_or_business()
    {
        foreach (var code in LookupCodes.IdentificationDocumentTypes.Individual)
        {
            LookupCodes.IdentificationDocumentTypes
                .AppliesToPartyType(code, LookupCodes.PartyTypes.Individual)
                .ShouldBeTrue();
            LookupCodes.IdentificationDocumentTypes
                .AppliesToPartyType(code, LookupCodes.PartyTypes.Business)
                .ShouldBeFalse();
            LookupCodes.IdentificationDocumentTypes
                .AppliesToPartyType(code, LookupCodes.PartyTypes.Group)
                .ShouldBeFalse();
        }

        foreach (var code in LookupCodes.IdentificationDocumentTypes.Business)
        {
            LookupCodes.IdentificationDocumentTypes
                .AppliesToPartyType(code, LookupCodes.PartyTypes.Business)
                .ShouldBeTrue();
            LookupCodes.IdentificationDocumentTypes
                .AppliesToPartyType(code, LookupCodes.PartyTypes.Individual)
                .ShouldBeFalse();
        }
    }

    [Fact]
    public void Module_and_sub_module_codes_are_unique_and_fit_column_lengths()
    {
        var modules = ModuleCatalog.All.Select(module => module.Code).ToArray();
        modules.ShouldBe(modules.Distinct(StringComparer.Ordinal), ignoreOrder: true);
        modules.ShouldAllBe(code => code.Length > 0 && code.Length <= 20);

        var subModules = ModuleCatalog.All.SelectMany(module => module.SubModules.Select(sub => sub.Code)).ToArray();
        subModules.ShouldBe(subModules.Distinct(StringComparer.Ordinal), ignoreOrder: true);
        subModules.ShouldAllBe(code => code.Length > 0 && code.Length <= 20);
        subModules.Length.ShouldBe(29);
        ModuleCatalog.All.Count.ShouldBe(6);
    }

    [Fact]
    public void Regulatory_and_party_catalogs_match_section_seven()
    {
        LookupCodes.RegulatoryCategories.All.ShouldBe(
        [
            "MICROFINANCE_BANK",
            "SAVINGS_AND_LOANS",
            "COMMUNITY_BANK",
            "CREDIT_UNION",
            "LAST_MILE_PROVIDER"
        ]);

        LookupCodes.PartyTypes.All.ShouldBe(["INDIVIDUAL", "BUSINESS", "GROUP"]);
        LookupCodes.IdentificationDocumentTypes.Individual.ShouldBe(
            ["GHANA_CARD", "PASSPORT", "DRIVERS_LICENCE", "VOTER_ID"]);
        EventCodes.All.Count.ShouldBe(34);
    }

    private static void AssertUniqueAndFit(IReadOnlyList<string> codes)
    {
        codes.ShouldNotBeEmpty();
        codes.ShouldBe(codes.Distinct(StringComparer.Ordinal), ignoreOrder: true);
        codes.ShouldAllBe(code => code.Length > 0 && code.Length <= LookupCodes.MaxCodeLength);
    }
}
