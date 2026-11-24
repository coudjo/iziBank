using Banking.Domain.Ledger;
using Banking.Domain.Transactions;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Banking.Infrastructure.Inquiry;

/// <summary>One general-ledger line, as the GL inquiry shows it.</summary>
public sealed record LedgerEntryView(
    long LedgerEntryId,
    string Reference,
    DateOnly EntryDate,
    string TransactionCode,
    string? Narration,
    string CrDb,
    decimal Amount,
    DateTime PostedOn);

/// <summary>One ledger account in the trial balance: debits, credits and the net (debit minus credit).</summary>
public sealed record TrialBalanceRow(
    long LedgerAccountId,
    string LedgerAccountCode,
    string AccountName,
    decimal Debit,
    decimal Credit,
    decimal Net);

/// <summary>
/// B7-02: the branch's trial balance as at a date. <see cref="Balanced"/> is true when total debits equal total credits,
/// which is the plan's "sums to zero per branch" check (6.11, 10).
/// </summary>
public sealed record TrialBalance(
    int BranchId,
    DateOnly AsOf,
    IReadOnlyList<TrialBalanceRow> Rows,
    decimal TotalDebit,
    decimal TotalCredit,
    bool Balanced);

/// <summary>
/// B7-02: general-ledger inquiry and trial balance (plan 8, <c>LED/REPORTS</c>). Read-only. Both work from the
/// append-only <c>LedgerEntry</c> lines, so the figures are right on any day, not only after end of day.
/// </summary>
public sealed class LedgerInquiry
{
    private readonly BankingDbContext _db;

    public LedgerInquiry(BankingDbContext db) => _db = db;

    /// <summary>Newest first. Null when the ledger account does not belong to the branch.</summary>
    public async Task<PagedResult<LedgerEntryView>?> EntriesAsync(
        long ledgerAccountId,
        int branchId,
        DateOnly? from,
        DateOnly? to,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var exists = await _db.Set<LedgerAccount>().AsNoTracking()
            .AnyAsync(account => account.LedgerAccountId == ledgerAccountId && account.BranchId == branchId, cancellationToken)
            .ConfigureAwait(false);
        if (!exists)
        {
            return null;
        }

        var query = _db.Set<LedgerEntry>().AsNoTracking()
            .Where(entry => entry.LedgerAccountId == ledgerAccountId && entry.BranchId == branchId);

        if (from is { } start)
        {
            query = query.Where(entry => entry.EntryDate >= start);
        }

        if (to is { } end)
        {
            query = query.Where(entry => entry.EntryDate <= end);
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var pageRows = await query
            .OrderByDescending(entry => entry.LedgerEntryId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = await ViewsAsync(pageRows, cancellationToken).ConfigureAwait(false);
        return new PagedResult<LedgerEntryView>(items, page, pageSize, total);
    }

    /// <summary>Every ledger account of the branch with lines dated on or before <paramref name="asOf"/>.</summary>
    public async Task<TrialBalance> TrialBalanceAsync(int branchId, DateOnly asOf, CancellationToken cancellationToken)
    {
        var sums = await _db.Set<LedgerEntry>().AsNoTracking()
            .Where(entry => entry.BranchId == branchId && entry.EntryDate <= asOf)
            .GroupBy(entry => entry.LedgerAccountId)
            .Select(group => new
            {
                LedgerAccountId = group.Key,
                Debit = group.Sum(entry => entry.CrDb == EntrySides.Debit ? entry.Amount : 0m),
                Credit = group.Sum(entry => entry.CrDb == EntrySides.Credit ? entry.Amount : 0m),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var accountIds = sums.Select(sum => sum.LedgerAccountId).ToArray();
        var accounts = await _db.Set<LedgerAccount>().AsNoTracking()
            .Where(account => accountIds.Contains(account.LedgerAccountId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var accountById = accounts.ToDictionary(account => account.LedgerAccountId);

        var rows = sums
            .OrderBy(sum => accountById[sum.LedgerAccountId].LedgerAccountCode, StringComparer.Ordinal)
            .Select(sum =>
            {
                var account = accountById[sum.LedgerAccountId];
                return new TrialBalanceRow(
                    sum.LedgerAccountId,
                    account.LedgerAccountCode,
                    account.AccountName,
                    sum.Debit,
                    sum.Credit,
                    sum.Debit - sum.Credit);
            })
            .ToList();

        var totalDebit = rows.Sum(row => row.Debit);
        var totalCredit = rows.Sum(row => row.Credit);
        return new TrialBalance(branchId, asOf, rows, totalDebit, totalCredit, totalDebit == totalCredit);
    }

    /// <summary>Adds the posting reference, narration and transaction code to each line, keeping the input order.</summary>
    private async Task<List<LedgerEntryView>> ViewsAsync(List<LedgerEntry> entries, CancellationToken cancellationToken)
    {
        if (entries.Count == 0)
        {
            return [];
        }

        var postingIds = entries.Select(entry => entry.PostedTransactionId).Distinct().ToArray();
        var postings = await _db.Set<PostedTransaction>().AsNoTracking()
            .Where(posted => postingIds.Contains(posted.PostedTransactionId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var codeIds = postings.Select(posted => posted.TransactionCodeId).Distinct().ToArray();
        var codes = await _db.Set<TransactionCode>().AsNoTracking()
            .Where(code => codeIds.Contains(code.TransactionCodeId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var postingById = postings.ToDictionary(posted => posted.PostedTransactionId);
        var codeById = codes.ToDictionary(code => code.TransactionCodeId);

        return entries.Select(entry =>
        {
            var posted = postingById[entry.PostedTransactionId];
            return new LedgerEntryView(
                entry.LedgerEntryId,
                posted.Reference,
                entry.EntryDate,
                codeById[posted.TransactionCodeId].Code,
                posted.Narration,
                entry.CrDb,
                entry.Amount,
                posted.PostedOn);
        }).ToList();
    }
}
