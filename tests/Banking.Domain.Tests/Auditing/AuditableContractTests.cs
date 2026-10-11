using Banking.Domain.Auditing;

namespace Banking.Domain.Tests.Auditing;

public sealed class AuditableContractTests
{
    [Fact]
    public void Footer_properties_are_nullable_except_created_on()
    {
        IAuditable auditable = new SampleAuditable
        {
            CreatedOn = new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc)
        };

        auditable.CreatedByUserId.ShouldBeNull();
        auditable.ModifiedByUserId.ShouldBeNull();
        auditable.ModifiedOn.ShouldBeNull();
        auditable.CreatedOn.Kind.ShouldBe(DateTimeKind.Utc);
    }

    private sealed class SampleAuditable : IAuditable
    {
        public long? CreatedByUserId { get; set; }

        public DateTime CreatedOn { get; set; }

        public long? ModifiedByUserId { get; set; }

        public DateTime? ModifiedOn { get; set; }
    }
}
