namespace Banking.Domain.Auditing;

public interface ISupervisable
{
    bool IsSupervisionRequired { get; set; }

    long? SupervisedByUserId { get; set; }

    DateTime? SupervisedOn { get; set; }
}
