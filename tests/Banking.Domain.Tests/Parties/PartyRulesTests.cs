using Banking.Domain.Parties;

namespace Banking.Domain.Tests.Parties;

public sealed class PartyRulesTests
{
    [Theory]
    [InlineData(1L, 8, "00000001")]
    [InlineData(12345678L, 8, "12345678")]
    [InlineData(42L, 4, "0042")]
    public void Party_numbers_are_zero_padded(long value, int length, string expected) =>
        PartyRules.FormatPartyNumber(value, length).ShouldBe(expected);

    [Fact]
    public void A_number_that_no_longer_fits_stops_rather_than_truncating() =>
        Should.Throw<InvalidOperationException>(() => PartyRules.FormatPartyNumber(123_456_789, 8));

    [Fact]
    public void Display_name_is_first_and_last_name_with_single_spaces() =>
        PartyRules.IndividualDisplayName("  Ama ", " Serwaa  Mensah").ShouldBe("Ama Serwaa Mensah");

    [Fact]
    public void Identification_numbers_are_compared_without_spaces_and_case() =>
        PartyRules.NormalizeIdentificationNumber(" gha-123 456 789-0 ").ShouldBe("GHA-123456789-0");

    [Theory]
    [InlineData("GHA-123456789-0", "****89-0")]
    [InlineData("1234", "****")]
    [InlineData(null, "")]
    public void Lists_show_only_the_last_four_characters(string? value, string expected) =>
        PartyRules.MaskIdentificationNumber(value).ShouldBe(expected);

    [Theory]
    [InlineData("2008-10-14", "2026-10-14", 18)]
    [InlineData("2008-10-15", "2026-10-14", 17)]
    [InlineData("2008-02-29", "2026-02-28", 17)]
    [InlineData("2008-02-29", "2026-03-01", 18)]
    public void Age_counts_whole_years(string born, string on, int expected) =>
        PartyRules.AgeOn(DateOnly.Parse(born, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(on, System.Globalization.CultureInfo.InvariantCulture))
            .ShouldBe(expected);

    [Theory]
    [InlineData("024 123 4567", "0241234567")]
    [InlineData("+233-24-123-4567", "+233241234567")]
    [InlineData("12345", "")]
    [InlineData("02412abc67", "")]
    [InlineData("  ", null)]
    public void Phone_numbers_are_compacted_and_checked(string value, string? expected) =>
        PartyRules.NormalizePhone(value).ShouldBe(expected);
}
