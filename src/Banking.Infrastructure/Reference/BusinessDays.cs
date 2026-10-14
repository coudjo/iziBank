using System.Globalization;
using Banking.Application.Abstractions;
using Banking.Application.Errors;
using Banking.Application.Reference;
using Banking.Domain.Institution;
using Banking.Domain.Lookups;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Banking.Infrastructure.Reference;

public sealed record BusinessDayView(
    long BusinessDayStatusId,
    DateOnly WorkingDate,
    string? Status,
    DateTime? StartedOn,
    DateTime? EndedOn,
    long? OpenedByUserId,
    long? ClosedByUserId);

/// <summary>The working-date strip in the portal's top bar (UX 9, portal request P1).</summary>
public sealed record CurrentBusinessDay(int BranchId, string BranchCode, DateOnly? WorkingDate, string? Status, bool IsOpen);

/// <summary>A3-02: business day open and close per branch (plan 6.12).</summary>
public sealed class BusinessDays
{
    private readonly BankingDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IActivityContext _activity;
    private readonly LookupIdCache _lookups;
    private readonly TimeProvider _timeProvider;
    private readonly IReadOnlySet<DayOfWeek> _weekendDays;

    public BusinessDays(
        BankingDbContext db,
        ICurrentUser currentUser,
        IActivityContext activity,
        LookupIdCache lookups,
        TimeProvider timeProvider,
        IOptions<CalendarOptions> calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        _db = db;
        _currentUser = currentUser;
        _activity = activity;
        _lookups = lookups;
        _timeProvider = timeProvider;
        _weekendDays = calendar.Value.ParseWeekendDays();
    }

    public async Task<CurrentBusinessDay?> CurrentAsync(int branchId, CancellationToken cancellationToken)
    {
        var branch = await _db.Set<Branch>()
            .AsNoTracking()
            .Where(row => row.BranchId == branchId)
            .Select(row => new { row.BranchId, row.BranchCode })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (branch is null)
        {
            return null;
        }

        var latest = await _db.Set<BusinessDayStatus>()
            .AsNoTracking()
            .Where(row => row.BranchId == branchId)
            .OrderByDescending(row => row.WorkingDate)
            .Select(row => new { row.WorkingDate, Status = row.CurrentStatusId == null ? null : row.CurrentStatus!.Code })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return new CurrentBusinessDay(
            branch.BranchId,
            branch.BranchCode,
            latest?.WorkingDate,
            latest?.Status,
            latest?.Status == LookupCodes.BusinessDayStatuses.Open);
    }

    public async Task<PagedResult<BusinessDayView>?> ListAsync(int branchId, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (!await _db.Set<Branch>().AnyAsync(row => row.BranchId == branchId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var query = _db.Set<BusinessDayStatus>().AsNoTracking().Where(row => row.BranchId == branchId);
        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query
            .OrderByDescending(row => row.WorkingDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new BusinessDayView(
                row.BusinessDayStatusId,
                row.WorkingDate,
                row.CurrentStatusId == null ? null : row.CurrentStatus!.Code,
                row.BusinessDayStart,
                row.BusinessDayEnd,
                row.CreatedByUserId,
                row.CurrentStatus!.Code == LookupCodes.BusinessDayStatuses.Closed ? row.ModifiedByUserId : null))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<BusinessDayView>(items, page, pageSize, total);
    }

    /// <summary>
    /// Opens <paramref name="workingDate"/> for the branch. The branch row is locked for the check and insert, so two
    /// concurrent opens for one branch cannot both pass the "previous day closed" rule.
    /// </summary>
    public async Task<OperationResult<long>> OpenAsync(int branchId, DateOnly? workingDate, CancellationToken cancellationToken)
    {
        if (workingDate is null)
        {
            return OperationResult.Fail<long>(
                ProblemCodes.Validation,
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["workingDate"] = ["Enter the working date to open."] });
        }

        var date = workingDate.Value;
        var branch = await _db.Set<Branch>().AsNoTracking().SingleOrDefaultAsync(row => row.BranchId == branchId, cancellationToken).ConfigureAwait(false);
        if (branch is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        if (_currentUser.BranchId != branchId)
        {
            return OperationResult.Fail<long>(ProblemCodes.RecordOtherBranch);
        }

        if (!branch.IsActive)
        {
            return OperationResult.Fail<long>(
                ProblemCodes.Validation,
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["branchId"] = ["The branch is inactive."] });
        }

        var openId = await _lookups.GetIdAsync<BusinessDayStatusType>(_db, LookupCodes.BusinessDayStatuses.Open, cancellationToken).ConfigureAwait(false);

        var transaction = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            await _db.Database
                .ExecuteSqlInterpolatedAsync($"SELECT [BranchId] FROM [Branch] WITH (UPDLOCK, HOLDLOCK) WHERE [BranchId] = {branchId}", cancellationToken)
                .ConfigureAwait(false);

            var latest = await _db.Set<BusinessDayStatus>()
                .AsNoTracking()
                .Where(row => row.BranchId == branchId)
                .OrderByDescending(row => row.WorkingDate)
                .Select(row => new { row.WorkingDate, Status = row.CurrentStatusId == null ? null : row.CurrentStatus!.Code })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            var isHoliday = await _db.Set<DefinedHoliday>().AnyAsync(row => row.HolidayDate == date, cancellationToken).ConfigureAwait(false);

            var refusal = BusinessDayRules.CheckOpen(
                date,
                latest is null ? null : new LatestBusinessDay(latest.WorkingDate, latest.Status == LookupCodes.BusinessDayStatuses.Closed),
                _weekendDays,
                isHoliday);
            if (refusal != OpenDayRefusal.None)
            {
                return OperationResult.Fail<long>(refusal switch
                {
                    OpenDayRefusal.PreviousDayNotClosed => ProblemCodes.BusinessDayPreviousOpen,
                    OpenDayRefusal.DateNotAfterPrevious => ProblemCodes.BusinessDayDateNotAfter,
                    _ => ProblemCodes.BusinessDayNonWorkingDay
                });
            }

            var day = new BusinessDayStatus
            {
                BranchId = branchId,
                WorkingDate = date,
                BusinessDayStart = _timeProvider.GetUtcNow().UtcDateTime,
                CurrentStatusId = openId
            };
            _db.Set<BusinessDayStatus>().Add(day);
            _activity.Record(EventCodes.BusinessDayOpen, nameof(BusinessDayStatus), Key(branch.BranchCode, date));
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok(day.BusinessDayStatusId);
        }
    }

    /// <summary>Only flips the status to CLOSED. End-of-day processing arrives with the transactions phase.</summary>
    public async Task<OperationResult<long>> CloseAsync(int branchId, DateOnly workingDate, CancellationToken cancellationToken)
    {
        var branch = await _db.Set<Branch>().AsNoTracking().SingleOrDefaultAsync(row => row.BranchId == branchId, cancellationToken).ConfigureAwait(false);
        if (branch is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        if (_currentUser.BranchId != branchId)
        {
            return OperationResult.Fail<long>(ProblemCodes.RecordOtherBranch);
        }

        var openId = await _lookups.GetIdAsync<BusinessDayStatusType>(_db, LookupCodes.BusinessDayStatuses.Open, cancellationToken).ConfigureAwait(false);
        var closedId = await _lookups.GetIdAsync<BusinessDayStatusType>(_db, LookupCodes.BusinessDayStatuses.Closed, cancellationToken).ConfigureAwait(false);

        var day = await _db.Set<BusinessDayStatus>()
            .SingleOrDefaultAsync(row => row.BranchId == branchId && row.WorkingDate == workingDate, cancellationToken)
            .ConfigureAwait(false);
        if (day is null || day.CurrentStatusId != openId)
        {
            return OperationResult.Fail<long>(ProblemCodes.BusinessDayNotOpen);
        }

        day.CurrentStatusId = closedId;
        day.BusinessDayEnd = _timeProvider.GetUtcNow().UtcDateTime;
        _activity.Record(EventCodes.BusinessDayClose, nameof(BusinessDayStatus), Key(branch.BranchCode, workingDate));

        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            _activity.Written = false;
            return OperationResult.Fail<long>(ProblemCodes.BusinessDayNotOpen);
        }

        return OperationResult.Ok(day.BusinessDayStatusId);
    }

    private static string Key(string branchCode, DateOnly date) =>
        string.Create(CultureInfo.InvariantCulture, $"{branchCode}:{date:yyyy-MM-dd}");
}

/// <summary>Plan 6.12: <c>IWorkingDateProvider</c> reads the OPEN business day of a branch.</summary>
public sealed class WorkingDateProvider : IWorkingDateProvider
{
    private readonly BankingDbContext _db;
    private readonly LookupIdCache _lookups;

    public WorkingDateProvider(BankingDbContext db, LookupIdCache lookups)
    {
        _db = db;
        _lookups = lookups;
    }

    public async Task<DateOnly> GetAsync(int branchId, CancellationToken cancellationToken)
    {
        var openId = await _lookups.GetIdAsync<BusinessDayStatusType>(_db, LookupCodes.BusinessDayStatuses.Open, cancellationToken).ConfigureAwait(false);
        var open = await _db.Set<BusinessDayStatus>()
            .AsNoTracking()
            .Where(row => row.BranchId == branchId && row.CurrentStatusId == openId)
            .OrderByDescending(row => row.WorkingDate)
            .Select(row => (DateOnly?)row.WorkingDate)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return open ?? throw new BusinessRuleException(ProblemCodes.BusinessDayNotOpen);
    }
}
