using Banking.Domain.Security;

namespace Banking.Domain.Tests.Security;

public sealed class TransactionLimitRulesTests
{
    [Fact]
    public void The_largest_limit_for_the_code_wins_and_no_limit_means_none()
    {
        TransactionLimitValue[] limits =
        [
            new(TransactionLimitCodes.CashDeposit, 5_000m),
            new(TransactionLimitCodes.CashDeposit, 20_000m),
            new(TransactionLimitCodes.CashWithdrawal, 1_000m)
        ];

        TransactionLimitRules.Effective(limits, TransactionLimitCodes.CashDeposit).ShouldBe(20_000m);
        TransactionLimitRules.Effective(limits, TransactionLimitCodes.CashWithdrawal).ShouldBe(1_000m);
        TransactionLimitRules.Effective(limits, TransactionLimitCodes.Transfer).ShouldBeNull();
        TransactionLimitRules.Effective([], TransactionLimitCodes.Transfer).ShouldBeNull();
    }

    [Theory]
    [InlineData("CASH_DEPOSIT", 0)]
    [InlineData("CASH_DEPOSIT", -5)]
    [InlineData("CASH_DEPOSIT", 10.005)]
    [InlineData("LOAN_DISBURSEMENT", 10)]
    public void Bad_limits_are_refused(string code, double amount)
    {
        TransactionLimitRules.Validate([new(code, (decimal)amount)]).ShouldContainKey("limits");
    }

    [Fact]
    public void A_code_appears_once_and_good_limits_pass()
    {
        TransactionLimitRules.Validate([new(TransactionLimitCodes.Transfer, 10m), new(TransactionLimitCodes.Transfer, 20m)]).ShouldContainKey("limits");
        TransactionLimitRules.Validate([new(TransactionLimitCodes.Transfer, 10.50m), new(TransactionLimitCodes.CashDeposit, 5_000m)]).ShouldBeEmpty();
        TransactionLimitRules.Validate([]).ShouldBeEmpty();
    }
}
