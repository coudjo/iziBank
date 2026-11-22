using Banking.Application.Errors;
using Banking.Infrastructure.Charges;
using Banking.Infrastructure.Interest;
using Banking.Infrastructure.Security;
using Banking.Infrastructure.Transactions;

namespace Banking.Infrastructure.EndOfDay;

/// <summary>What one end-of-day run did for a branch and working date.</summary>
public sealed record EndOfDayRun(
    int AccountsAccrued,
    int DaysAccrued,
    bool InterestJournalPosted,
    int PeriodsCapitalized,
    decimal InterestCapitalized,
    int FeesCharged,
    decimal FeesAmount,
    int FeesWaiting);

/// <summary>
/// B6: the end-of-day run for one branch and working date (plan 6.9). The steps run in this order, and each is safe to run
/// again: accrual writes each day once, the interest journal posts once per branch and day, capitalization and periodic
/// fees skip the periods they have already posted. Order matters: the day's accrual is journaled before any period that
/// ends that day is capitalized, so <c>INTEREST_PAYABLE</c> is credited before it is debited.
/// </summary>
public sealed class EndOfDayRunner
{
    private readonly InterestAccrual _accrual;
    private readonly InterestDailyJournal _journal;
    private readonly InterestCapitalization _capitalization;
    private readonly PeriodicCharges _charges;

    public EndOfDayRunner(InterestAccrual accrual, InterestDailyJournal journal, InterestCapitalization capitalization, PeriodicCharges charges)
    {
        _accrual = accrual;
        _journal = journal;
        _capitalization = capitalization;
        _charges = charges;
    }

    /// <summary>Stops at the first step that fails. Steps before it stay posted, and the next run picks up from there.</summary>
    public async Task<OperationResult<EndOfDayRun>> RunAsync(PostingActor actor, DateOnly workingDate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var accrued = await _accrual.AccrueAsync(actor.BranchId, workingDate, cancellationToken).ConfigureAwait(false);

        var journal = await _journal.PostDailyAsync(actor, workingDate, cancellationToken).ConfigureAwait(false);
        if (journal.ProblemCode is not null)
        {
            return OperationResult.Fail<EndOfDayRun>(journal.ProblemCode, journal.Errors);
        }

        var capitalized = await _capitalization.CapitalizeAsync(actor, workingDate, cancellationToken).ConfigureAwait(false);
        if (capitalized.ProblemCode is not null)
        {
            return OperationResult.Fail<EndOfDayRun>(capitalized.ProblemCode, capitalized.Errors);
        }

        var fees = await _charges.ChargeAsync(actor, workingDate, cancellationToken).ConfigureAwait(false);
        if (fees.ProblemCode is not null)
        {
            return OperationResult.Fail<EndOfDayRun>(fees.ProblemCode, fees.Errors);
        }

        return OperationResult.Ok(new EndOfDayRun(
            accrued.AccountsAccrued,
            accrued.DaysAccrued,
            journal.Value,
            capitalized.Value!.AccountsCapitalized,
            capitalized.Value.Amount,
            fees.Value!.Charged,
            fees.Value.Amount,
            fees.Value.NotCharged));
    }
}
