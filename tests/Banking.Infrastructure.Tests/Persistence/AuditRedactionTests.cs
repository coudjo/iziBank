using Banking.Infrastructure.Persistence.Auditing;

namespace Banking.Infrastructure.Tests.Persistence;

public sealed class AuditRedactionTests
{
    [Theory]
    [InlineData("SecretKeyHash")]
    [InlineData("AccessSecret")]
    [InlineData("PasswordValue")]
    [InlineData("RequestHash")]
    public void Secrets_are_never_written(string property)
    {
        AuditRedaction.ValueFor(property, "abc").ShouldBe(AuditRedaction.Redacted);
    }

    [Fact]
    public void Binary_values_are_written_as_their_size()
    {
        AuditRedaction.ValueFor("ImageValue", new byte[] { 1, 2, 3 }).ShouldBe("[binary, 3 bytes]");
    }

    [Fact]
    public void Ordinary_values_pass_through()
    {
        AuditRedaction.ValueFor("FullName", "Ama Mensah").ShouldBe("Ama Mensah");
        AuditRedaction.ValueFor("SecretKeyHash", null).ShouldBeNull();
    }

    [Theory]
    [InlineData("CreatedOn")]
    [InlineData("ModifiedByUserId")]
    [InlineData("RowVersion")]
    public void Footer_and_concurrency_columns_are_left_out(string property)
    {
        AuditRedaction.IsExcluded(property).ShouldBeTrue();
    }
}
