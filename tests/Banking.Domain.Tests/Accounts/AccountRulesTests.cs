using Banking.Domain.Accounts;
using Banking.Domain.Lookups;

namespace Banking.Domain.Tests.Accounts;

/// <summary>A5-03 numbers, A5-05 balance formulas, 6.10 status order.</summary>
public sealed class AccountRulesTests
{
    [Theory]
    [InlineData("10", 1L, 13, false, "1000000000001")]
    [InlineData("2001", 4567L, 13, false, "2001000004567")]
    [InlineData("10", 1L, 13, true, "1000000000017")]
    public void Numbers_are_prefix_plus_padded_sequence_of_the_configured_length(string prefix, long value, int length, bool check, string expected)
    {
        var number = AccountNumberFormat.Compose(prefix, value, length, check);
        number.ShouldBe(expected);
        number.Length.ShouldBe(length);
        if (check)
        {
            AccountNumberFormat.IsLuhnValid(number).ShouldBeTrue();
        }
    }

    [Theory]
    [InlineData("7992739871", '3')]
    [InlineData("100000000000", '9')]
    [InlineData("0", '0')]
    public void The_check_digit_is_luhn(string digits, char expected) =>
        AccountNumberFormat.LuhnCheckDigit(digits).ShouldBe(expected);

    [Fact]
    public void A_mistyped_digit_fails_the_luhn_check()
    {
        var number = AccountNumberFormat.Compose("10", 4242, 13, useCheckDigit: true);
        var wrong = number[..5] + (number[5] == '9' ? '0' : (char)(number[5] + 1)) + number[6..];
        AccountNumberFormat.IsLuhnValid(wrong).ShouldBeFalse();
    }

    [Fact]
    public void A_sequence_that_no_longer_fits_stops_rather_than_truncating() =>
        Should.Throw<InvalidOperationException>(() => AccountNumberFormat.Compose("123456", 10_000_000, 13, useCheckDigit: false));

    [Fact]
    public void Available_balance_takes_off_holds_and_unsupervised_debits_and_adds_the_overdraw_limit()
    {
        var balance = new AccountBalance
        {
            ClearBalance = 1_000m,
            FreezedAmount = 150m,
            UnsupervisedDebit = 50m,
            UnsupervisedCredit = 300m,
            OverdrawLimit = 200m
        };

        AccountBalanceCalculator.Available(balance).ShouldBe(1_000m);
        AccountBalanceCalculator.Total(balance).ShouldBe(1_250m);

        AccountBalanceCalculator.Recompute(balance);
        balance.AvailableBalance.ShouldBe(1_000m);
        balance.TotalBalance.ShouldBe(1_250m);
    }

    [Fact]
    public void Holds_can_take_the_available_balance_below_zero_without_an_overdraw_limit()
    {
        var balance = new AccountBalance { ClearBalance = 100m, FreezedAmount = 250m };
        AccountBalanceCalculator.Available(balance).ShouldBe(-150m);
        AccountBalanceCalculator.Total(balance).ShouldBe(100m);
    }

    [Fact]
    public void Unsupervised_credits_count_in_the_total_but_not_in_the_available_balance()
    {
        var balance = new AccountBalance { ClearBalance = 0m, UnsupervisedCredit = 500m };
        AccountBalanceCalculator.Available(balance).ShouldBe(0m);
        AccountBalanceCalculator.Total(balance).ShouldBe(500m);
    }

    [Theory]
    [InlineData(true, true, true, true, AccountStatuses.PendingSupervision)]
    [InlineData(false, true, true, true, AccountStatuses.Closed)]
    [InlineData(false, false, true, true, AccountStatuses.Frozen)]
    [InlineData(false, false, false, true, AccountStatuses.Dormant)]
    [InlineData(false, false, false, false, AccountStatuses.Active)]
    public void Status_takes_the_first_match_in_plan_order(bool pending, bool closed, bool frozen, bool dormant, string expected) =>
        AccountStatusRules.Derive(pending, closed, frozen, dormant).ShouldBe(expected);
}

/// <summary>A5-04: plan 6.9 rule 4 (shape by primary holder) and addendum 6.9a (who signs).</summary>
public sealed class AccountOpeningRulesTests
{
    private static readonly HashSet<long> NoMembers = [];

    [Fact]
    public void An_individual_alone_uses_the_single_mode_and_signs()
    {
        var shape = AccountOpeningRules.Check([Individual(1, Roles.Primary, "Ama Mensah")], null, NoMembers);
        shape.IsValid.ShouldBeTrue();
        shape.OperatingModeCode.ShouldBe(Modes.SingleMode);
        Sorted(shape.Signers).ShouldBe(new[] { 1L });
        shape.DefaultName.ShouldBe("Ama Mensah");
    }

    [Fact]
    public void An_individual_with_a_signatory_stays_single_and_both_sign()
    {
        var shape = AccountOpeningRules.Check([Individual(1, Roles.Primary), Individual(2, Roles.Signatory)], Modes.SingleMode, NoMembers);
        shape.IsValid.ShouldBeTrue();
        Sorted(shape.Signers).ShouldBe(new[] { 1L, 2L });
    }

    [Fact]
    public void An_individual_alone_cannot_choose_a_signing_mode()
    {
        var shape = AccountOpeningRules.Check([Individual(1, Roles.Primary)], Modes.EitherToSign, NoMembers);
        shape.Errors.ShouldContainKey("operatingModeId");
    }

    [Fact]
    public void A_joint_account_needs_either_or_all_to_sign_and_joins_the_names()
    {
        HolderSpec[] holders = [Individual(1, Roles.Primary, "Ama Mensah"), Individual(2, Roles.Joint, "Kofi Mensah")];

        AccountOpeningRules.Check(holders, null, NoMembers).Errors.ShouldContainKey("operatingModeId");
        AccountOpeningRules.Check(holders, Modes.SingleMode, NoMembers).Errors.ShouldContainKey("operatingModeId");

        var shape = AccountOpeningRules.Check(holders, Modes.AllToSign, NoMembers);
        shape.IsValid.ShouldBeTrue();
        shape.OperatingModeCode.ShouldBe(Modes.AllToSign);
        Sorted(shape.Signers).ShouldBe(new[] { 1L, 2L });
        shape.DefaultName.ShouldBe("Ama Mensah & Kofi Mensah");
    }

    [Fact]
    public void Joint_holders_and_signatories_must_be_individuals()
    {
        AccountOpeningRules.Check([Individual(1, Roles.Primary), Business(2, Roles.Joint)], Modes.EitherToSign, NoMembers)
            .Errors.ShouldContainKey("holders");
        AccountOpeningRules.Check([Individual(1, Roles.Primary), Business(2, Roles.Signatory)], null, NoMembers)
            .Errors.ShouldContainKey("holders");
    }

    [Fact]
    public void A_business_account_needs_a_signatory_and_a_signing_mode_and_the_business_does_not_sign()
    {
        AccountOpeningRules.Check([Business(1, Roles.Primary)], Modes.EitherToSign, NoMembers).Errors.ShouldContainKey("holders");
        AccountOpeningRules.Check([Business(1, Roles.Primary), Individual(2, Roles.Signatory)], null, NoMembers).Errors.ShouldContainKey("operatingModeId");
        AccountOpeningRules.Check([Business(1, Roles.Primary), Individual(2, Roles.Joint), Individual(3, Roles.Signatory)], Modes.EitherToSign, NoMembers)
            .Errors.ShouldContainKey("holders");

        var shape = AccountOpeningRules.Check([Business(1, Roles.Primary), Individual(2, Roles.Signatory)], Modes.EitherToSign, NoMembers);
        shape.IsValid.ShouldBeTrue();
        Sorted(shape.Signers).ShouldBe(new[] { 2L });
    }

    [Fact]
    public void Group_signatories_must_be_active_members()
    {
        HashSet<long> members = [2, 3];
        AccountOpeningRules.Check([Group(1, Roles.Primary), Individual(4, Roles.Signatory)], Modes.AllToSign, members)
            .Errors.ShouldContainKey("holders");
        AccountOpeningRules.Check([Group(1, Roles.Primary)], Modes.AllToSign, members).Errors.ShouldContainKey("holders");

        var shape = AccountOpeningRules.Check([Group(1, Roles.Primary), Individual(2, Roles.Signatory), Individual(3, Roles.Signatory)], Modes.AllToSign, members);
        shape.IsValid.ShouldBeTrue();
        Sorted(shape.Signers).ShouldBe(new[] { 2L, 3L });
    }

    [Fact]
    public void Beneficial_owners_of_any_type_are_allowed_and_never_sign()
    {
        var shape = AccountOpeningRules.Check([Business(1, Roles.Primary), Individual(2, Roles.Signatory), Business(3, Roles.BeneficialOwner)], Modes.EitherToSign, NoMembers);
        shape.IsValid.ShouldBeTrue();
        Sorted(shape.Signers).ShouldBe(new[] { 2L });
    }

    [Fact]
    public void There_is_exactly_one_primary_and_no_customer_twice()
    {
        AccountOpeningRules.Check([Individual(1, Roles.Joint)], null, NoMembers).Errors.ShouldContainKey("holders");
        AccountOpeningRules.Check([Individual(1, Roles.Primary), Individual(2, Roles.Primary)], null, NoMembers).Errors.ShouldContainKey("holders");
        AccountOpeningRules.Check([Individual(1, Roles.Primary), Individual(1, Roles.Signatory)], null, NoMembers).Errors.ShouldContainKey("holders");
    }

    private static long[] Sorted(IReadOnlySet<long> ids) => ids.Order().ToArray();

    private static HolderSpec Individual(long id, string role, string name = "Person") => new(id, LookupCodes.PartyTypes.Individual, role, name);

    private static HolderSpec Business(long id, string role) => new(id, LookupCodes.PartyTypes.Business, role, "Business");

    private static HolderSpec Group(long id, string role) => new(id, LookupCodes.PartyTypes.Group, role, "Group");

    private static class Roles
    {
        public const string Primary = LookupCodes.AccountHolderRoles.Primary;
        public const string Joint = LookupCodes.AccountHolderRoles.Joint;
        public const string Signatory = LookupCodes.AccountHolderRoles.Signatory;
        public const string BeneficialOwner = LookupCodes.AccountHolderRoles.BeneficialOwner;
    }

    private static class Modes
    {
        public const string SingleMode = LookupCodes.OperatingModes.SingleMode;
        public const string EitherToSign = LookupCodes.OperatingModes.EitherToSign;
        public const string AllToSign = LookupCodes.OperatingModes.AllToSign;
    }
}
