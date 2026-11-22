using Banking.Application.Abstractions;
using Banking.Application.EndOfDay;
using Banking.Domain.Institution;
using Banking.Domain.Security;
using Banking.Infrastructure.EndOfDay;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Banking.Api.EndOfDay;

/// <summary>
/// B6: runs the end-of-day for every active branch once its working date is reached on or after <see cref="EndOfDayOptions.RunAtUtc"/>.
/// It checks once a minute. A branch that has run for its working date is not run again in this process; after a restart
/// it runs again, which is safe because every step is re-runnable. Runs as the system staff user.
/// </summary>
public sealed partial class EndOfDayService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IOptions<EndOfDayOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EndOfDayService> _logger;
    private readonly Dictionary<int, DateOnly> _ranFor = new();

    public EndOfDayService(IServiceScopeFactory scopes, IOptions<EndOfDayOptions> options, TimeProvider timeProvider, ILogger<EndOfDayService> logger)
    {
        _scopes = scopes;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await RunDueBranchesAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogRunFailed(exception);
            }
        }
    }

    private async Task RunDueBranchesAsync(CancellationToken cancellationToken)
    {
        var options = _options.Value;
        if (!options.Enabled)
        {
            return;
        }

        var runAt = TimeOnly.ParseExact(options.RunAtUtc, "HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        if (TimeOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime) < runAt)
        {
            return;
        }

        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        var workingDates = scope.ServiceProvider.GetRequiredService<IWorkingDateProvider>();
        var runner = scope.ServiceProvider.GetRequiredService<EndOfDayRunner>();

        var branches = await db.Set<Branch>()
            .AsNoTracking()
            .Where(row => row.IsActive)
            .Select(row => row.BranchId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var systemUserId = await db.Set<StaffUser>()
            .AsNoTracking()
            .Where(row => row.StaffCode == StaffCodes.System)
            .Select(row => row.StaffUserId)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var branchId in branches)
        {
            var workingDate = await workingDates.GetAsync(branchId, cancellationToken).ConfigureAwait(false);
            if (_ranFor.TryGetValue(branchId, out var ran) && ran == workingDate)
            {
                continue;
            }

            var result = await runner.RunAsync(new PostingActor(systemUserId, branchId), workingDate, cancellationToken).ConfigureAwait(false);
            if (result.ProblemCode is null)
            {
                _ranFor[branchId] = workingDate;
                LogBranchFinished(branchId, workingDate);
            }
            else
            {
                LogBranchStopped(branchId, workingDate, result.ProblemCode);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "End-of-day run failed; it will be retried on the next check.")]
    private partial void LogRunFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "End-of-day run for branch {BranchId} on {WorkingDate} finished.")]
    private partial void LogBranchFinished(int branchId, DateOnly workingDate);

    [LoggerMessage(Level = LogLevel.Warning, Message = "End-of-day run for branch {BranchId} on {WorkingDate} stopped: {ProblemCode}.")]
    private partial void LogBranchStopped(int branchId, DateOnly workingDate, string? problemCode);
}
