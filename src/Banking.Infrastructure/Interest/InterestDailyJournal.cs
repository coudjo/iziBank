using Banking.Application.Errors;
using Banking.Domain.Accounts;
using Banking.Domain.Interest;
using Banking.Domain.Ledger;
using Banking.Domain.Transactions;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Security;
using Banking.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace Banking.Infrastructure.Interest;

/// <summary>
/// B6: the daily interest journal (plan 6.9). For one working date and branch it posts, by product, the interest accrued
/// to date, rounded to cents, less what the product's expense account already holds. Carrying that difference forward keeps
/// the ledger equal to the rounded accruals at every point, so a fraction of a cent is posted on a later day rather than lost.
/// It is one ledger-only <c>INT_ACCR</c> posting per branch and day: Dr <c>INTEREST_EXPENSE</c> / Cr <c>INTEREST_PAYABLE</c>.
/// A branch's day posts once; running the same date again posts nothing.
/// </summary>
public sealed class InterestDailyJournal
{
    private readonly BankingDbContext _db;
    private readonly PostingEngine _engine;

    public InterestDailyJournal(BankingDbContext db, PostingEngine engine)
    {
        _db = db;
        _engine = engine;
    }

    /// <summary>Returns true when a journal was posted, false when there was nothing to post or the day was already posted.</summary>
    public async Task<OperationResult<bool>> PostDailyAsync(PostingActor actor, DateOnly workingDate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var codeId = await _db.Set<TransactionCode>()
            .AsNoTracking()
            .Where(row => row.Code == TransactionCodes.InterestAccrual)
            .Select(row => (int?)row.TransactionCodeId)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (codeId is null)
        {
            return OperationResult.Fail<bool>(ProblemCodes.Validation);
        }

        var alreadyPosted = await _db.Set<PostedTransaction>()
            .AsNoTracking()
            .AnyAsync(row => row.BranchId == actor.BranchId && row.EntryDate == workingDate && row.TransactionCodeId == codeId, cancellationToken)
            .ConfigureAwait(false);
        if (alreadyPosted)
        {
            return OperationResult.Ok(false);
        }

        // Accrued to date, by product, across every day up to and including the working date.
        var toDate = await _db.Set<AccountInterestAccrual>()
            .AsNoTracking()
            .Where(row => row.AccrualDate <= workingDate && row.Account!.BranchId == actor.BranchId)
            .GroupBy(row => row.Account!.ProductId)
            .Select(group => new ProductAmount(group.Key, group.Sum(row => row.Amount)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (toDate.Count == 0)
        {
            return OperationResult.Ok(false);
        }

        var productIds = toDate.Select(total => total.ProductId).ToList();
        var settings = await _db.Set<ProductLedgerSetting>()
            .AsNoTracking()
            .Where(row => productIds.Contains(row.ProductId)
                && (row.SettingCode == LedgerSettingCodes.InterestExpense || row.SettingCode == LedgerSettingCodes.InterestPayable))
            .Select(row => new ProductSetting(row.ProductId, row.SettingCode, row.LedgerAccountId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var ledger = new List<LedgerLine>();
        var amount = 0m;
        foreach (var total in toDate)
        {
            var expense = settings.SingleOrDefault(row => row.ProductId == total.ProductId && row.SettingCode == LedgerSettingCodes.InterestExpense);
            if (expense is null)
            {
                return OperationResult.FailWith<bool>(
                    ProblemCodes.LedgerSettingMissing,
                    new Dictionary<string, object?>(StringComparer.Ordinal) { ["settingCode"] = LedgerSettingCodes.InterestExpense, ["productId"] = total.ProductId });
            }

            var payable = settings.SingleOrDefault(row => row.ProductId == total.ProductId && row.SettingCode == LedgerSettingCodes.InterestPayable);
            if (payable is null)
            {
                return OperationResult.FailWith<bool>(
                    ProblemCodes.LedgerSettingMissing,
                    new Dictionary<string, object?>(StringComparer.Ordinal) { ["settingCode"] = LedgerSettingCodes.InterestPayable, ["productId"] = total.ProductId });
            }

            // The product's expense account holds what earlier days posted. The due amount is the rounded total less that.
            // One expense account per product is assumed (each product has its own setting).
            var posted = await PostedToAsync(expense.LedgerAccountId, actor.BranchId, cancellationToken).ConfigureAwait(false);
            var due = Math.Round(total.Amount, 2, MidpointRounding.AwayFromZero) - posted;
            if (due <= 0m)
            {
                continue;
            }

            ledger.Add(new LedgerLine(expense.LedgerAccountId, actor.BranchId, EntrySides.Debit, due));
            ledger.Add(new LedgerLine(payable.LedgerAccountId, actor.BranchId, EntrySides.Credit, due));
            amount += due;
        }

        if (ledger.Count == 0)
        {
            return OperationResult.Ok(false);
        }

        var draft = new PostingDraft(
            TransactionCodes.InterestAccrual,
            null,
            amount,
            "Interest accrued " + workingDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            new List<AccountLine>(),
            ledger,
            false,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["workingDate"] = workingDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
            });

        var posting = await _engine.SubmitSystemAsync(actor, draft, cancellationToken).ConfigureAwait(false);
        return posting.ProblemCode is not null
            ? OperationResult.Fail<bool>(posting.ProblemCode, posting.Errors)
            : OperationResult.Ok(true);
    }

    private async Task<decimal> PostedToAsync(long ledgerAccountId, int branchId, CancellationToken cancellationToken) =>
        await _db.Set<LedgerEntry>()
            .AsNoTracking()
            .Where(row => row.LedgerAccountId == ledgerAccountId && row.BranchId == branchId)
            .SumAsync(row => row.CrDb == EntrySides.Debit ? row.Amount : -row.Amount, cancellationToken)
            .ConfigureAwait(false);

    private sealed record ProductAmount(int ProductId, decimal Amount);

    private sealed record ProductSetting(int ProductId, string SettingCode, long LedgerAccountId);
}
