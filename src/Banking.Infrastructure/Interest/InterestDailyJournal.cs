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
/// B6: the daily interest journal (plan 6.9). For one working date and branch it posts the day's accrued interest, by product,
/// as one ledger-only posting: Dr <c>INTEREST_EXPENSE</c> / Cr <c>INTEREST_PAYABLE</c>. Each product's total is rounded to
/// two decimals, because a posting holds whole cents. A branch's day is posted once; running the same date again posts nothing.
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

    /// <summary>Returns true when a journal was posted, false when the day had nothing to post or was already posted.</summary>
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

        var totals = await _db.Set<AccountInterestAccrual>()
            .AsNoTracking()
            .Where(row => row.AccrualDate == workingDate && row.Account!.BranchId == actor.BranchId)
            .GroupBy(row => row.Account!.ProductId)
            .Select(group => new ProductAccrual(group.Key, group.Sum(row => row.Amount)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var accrued = totals
            .Select(total => new ProductAccrual(total.ProductId, Math.Round(total.Amount, 2, MidpointRounding.AwayFromZero)))
            .Where(total => total.Amount > 0m)
            .ToList();
        if (accrued.Count == 0)
        {
            return OperationResult.Ok(false);
        }

        var productIds = accrued.Select(total => total.ProductId).ToList();
        var settings = await _db.Set<ProductLedgerSetting>()
            .AsNoTracking()
            .Where(row => productIds.Contains(row.ProductId)
                && (row.SettingCode == LedgerSettingCodes.InterestExpense || row.SettingCode == LedgerSettingCodes.InterestPayable))
            .Select(row => new ProductSetting(row.ProductId, row.SettingCode, row.LedgerAccountId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var ledger = new List<LedgerLine>();
        foreach (var total in accrued)
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

            ledger.Add(new LedgerLine(expense.LedgerAccountId, actor.BranchId, EntrySides.Debit, total.Amount));
            ledger.Add(new LedgerLine(payable.LedgerAccountId, actor.BranchId, EntrySides.Credit, total.Amount));
        }

        var amount = accrued.Sum(total => total.Amount);
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

    private sealed record ProductAccrual(int ProductId, decimal Amount);

    private sealed record ProductSetting(int ProductId, string SettingCode, long LedgerAccountId);
}
