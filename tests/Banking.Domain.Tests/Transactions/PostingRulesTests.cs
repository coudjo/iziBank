using Banking.Domain.Accounts;
using Banking.Domain.Lookups;
using Banking.Domain.Transactions;

namespace Banking.Domain.Tests.Transactions;

public sealed class PostingRulesTests
{
    private static readonly AccountPostingState Open = new(
        Closed: false,
        Approved: true,
        Frozen: false,
        DoNotAllowCredit: false,
        DoNotAllowDebit: false,
        CreditLimit: null,
        DebitLimit: null,
        Dormant: false,
        AvailableBalance: 1_000m,
        MinimumBalance: 20m);

    [Theory]
    [InlineData(0.01, true)]
    [InlineData(100, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(10.005, false)]
    public void Amounts_are_positive_with_two_decimals(double amount, bool valid) =>
        PostingRules.IsValidAmount((decimal)amount).ShouldBe(valid);

    [Fact]
    public void Checks_run_in_plan_order()
    {
        var everything = Open with { Closed = true, Approved = false, Frozen = true, DoNotAllowDebit = true, Dormant = true };
        PostingRules.Check(everything, 0m, 10m).ShouldBe(PostingRefusal.Closed);
        PostingRules.Check(everything with { Closed = false }, 0m, 10m).ShouldBe(PostingRefusal.NotApproved);
        PostingRules.Check(everything with { Closed = false, Approved = true }, 0m, 10m).ShouldBe(PostingRefusal.Frozen);
        PostingRules.Check(everything with { Closed = false, Approved = true, Frozen = false }, 0m, 10m).ShouldBe(PostingRefusal.DebitBlocked);
        PostingRules.Check(Open with { Dormant = true }, 0m, 10m).ShouldBe(PostingRefusal.Dormant);
    }

    [Fact]
    public void Dormant_and_debit_blocked_accounts_still_take_credits()
    {
        PostingRules.Check(Open with { Dormant = true, DoNotAllowDebit = true }, 50m, 0m).ShouldBe(PostingRefusal.None);
        PostingRules.Check(Open with { DoNotAllowCredit = true }, 50m, 0m).ShouldBe(PostingRefusal.CreditBlocked);
        PostingRules.Check(Open with { CreditLimit = 40m }, 50m, 0m).ShouldBe(PostingRefusal.CreditLimit);
        PostingRules.Check(Open with { DebitLimit = 40m }, 0m, 50m).ShouldBe(PostingRefusal.DebitLimit);
    }

    [Fact]
    public void The_available_balance_must_stay_at_or_above_the_product_minimum()
    {
        PostingRules.Check(Open, 0m, 980m).ShouldBe(PostingRefusal.None);
        PostingRules.Check(Open, 0m, 980.01m).ShouldBe(PostingRefusal.InsufficientFunds);
        PostingRules.Check(Open, 100m, 1_080m).ShouldBe(PostingRefusal.None);
    }

    [Fact]
    public void Each_branch_must_balance_on_its_own()
    {
        LedgerLine[] balanced =
        [
            new(1, 1, EntrySides.Debit, 100m),
            new(2, 1, EntrySides.Credit, 100m),
            new(3, 2, EntrySides.Debit, 40m),
            new(4, 2, EntrySides.Credit, 40m)
        ];
        PostingRules.UnbalancedBranches(balanced).ShouldBeEmpty();

        LedgerLine[] crossed = [new(1, 1, EntrySides.Debit, 100m), new(2, 2, EntrySides.Credit, 100m)];
        PostingRules.UnbalancedBranches(crossed).ShouldBe([1, 2]);
    }

    [Fact]
    public void Holding_reduces_available_and_releasing_restores_it()
    {
        var balance = new AccountBalance { ClearBalance = 500m };
        AccountBalanceCalculator.Recompute(balance);

        PostingRules.Hold(balance, EntrySides.Debit, 200m);
        balance.AvailableBalance.ShouldBe(300m);
        balance.TotalBalance.ShouldBe(300m);
        PostingRules.Hold(balance, EntrySides.Credit, 50m);
        balance.AvailableBalance.ShouldBe(300m);
        balance.TotalBalance.ShouldBe(350m);

        PostingRules.Release(balance, EntrySides.Debit, 200m);
        PostingRules.Release(balance, EntrySides.Credit, 50m);
        balance.AvailableBalance.ShouldBe(500m);
        balance.TotalBalance.ShouldBe(500m);

        PostingRules.Post(balance, EntrySides.Debit, 120m).ShouldBe(380m);
        PostingRules.Post(balance, EntrySides.Credit, 20m).ShouldBe(400m);
        balance.AvailableBalance.ShouldBe(400m);
    }
}

public sealed class MandateRulesTests
{
    private const string Primary = LookupCodes.AccountHolderRoles.Primary;
    private const string Joint = LookupCodes.AccountHolderRoles.Joint;
    private const string Signatory = LookupCodes.AccountHolderRoles.Signatory;
    private const string Individual = LookupCodes.PartyTypes.Individual;

    [Fact]
    public void Individuals_sign_their_own_accounts_and_businesses_sign_through_signatories()
    {
        MandateRules.Signers([new(1, Primary, Individual), new(2, Joint, Individual), new(3, Signatory, Individual)])
            .ShouldBe([1L, 2L, 3L], ignoreOrder: true);
        MandateRules.Signers([new(10, Primary, LookupCodes.PartyTypes.Business), new(3, Signatory, Individual)])
            .ShouldBe([3L]);
        MandateRules.Signers([new(1, Primary, Individual), new(9, LookupCodes.AccountHolderRoles.BeneficialOwner, Individual)])
            .ShouldBe([1L]);
    }

    [Fact]
    public void All_to_sign_needs_everyone_and_the_other_modes_one_signer()
    {
        var signers = new HashSet<long> { 1, 2 };
        MandateRules.IsSatisfied(LookupCodes.OperatingModes.EitherToSign, signers, [2]).ShouldBeTrue();
        MandateRules.IsSatisfied(LookupCodes.OperatingModes.AllToSign, signers, [2]).ShouldBeFalse();
        MandateRules.IsSatisfied(LookupCodes.OperatingModes.AllToSign, signers, [1, 2]).ShouldBeTrue();
        MandateRules.IsSatisfied(LookupCodes.OperatingModes.SingleMode, signers, [3]).ShouldBeFalse();
        MandateRules.IsSatisfied(LookupCodes.OperatingModes.SingleMode, signers, []).ShouldBeFalse();
    }
}
