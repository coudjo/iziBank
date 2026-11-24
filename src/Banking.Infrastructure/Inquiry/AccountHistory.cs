using Banking.Domain.Accounts;
using Banking.Domain.Transactions;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Banking.Infrastructure.Inquiry;

/// <summary>One customer ledger line, as the history and the statement show it.</summary>
public sealed record AccountEntryView(
    long AccountLedgerEntryId,
    string Reference,
    DateOnly EntryDate,
    string TransactionCode,
    string? Narration,
    string CrDb,
    decimal Amount,
    decimal BalanceAfter,
    DateTime PostedOn);

/// <summary>
/// B7-01: opening + credits - debits = closing. <see cref="Reconciles"/> is true when the last line's
/// <c>BalanceAfter</c> equals the computed closing balance (or the opening balance when there are no lines).
/// </summary>
public sealed record AccountStatement(
    long AccountId,
    DateOnly From,
    DateOnly To,
    decimal OpeningBalance,
    decimal TotalCredits,
    decimal TotalDebits,
    decimal ClosingBalance,
    bool Reconciles,
    IReadOnlyList<AccountEntryView> Entries);

/// <summary>
/// B7-01: read-only account history and statements from the append-only customer sub-ledger (plan 6.11). The opening
/// balance is the <c>BalanceAfter</c> of the last line before the period, or the account's opening balance when
/// there is none.
/// </summary>
public sealed class AccountHistory
{
    private readonly BankingDbContext _db;

    public AccountHistory(BankingDbContext db) => _db = db;

    /// <summary>Newest first. Null when the account does not exist.</summary>
    public async Task<PagedResult<AccountEntryView>?> EntriesAsync(long accountId, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (!await AccountExistsAsync(accountId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var total = await _db.Set<AccountLedgerEntry>().AsNoTracking()
            .CountAsync(entry => entry.AccountId == accountId, cancellationToken)
            .ConfigureAwait(false);

        var pageRows = await _db.Set<AccountLedgerEntry>().AsNoTracking()
            .Where(entry => entry.AccountId == accountId)
            .OrderByDescending(entry => entry.AccountLedgerEntryId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = await ViewsAsync(pageRows, cancellationToken).ConfigureAwait(false);
        return new PagedResult<AccountEntryView>(items, page, pageSize, total);
    }

    /// <summary>Lines dated from <paramref name="from"/> to <paramref name="to"/> inclusive. Null when the account does not exist.</summary>
    public async Task<AccountStatement?> StatementAsync(long accountId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (!await AccountExistsAsync(accountId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var priorBalance = await _db.Set<AccountLedgerEntry>().AsNoTracking()
            .Where(entry => entry.AccountId == accountId && entry.EntryDate < from)
            .OrderByDescending(entry => entry.AccountLedgerEntryId)
            .Select(entry => (decimal?)entry.BalanceAfter)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var openingAccount = await _db.Set<AccountBalance>().AsNoTracking()
            .Where(balance => balance.AccountId == accountId)
            .Select(balance => (decimal?)balance.OpeningBalance)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var opening = priorBalance ?? openingAccount ?? 0m;

        var inPeriod = await _db.Set<AccountLedgerEntry>().AsNoTracking()
            .Where(entry => entry.AccountId == accountId && entry.EntryDate >= from && entry.EntryDate <= to)
            .OrderBy(entry => entry.AccountLedgerEntryId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var entries = await ViewsAsync(inPeriod, cancellationToken).ConfigureAwait(false);
        var credits = entries.Where(row => row.CrDb == EntrySides.Credit).Sum(row => row.Amount);
        var debits = entries.Where(row => row.CrDb == EntrySides.Debit).Sum(row => row.Amount);
        var closing = opening + credits - debits;
        var lastBalance = entries.Count == 0 ? opening : entries[^1].BalanceAfter;

        return new AccountStatement(accountId, from, to, opening, credits, debits, closing, lastBalance == closing, entries);
    }

    /// <summary>
    /// Adds the posting reference, narration and transaction code to each ledger line. Each lookup is a plain
    /// entity query, so EF never has to translate a projection, and the input order is kept.
    /// </summary>
    private async Task<List<AccountEntryView>> ViewsAsync(List<AccountLedgerEntry> entries, CancellationToken cancellationToken)
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
            return new AccountEntryView(
                entry.AccountLedgerEntryId,
                posted.Reference,
                entry.EntryDate,
                codeById[posted.TransactionCodeId].Code,
                posted.Narration,
                entry.CrDb,
                entry.Amount,
                entry.BalanceAfter,
                posted.PostedOn);
        }).ToList();
    }

    private Task<bool> AccountExistsAsync(long accountId, CancellationToken cancellationToken) =>
        _db.Set<Account>().AsNoTracking().AnyAsync(account => account.AccountId == accountId, cancellationToken);
}
