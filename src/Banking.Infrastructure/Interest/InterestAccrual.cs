using Banking.Domain.Accounts;
using Banking.Domain.Interest;
using Banking.Domain.Lookups;
using Banking.Domain.Products;
using Banking.Domain.Transactions;
using Banking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Banking.Infrastructure.Interest;

/// <summary>What one accrual run wrote: how many accounts had days accrued, and how many account-days.</summary>
public sealed record InterestAccrualRun(int AccountsAccrued, int DaysAccrued);

/// <summary>
/// B5-03: daily interest accrual for a branch's accounts, up to a working date (plan 6.9). Each account accrues from the
/// day after its last accrual, or from the day it opened, to the working date. A day with no postings carries the last
/// closing balance, so weekends and holidays accrue on it. A day is written once: running the same date again adds
/// nothing. A closed account accrues up to the day before it closed. Frozen and dormant accounts still accrue.
/// The end-of-day run (B6) calls this; nothing else does.
/// </summary>
public sealed class InterestAccrual
{
    private readonly BankingDbContext _db;
    private readonly TimeProvider _timeProvider;

    public InterestAccrual(BankingDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<InterestAccrualRun> AccrueAsync(int branchId, DateOnly workingDate, CancellationToken cancellationToken)
    {
        var creditTypeId = await _db.Set<RateType>()
            .Where(row => row.Code == LookupCodes.RateTypes.Credit)
            .Select(row => (int?)row.Id)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (creditTypeId is null)
        {
            return new InterestAccrualRun(0, 0);
        }

        var accounts = await _db.Set<Account>()
            .AsNoTracking()
            .Where(row => row.BranchId == branchId
                          && row.Product!.InterestRates.Any(rate => rate.RateTypeId == creditTypeId))
            .Select(row => new AccrualAccount(
                row.AccountId,
                row.ProductId,
                row.DateOpened,
                row.ClosedOn,
                row.Balance!.OpeningBalance,
                row.Product!.InterestDayCount!.Code))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (accounts.Count == 0)
        {
            return new InterestAccrualRun(0, 0);
        }

        var productIds = accounts.Select(account => account.ProductId).Distinct().ToList();
        var bandRows = await _db.Set<ProductInterestRate>()
            .AsNoTracking()
            .Where(row => productIds.Contains(row.ProductId) && row.RateTypeId == creditTypeId)
            .Select(row => new RateRow(row.ProductId, row.MinimumAmount, row.MaximumAmount, row.InterestRate, row.EffectiveFrom, row.EffectiveTo))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var bandsByProduct = bandRows.GroupBy(row => row.ProductId).ToDictionary(group => group.Key, group => group.ToList());

        var accountIds = accounts.Select(account => account.AccountId).ToList();
        var lastAccrued = await _db.Set<AccountInterestAccrual>()
            .AsNoTracking()
            .Where(row => accountIds.Contains(row.AccountId))
            .GroupBy(row => row.AccountId)
            .Select(group => new { AccountId = group.Key, Last = group.Max(row => row.AccrualDate) })
            .ToDictionaryAsync(row => row.AccountId, row => row.Last, cancellationToken)
            .ConfigureAwait(false);

        var accountsAccrued = 0;
        var daysAccrued = 0;
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        foreach (var account in accounts)
        {
            var start = lastAccrued.TryGetValue(account.AccountId, out var last) ? last.AddDays(1) : account.DateOpened;
            var end = workingDate;
            if (account.ClosedOn is { } closedOn)
            {
                var lastOpenDay = DateOnly.FromDateTime(closedOn).AddDays(-1);
                if (lastOpenDay < end)
                {
                    end = lastOpenDay;
                }
            }

            if (start > end)
            {
                continue;
            }

            var entries = await _db.Set<AccountLedgerEntry>()
                .AsNoTracking()
                .Where(row => row.AccountId == account.AccountId && row.EntryDate <= end)
                .OrderBy(row => row.EntryDate)
                .ThenBy(row => row.AccountLedgerEntryId)
                .Select(row => new { row.EntryDate, row.BalanceAfter })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            List<RateRow> bands = bandsByProduct.TryGetValue(account.ProductId, out var productBands) ? productBands : new List<RateRow>();
            var yearLength = InterestRules.YearLength(account.DayCountCode);
            var balance = account.OpeningBalance;
            var index = 0;
            var rows = new List<AccountInterestAccrual>();

            for (var day = start; day <= end; day = day.AddDays(1))
            {
                while (index < entries.Count && entries[index].EntryDate <= day)
                {
                    balance = entries[index].BalanceAfter;
                    index++;
                }

                var applicable = bands
                    .Where(band => band.EffectiveFrom <= day && (band.EffectiveTo is null || band.EffectiveTo >= day))
                    .Select(band => new InterestBand(band.MinimumAmount, band.MaximumAmount, band.InterestRate));
                var rate = balance > 0m ? InterestRules.BandFor(balance, applicable)?.InterestRate ?? 0m : 0m;

                rows.Add(new AccountInterestAccrual
                {
                    AccountId = account.AccountId,
                    AccrualDate = day,
                    Balance = balance,
                    Rate = rate,
                    Amount = InterestRules.DailyAmount(balance, rate, yearLength),
                    AccruedOn = now
                });
            }

            _db.Set<AccountInterestAccrual>().AddRange(rows);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            accountsAccrued++;
            daysAccrued += rows.Count;
        }

        return new InterestAccrualRun(accountsAccrued, daysAccrued);
    }

    private sealed record AccrualAccount(
        long AccountId,
        int ProductId,
        DateOnly DateOpened,
        DateTime? ClosedOn,
        decimal OpeningBalance,
        string? DayCountCode);

    private sealed record RateRow(
        int ProductId,
        decimal? MinimumAmount,
        decimal? MaximumAmount,
        decimal InterestRate,
        DateOnly EffectiveFrom,
        DateOnly? EffectiveTo);
}
