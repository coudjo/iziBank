namespace Banking.Application.Abstractions;

/// <summary>Plan 6.12: the working date of a branch is the date of its OPEN business day.</summary>
public interface IWorkingDateProvider
{
    /// <summary>
    /// Returns the open working date for the branch. Throws <see cref="BusinessRuleException"/> with
    /// <c>business-day.not-open</c> when the branch has no open business day.
    /// </summary>
    Task<DateOnly> GetAsync(int branchId, CancellationToken cancellationToken);
}
