using Banking.Application.Errors;
using Banking.Application.Interest;
using Banking.Domain.Accounts;
using Banking.Domain.Interest;
using Banking.Domain.Ledger;
using Banking.Domain.Lookups;
using Banking.Domain.Products;
using Banking.Domain.Transactions;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Security;
using Banking.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Banking.Infrastructure.Interest;

/// <summary>What one capitalization run posted: accounts credited, gross credited and tax withheld.</summary>
public sealed record InterestCapitalizationRun(int AccountsCapitalized, decimal Amount, decimal TaxAmount);

/// <summary>
/// B5-04: capitalizes interest at the end of each capitalization period (plan 6.9, Q10). The accruals in a period are
/// summed and rounded (Q6). The customer is credited the gross, the withholding tax (when the product says so) is
/// debited back, and the ledger is Dr <c>INTEREST_PAYABLE</c> / Cr <c>WHT_PAYABLE</c>. Each posting is a system posting
/// and needs no supervisor, but it is checked like any other. A period whose rounded amount is zero is not posted, and
/// its accruals roll into the next period, so rounding is never lost.
/// </summary>
public sealed class InterestCapitalization
{
    private readonly BankingDbContext _db;
    private readonly PostingEngine _engine;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<InterestOptions> _options;

    public InterestCapitalization(BankingDbContext db, PostingEngine engine, TimeProvider timeProvider, IOptions<InterestOptions> options)
    {
        _db = db;
        _engine = engine;
        _timeProvider = timeProvider;
        _options = options;
    }

    /// <summary>The end-of-day run (B6): every open account of the branch, for each period that ended on or before the working date.</summary>
    public async Task<OperationResult<InterestCapitalizationRun>> CapitalizeAsync(PostingActor actor, DateOnly workingDate, CancellationToken cancellationToken)
    {
        var accounts = await _db.Set<Account>()
            .AsNoTracking()
            .Where(row => row.BranchId == actor.BranchId && row.ClosedOn == null && row.Product!.InterestCapitalizationFrequencyId != null)
            .Select(row => new CapitalizingAccount(row.AccountId, row.ProductId, row.BranchId, row.Product!.InterestCapitalizationFrequency!.Code, row.Product!.WithholdingTaxApplies))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var count = 0;
        var amount = 0m;
        var tax = 0m;
        foreach (var account in accounts)
        {
            var from = await NextPeriodStartAsync(account.AccountId, cancellationToken).ConfigureAwait(false);
            if (from is null)
            {
                continue;
            }

            var to = CapitalizationRules.PeriodEnd(account.FrequencyCode, from.Value);
            while (to is { } end && end <= workingDate)
            {
                var result = await CapitalizePeriodAsync(actor, account, from.Value, end, cancellationToken).ConfigureAwait(false);
                if (result.ProblemCode is not null)
                {
                    return OperationResult.Fail<InterestCapitalizationRun>(result.ProblemCode, result.Errors);
                }

                if (result.Value is { } posted)
                {
                    count++;
                    amount += posted.Amount;
                    tax += posted.TaxAmount;
                    from = end.AddDays(1);
                }

                to = CapitalizationRules.PeriodEnd(account.FrequencyCode, end.AddDays(1));
            }
        }

        return OperationResult.Ok(new InterestCapitalizationRun(count, amount, tax));
    }

    /// <summary>
    /// Closing an account capitalizes its interest first (plan 6.9). The period runs from the last capitalization to the
    /// account's last accrual, whether or not that is a period end. Called before the close transaction starts.
    /// </summary>
    public async Task<OperationResult<InterestCapitalizationRun>> CapitalizeToLastAccrualAsync(long userId, long accountId, CancellationToken cancellationToken)
    {
        var account = await _db.Set<Account>()
            .AsNoTracking()
            .Where(row => row.AccountId == accountId)
            .Select(row => new CapitalizingAccount(row.AccountId, row.ProductId, row.BranchId, row.Product!.InterestCapitalizationFrequency!.Code, row.Product!.WithholdingTaxApplies))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (account is null)
        {
            return OperationResult.Fail<InterestCapitalizationRun>(ProblemCodes.NotFound);
        }

        var from = await NextPeriodStartAsync(accountId, cancellationToken).ConfigureAwait(false);
        var last = await _db.Set<AccountInterestAccrual>()
            .AsNoTracking()
            .Where(row => row.AccountId == accountId)
            .Select(row => (DateOnly?)row.AccrualDate)
            .MaxAsync(cancellationToken)
            .ConfigureAwait(false);
        if (from is null || last is null || from.Value > last.Value)
        {
            return OperationResult.Ok(new InterestCapitalizationRun(0, 0m, 0m));
        }

        var result = await CapitalizePeriodAsync(new PostingActor(userId, account.BranchId), account, from.Value, last.Value, cancellationToken).ConfigureAwait(false);
        if (result.ProblemCode is not null)
        {
            return OperationResult.Fail<InterestCapitalizationRun>(result.ProblemCode, result.Errors);
        }

        return result.Value is { } posted
            ? OperationResult.Ok(new InterestCapitalizationRun(1, posted.Amount, posted.TaxAmount))
            : OperationResult.Ok(new InterestCapitalizationRun(0, 0m, 0m));
    }

    /// <summary>The first day not yet capitalized: the day after the last period, or the first accrual day.</summary>
    private async Task<DateOnly?> NextPeriodStartAsync(long accountId, CancellationToken cancellationToken)
    {
        var lastTo = await _db.Set<AccountInterestCapitalization>()
            .AsNoTracking()
            .Where(row => row.AccountId == accountId)
            .Select(row => (DateOnly?)row.ToDate)
            .MaxAsync(cancellationToken)
            .ConfigureAwait(false);
        if (lastTo is { } done)
        {
            return done.AddDays(1);
        }

        return await _db.Set<AccountInterestAccrual>()
            .AsNoTracking()
            .Where(row => row.AccountId == accountId)
            .Select(row => (DateOnly?)row.AccrualDate)
            .MinAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Capitalizes [from, to]: returns the posted amounts, or null when the period rounds to zero and is left to roll
    /// forward. A problem is returned when a ledger setting is missing or the posting is refused.
    /// </summary>
    private async Task<OperationResult<PeriodPosted?>> CapitalizePeriodAsync(PostingActor actor, CapitalizingAccount account, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var sum = await _db.Set<AccountInterestAccrual>()
            .AsNoTracking()
            .Where(row => row.AccountId == account.AccountId && row.AccrualDate >= from && row.AccrualDate <= to)
            .SumAsync(row => row.Amount, cancellationToken)
            .ConfigureAwait(false);

        var rounding = _options.Value.Rounding;
        var gross = CapitalizationRules.Round(sum, rounding);
        if (gross <= 0m)
        {
            return OperationResult.Ok<PeriodPosted?>(null);
        }

        var tax = account.WithholdingTaxApplies
            ? CapitalizationRules.WithholdingTax(gross, _options.Value.WithholdingTaxRate, rounding)
            : 0m;

        var payableId = await _db.Set<ProductLedgerSetting>()
            .AsNoTracking()
            .Where(row => row.ProductId == account.ProductId && row.SettingCode == LedgerSettingCodes.InterestPayable)
            .Select(row => (long?)row.LedgerAccountId)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (payableId is null)
        {
            return OperationResult.FailWith<PeriodPosted?>(
                ProblemCodes.LedgerSettingMissing,
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["settingCode"] = LedgerSettingCodes.InterestPayable, ["productId"] = account.ProductId });
        }

        var accountLines = new List<AccountLine> { new(account.AccountId, EntrySides.Credit, gross) };
        var ledger = new List<LedgerLine> { new(payableId.Value, account.BranchId, EntrySides.Debit, gross) };
        if (tax > 0m)
        {
            accountLines.Add(new AccountLine(account.AccountId, EntrySides.Debit, tax));
            var whtId = await _db.Set<SystemLedgerSetting>()
                .AsNoTracking()
                .Where(row => row.BranchId == account.BranchId && row.SettingCode == LedgerSettingCodes.WithholdingTaxPayable)
                .Select(row => (long?)row.LedgerAccountId)
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (whtId is null)
            {
                return OperationResult.FailWith<PeriodPosted?>(
                    ProblemCodes.LedgerSettingMissing,
                    new Dictionary<string, object?>(StringComparer.Ordinal) { ["settingCode"] = LedgerSettingCodes.WithholdingTaxPayable, ["branchId"] = account.BranchId });
            }

            ledger.Add(new LedgerLine(whtId.Value, account.BranchId, EntrySides.Credit, tax));
        }

        var draft = new PostingDraft(
            TransactionCodes.InterestCapitalized,
            null,
            gross,
            null,
            accountLines,
            ledger,
            false,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["fromDate"] = from.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                ["toDate"] = to.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
            });

        var outcome = await _engine.SubmitSystemAsync(actor, draft, cancellationToken).ConfigureAwait(false);
        if (outcome.ProblemCode is not null)
        {
            return OperationResult.Fail<PeriodPosted?>(outcome.ProblemCode, outcome.Errors);
        }

        if (outcome.Value?.PostedTransactionId is not { } postedId)
        {
            return OperationResult.Fail<PeriodPosted?>(ProblemCodes.Validation);
        }

        // Two steps: the posting commits first, then this row. Revisit when the end-of-day run (B6) owns the transaction.
        _db.Set<AccountInterestCapitalization>().Add(new AccountInterestCapitalization
        {
            AccountId = account.AccountId,
            FromDate = from,
            ToDate = to,
            Amount = gross,
            TaxAmount = tax,
            PostedTransactionId = postedId,
            CreatedOn = _timeProvider.GetUtcNow().UtcDateTime
        });
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return OperationResult.Ok<PeriodPosted?>(new PeriodPosted(gross, tax));
    }

    private sealed record CapitalizingAccount(
        long AccountId,
        int ProductId,
        int BranchId,
        string? FrequencyCode,
        bool WithholdingTaxApplies);

    private sealed record PeriodPosted(decimal Amount, decimal TaxAmount);
}
