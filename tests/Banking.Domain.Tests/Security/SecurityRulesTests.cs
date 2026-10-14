using Banking.Domain.Security;

namespace Banking.Domain.Tests.Security;

public sealed class SecurityRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Permission_set_is_a_union_and_module_rows_cover_sub_modules()
    {
        var set = new PermissionSet(
        [
            new PermissionGrant("PTY", "PARTY", true, true, false, false, false),
            new PermissionGrant("PTY", "KYC", true, false, false, false, true),
            new PermissionGrant("SEC", null, true, false, true, false, false)
        ]);

        set.Allows("PTY", "PARTY", PermissionAction.Add).ShouldBeTrue();
        set.Allows("PTY", "PARTY", PermissionAction.Supervise).ShouldBeFalse();
        set.Allows("PTY", "KYC", PermissionAction.Supervise).ShouldBeTrue();
        set.Allows("PTY", null, PermissionAction.View).ShouldBeFalse();
        set.Allows("SEC", "USERS", PermissionAction.Edit).ShouldBeTrue();
        set.Allows("SEC", null, PermissionAction.Edit).ShouldBeTrue();
        set.Allows("ACC", "ACCOUNT", PermissionAction.View).ShouldBeFalse();
        PermissionSet.Empty.Allows("SEC", "USERS", PermissionAction.View).ShouldBeFalse();
    }

    [Fact]
    public void Merged_rows_or_flags_across_groups()
    {
        var set = new PermissionSet(
        [
            new PermissionGrant("PTY", "PARTY", true, true, false, false, false),
            new PermissionGrant("PTY", "PARTY", true, false, false, false, true)
        ]);

        var merged = set.Merged().ShouldHaveSingleItem();
        merged.AllowAdd.ShouldBeTrue();
        merged.AllowSupervise.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Abcdefg1", "")]
    [InlineData("abc1", "min-length,upper")]
    [InlineData("ABCDEFGH", "lower,digit")]
    [InlineData("Teller01", "same-as-staff-code")]
    public void Secret_key_rules(string candidate, string expected)
    {
        var expectedRules = expected.Split(',', StringSplitOptions.RemoveEmptyEntries);
        SecretKeyRules.Check(candidate, "TELLER01", 8).ShouldBe(expectedRules, ignoreOrder: true);
    }

    [Fact]
    public void Expiry_zero_or_null_never_expires()
    {
        SecretKeyRules.IsExpired(Now.AddYears(-5), null, Now).ShouldBeFalse();
        SecretKeyRules.IsExpired(Now.AddYears(-5), 0, Now).ShouldBeFalse();
        SecretKeyRules.IsExpired(Now.AddDays(-31), 30, Now).ShouldBeTrue();
        SecretKeyRules.IsExpired(Now.AddDays(-29), 30, Now).ShouldBeFalse();
        SecretKeyRules.IsExpired(null, 30, Now).ShouldBeTrue();
    }

    [Fact]
    public void Lockout_starts_at_the_threshold_and_ends_after_the_duration()
    {
        var duration = TimeSpan.FromMinutes(15);
        var four = Enumerable.Range(1, 4).Select(i => Now.AddMinutes(-i)).ToArray();
        LockoutPolicy.RetryAfter(four, Now, 5, duration).ShouldBeNull();

        var five = four.Append(Now.AddSeconds(-30)).ToArray();
        LockoutPolicy.RetryAfter(five, Now, 5, duration).ShouldBe(TimeSpan.FromMinutes(14.5));

        LockoutPolicy.RetryAfter(five, Now.AddMinutes(20), 5, duration).ShouldBeNull();
    }
}
