namespace Banking.Application.Abstractions;

/// <summary>
/// A business rule refused the operation deep inside a service that cannot return a result (for example
/// <see cref="IWorkingDateProvider"/>). The API turns it into a 409 problem whose <c>type</c> is <see cref="ProblemCode"/>.
/// </summary>
public sealed class BusinessRuleException : Exception
{
    public BusinessRuleException()
        : this("unexpected")
    {
    }

    public BusinessRuleException(string problemCode)
        : base($"Business rule refused the operation: {problemCode}.")
    {
        ProblemCode = problemCode;
    }

    public BusinessRuleException(string problemCode, Exception innerException)
        : base($"Business rule refused the operation: {problemCode}.", innerException)
    {
        ProblemCode = problemCode;
    }

    public string ProblemCode { get; }
}
