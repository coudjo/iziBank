namespace Banking.Domain.Auditing;

public abstract class AuditableEntity : IAuditable
{
    public long? CreatedByUserId { get; set; }

    public DateTime CreatedOn { get; set; }

    public long? ModifiedByUserId { get; set; }

    public DateTime? ModifiedOn { get; set; }
}
