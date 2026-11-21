using Banking.Application.Charges;
using Banking.Application.Errors;
using Banking.Domain.Accounts;
using Banking.Domain.Charges;
using Banking.Domain.Ledger;
using Banking.Domain.Lookups;
using Banking.Domain.Supervision;
using Banking.Domain.Transactions;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Security;
using Banking.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Banking.Infrastructure.Charges;

/// <summary>What one periodic-fee run posted: fees charged, the amount debited, and months still waiting for a balance.</summary>
public sealed record PeriodicChargeRun(int Charged, decimal Amount, int NotCharged);

/// <summary>
/// B5-05: the monthly maintenance fee (plan 6.8). For each open account of the branch, each calendar month whose end is on
/// or before the working date and that has no fee yet is charged the product's flat fee. The shortfall policy decides
/// what is taken when the balance falls short (B-17). The posting is a system posting (no supervisor), checked like any other.
/// A month that charges nothing is left without a row and is retried by the next run.
/// </summary>
public sealed class PeriodicCharges
{
    private readonly BankingDbContext _db;
    private readonly PostingEngine _engine;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<ChargeOptions> _options;

    public PeriodicCharges(BankingDbContext db, PostingEngine engine, TimeProvider timeProvider, IOptions<ChargeOptions> options)
    {
        _db = db;
        _engine = engine;
        _timeProvider = timeProvider;
        _options = options;
    }

    /// <summary>The end-of-day run (B6): every open account of the branch, for each month that has ended by the working date.</summary>
    public async Task<OperationResult<PeriodicChargeRun>> ChargeAsync(PostingActor actor, DateOnly workingDate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var accounts = await _db.Set<Account>()
            .AsNoTracking()
            .Where(row => row.BranchId == actor.BranchId && row.ClosedOn == null)
            .Select(row => new ChargedAccount(row.AccountId, row.ProductId, row.BranchId, row.DateOpened))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var count = 0;
        var amount = 0m;
        var unpaid = 0;
        foreach (var account in accounts)
        {
            for (var start = new DateOnly(account.DateOpened.Year, account.DateOpened.Month, 1);
                 PeriodicFeeRules.PeriodEndOf(start) <= workingDate;
                 start = start.AddMonths(1))
            {
                var result = await ChargePeriodAsync(actor, account, start, PeriodicFeeRules.PeriodEndOf(start), cancellationToken).ConfigureAwait(false);
                if (result.ProblemCode is not null)
                {
                    return OperationResult.Fail<PeriodicChargeRun>(result.ProblemCode, result.Errors);
                }

                if (result.Value is not { } period)
                {
                    continue;
                }

                if (period.Posted)
                {
                    count++;
                    amount += period.Amount;
                }
                else if (period.Unpaid)
                {
                    unpaid++;
                }
            }
        }

        return OperationResult.Ok(new PeriodicChargeRun(count, amount, unpaid));
    }

    private async Task<OperationResult<PeriodOutcome>> ChargePeriodAsync(PostingActor actor, ChargedAccount account, DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        var charged = await _db.Set<AccountChargePeriod>()
            .AsNoTracking()
            .AnyAsync(row => row.AccountId == account.AccountId && row.PeriodStart == start, cancellationToken)
            .ConfigureAwait(false);
        if (charged)
        {
            return OperationResult.Ok(PeriodOutcome.None);
        }

        var charges = await _db.Set<ProductCharge>()
            .AsNoTracking()
            .Include(row => row.ChargeCondition)
            .Include(row => row.ShortfallPolicy)
            .Include(row => row.Tiers)
            .ThenInclude(row => row.ChargeBasis)
            .Where(row => row.ProductId == account.ProductId
                && row.ChargeCondition!.Code == LookupCodes.ChargeConditions.MonthlyMaintenance
                && row.EffectiveFrom <= end
                && (row.EffectiveTo == null || row.EffectiveTo >= end))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var charge = charges.FirstOrDefault(row => SupervisionRules.StateOf(row) != SupervisionState.Pending);
        if (charge is null)
        {
            return OperationResult.Ok(PeriodOutcome.None);
        }

        var bands = charge.Tiers
            .OrderBy(row => row.TierNumber)
            .Select(row => new ChargeBand(row.ChargeBasis!.Code, row.TransactionAmount, row.AmountOrPercentage, row.MinimumCharge, row.MaximumCharge))
            .ToList();
        var fee = PeriodicFeeRules.FlatFee(bands);
        if (fee is not { } flat)
        {
            return OperationResult.Fail<PeriodOutcome>(ProblemCodes.Validation);
        }

        if (flat <= 0m)
        {
            return OperationResult.Ok(PeriodOutcome.None);
        }

        var available = await _db.Set<AccountBalance>()
            .AsNoTracking()
            .Where(row => row.AccountId == account.AccountId)
            .Select(row => row.AvailableBalance)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var policy = ChargeShortfallRules.Resolve(charge.ShortfallPolicy?.Code, _options.Value.ShortfallPolicy);
        var outcome = ChargeShortfallRules.Apply(policy, flat, available);
        if (outcome.Charged <= 0m)
        {
            return OperationResult.Ok(PeriodOutcome.Waiting);
        }

        var incomeId = await _db.Set<ProductLedgerSetting>()
            .AsNoTracking()
            .Where(row => row.ProductId == account.ProductId && row.SettingCode == LedgerSettingCodes.ChargeIncome)
            .Select(row => (long?)row.LedgerAccountId)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (incomeId is null)
        {
            return OperationResult.FailWith<PeriodOutcome>(
                ProblemCodes.LedgerSettingMissing,
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["settingCode"] = LedgerSettingCodes.ChargeIncome, ["productId"] = account.ProductId });
        }

        var debit = outcome.Charged;
        var arrears = outcome.Arrear > 0m
            ? new List<ArrearRaise> { new(account.AccountId, charge.ProductChargeId, outcome.Arrear) }
            : null;
        var draft = new PostingDraft(
            TransactionCodes.MonthlyFee,
            null,
            debit,
            "Monthly maintenance fee " + start.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture),
            new List<AccountLine> { new(account.AccountId, EntrySides.Debit, debit, policy == LookupCodes.ChargeShortfallPolicies.ChargeFull) },
            new List<LedgerLine> { new(incomeId.Value, account.BranchId, EntrySides.Credit, debit) },
            false,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["periodStart"] = start.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                ["periodEnd"] = end.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
            },
            Arrears: arrears);

        var posting = await _engine.SubmitSystemAsync(actor, draft, cancellationToken).ConfigureAwait(false);
        if (posting.ProblemCode is not null)
        {
            return OperationResult.Fail<PeriodOutcome>(posting.ProblemCode, posting.Errors);
        }

        if (posting.Value?.PostedTransactionId is not { } postedId)
        {
            return OperationResult.Fail<PeriodOutcome>(ProblemCodes.Validation);
        }

        // Two steps: the posting commits first, then this row. If the row write fails after the posting, the next run
        // would charge the month again. Must be closed before go-live (see ADR B-20 and progress.md).
        _db.Set<AccountChargePeriod>().Add(new AccountChargePeriod
        {
            AccountId = account.AccountId,
            ProductChargeId = charge.ProductChargeId,
            PeriodStart = start,
            PeriodEnd = end,
            FeeAmount = flat,
            ChargedAmount = debit,
            PostedTransactionId = postedId,
            CreatedOn = _timeProvider.GetUtcNow().UtcDateTime
        });
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return OperationResult.Ok(new PeriodOutcome(true, false, debit));
    }

    private sealed record ChargedAccount(long AccountId, int ProductId, int BranchId, DateOnly DateOpened);

    /// <param name="Posted">A fee was posted for the month.</param>
    /// <param name="Unpaid">A fee is due but the balance takes none of it now; the next run retries.</param>
    /// <param name="Amount">The amount posted.</param>
    private sealed record PeriodOutcome(bool Posted, bool Unpaid, decimal Amount)
    {
        public static PeriodOutcome None { get; } = new(false, false, 0m);

        public static PeriodOutcome Waiting { get; } = new(false, true, 0m);
    }
}
