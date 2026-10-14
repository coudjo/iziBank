using Banking.Application.Supervision;

namespace Banking.Application.Tests.Supervision;

public sealed class SupervisionOptionsTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Self_approval_refuses_to_start_outside_development_and_test(string environment)
    {
        var options = new SupervisionOptions { AllowSelfApproval = true };

        Should.Throw<InvalidOperationException>(() => SupervisionOptions.EnsureSafeFor(options, environment));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public void Self_approval_is_accepted_in_development_and_test(string environment)
    {
        var options = new SupervisionOptions { AllowSelfApproval = true };

        Should.NotThrow(() => SupervisionOptions.EnsureSafeFor(options, environment));
    }

    [Fact]
    public void Default_options_are_safe_everywhere()
    {
        Should.NotThrow(() => SupervisionOptions.EnsureSafeFor(new SupervisionOptions(), "Production"));
    }
}
