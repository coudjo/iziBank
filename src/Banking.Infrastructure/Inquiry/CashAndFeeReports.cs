using Banking.Domain.Charges;
using Banking.Domain.Accounts;
using Banking.Domain.Ledger;
using Banking.Domain.Security;
using Banking.Domain.Transactions;
using Banking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Banking.Infrastructure.Inquiry;

/// <summary>
/// One till session of the day. <see cref="Difference"/> is counted minus expected. <see cref="BookedDifference"/> is
/// what the close posting booked to cash over less cash short. <see cref="Reconciles"/> is null while either side is
/// not known yet (open, or close still waiting for approval).
/// </summary>
public sealed record TellerCashRow(
    long TillSessionId,
    string StaffCode,
    string Status,
    DateTime OpenedOn,
    DateTime? ClosedOn,
    decimal? ExpectedAmount,
    decimal? CountedAmount,
    decimal? Difference,
    decimal? BookedDifference,
    bool? Reconciles);

/// <summary>B7-03: the branch's till sessions for a working date, with totals and the number that do not reconcile.</summary>
public sealed record TellerCashReport(
    int BranchId,
    DateOnly Date,
    IReadOnlyList<TellerCashRow> Sessions,
    decimal TotalExpected,
    decimal TotalCounted,
    decimal TotalDifference,
    int Mismatches);

/// <summary>One outstanding charge arrear (B5-02): a charge the account could not pay in full.</summary>
public sealed record UncollectedFeeRow(
    long AccountChargeArrearId,
    long AccountId,
    long ProductChargeId,
    DateOnly RaisedOn,
    decimal Amount,
    decimal OutstandingAmount);

/// <summary>B7-03: arrears raised on or before the date that are still outstanding.</summary>
public sealed record UncollectedFeesReport(
    int BranchId,
    DateOnly AsOf,
    IReadOnlyList<UncollectedFeeRow> Rows,
    decimal TotalOutstanding);

/// <summary>
/// B7-03: teller cash and uncollected fees reports (plan 8). Read-only. The teller report checks each till session
/// against the close posting in the ledger.
/// </summary>
public sealed class CashAndFeeReports
{
    private readonly BankingDbContext _db;

    public CashAndFeeReports(BankingDbContext db) => _db = db;

    public async Task<TellerCashReport> TellerCashAsync(int branchId, DateOnly date, CancellationToken cancellationToken)
    {
        var sessions = await _db.Set<TillSession>().AsNoTracking()
            .Where(session => session.BranchId == branchId && session.WorkingDate == date)
            .OrderBy(session => session.TillSessionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var staffIds = sessions.Select(session => session.StaffUserId).Distinct().ToArray();
        var staff = await _db.Set<StaffUser>().AsNoTracking()
            .Where(user => staffIds.Contains(user.StaffUserId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var staffById = staff.ToDictionary(user => user.StaffUserId);

        var closeRequestIds = sessions
            .Where(session => session.ClosePostingRequestId is not null)
            .Select(session => session.ClosePostingRequestId!.Value)
            .ToArray();
        var closeRequests = await _db.Set<PostingRequest>().AsNoTracking()
            .Where(request => closeRequestIds.Contains(request.PostingRequestId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var requestById = closeRequests.ToDictionary(request => request.PostingRequestId);

        var postedIds = closeRequests
            .Where(request => request.PostedTransactionId is not null)
            .Select(request => request.PostedTransactionId!.Value)
            .ToArray();

        var shortOver = await _db.Set<SystemLedgerSetting>().AsNoTracking()
            .Where(setting => setting.BranchId == branchId
                && (setting.SettingCode == LedgerSettingCodes.CashShort || setting.SettingCode == LedgerSettingCodes.CashOver))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var shortAccountId = shortOver.SingleOrDefault(setting => setting.SettingCode == LedgerSettingCodes.CashShort)?.LedgerAccountId;
        var overAccountId = shortOver.SingleOrDefault(setting => setting.SettingCode == LedgerSettingCodes.CashOver)?.LedgerAccountId;
        var varianceAccountIds = new[] { shortAccountId, overAccountId }
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToArray();

        var varianceLines = postedIds.Length == 0 || varianceAccountIds.Length == 0
            ? new List<LedgerEntry>()
            : await _db.Set<LedgerEntry>().AsNoTracking()
                .Where(entry => postedIds.Contains(entry.PostedTransactionId) && varianceAccountIds.Contains(entry.LedgerAccountId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        var bookedByPosting = varianceLines
            .GroupBy(entry => entry.PostedTransactionId)
            .ToDictionary(group => group.Key, group => group.Sum(entry => SignedVariance(entry, shortAccountId, overAccountId)));

        var rows = sessions.Select(session =>
        {
            var difference = session.CountedAmount is { } counted && session.ExpectedAmount is { } expected
                ? counted - expected
                : (decimal?)null;

            decimal? booked = null;
            if (session.ClosePostingRequestId is { } requestId
                && requestById.TryGetValue(requestId, out var request)
                && request.PostedTransactionId is { } postedId)
            {
                booked = bookedByPosting.GetValueOrDefault(postedId);
            }

            var reconciles = difference is null || booked is null ? (bool?)null : difference == booked;

            return new TellerCashRow(
                session.TillSessionId,
                staffById.TryGetValue(session.StaffUserId, out var user) ? user.StaffCode : string.Empty,
                session.Status,
                session.OpenedOn,
                session.ClosedOn,
                session.ExpectedAmount,
                session.CountedAmount,
                difference,
                booked,
                reconciles);
        }).ToList();

        return new TellerCashReport(
            branchId,
            date,
            rows,
            rows.Sum(row => row.ExpectedAmount ?? 0m),
            rows.Sum(row => row.CountedAmount ?? 0m),
            rows.Sum(row => row.Difference ?? 0m),
            rows.Count(row => row.Reconciles == false));
    }

    public async Task<UncollectedFeesReport> UncollectedFeesAsync(int branchId, DateOnly asOf, CancellationToken cancellationToken)
    {
        var branchAccounts = _db.Set<Account>().AsNoTracking()
            .Where(account => account.BranchId == branchId)
            .Select(account => account.AccountId);

        var arrears = await _db.Set<AccountChargeArrear>().AsNoTracking()
            .Where(arrear => arrear.OutstandingAmount > 0m && arrear.RaisedOn <= asOf && branchAccounts.Contains(arrear.AccountId))
            .OrderBy(arrear => arrear.AccountChargeArrearId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = arrears
            .Select(arrear => new UncollectedFeeRow(
                arrear.AccountChargeArrearId,
                arrear.AccountId,
                arrear.ProductChargeId,
                arrear.RaisedOn,
                arrear.Amount,
                arrear.OutstandingAmount))
            .ToList();

        return new UncollectedFeesReport(branchId, asOf, rows, rows.Sum(row => row.OutstandingAmount));
    }

    /// <summary>Cash over is a credit, cash short a debit; a shortage is therefore negative.</summary>
    private static decimal SignedVariance(LedgerEntry entry, long? shortAccountId, long? overAccountId) =>
        entry.LedgerAccountId == overAccountId && entry.CrDb == EntrySides.Credit ? entry.Amount
        : entry.LedgerAccountId == shortAccountId && entry.CrDb == EntrySides.Debit ? -entry.Amount
        : 0m;
}
