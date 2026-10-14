using System.Data.Common;
using Banking.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Banking.Api.Security;

/// <summary>Every minute, closes idle and over-length sessions (plan 5.5).</summary>
internal sealed class SessionSweeperService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<SessionSweeperService> _logger;

    public SessionSweeperService(IServiceScopeFactory scopes, ILogger<SessionSweeperService> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                var scope = _scopes.CreateAsyncScope();
                await using var scopeDisposal = scope.ConfigureAwait(false);
                var admin = scope.ServiceProvider.GetRequiredService<SessionAdmin>();
                var closed = await admin.SweepAsync(stoppingToken).ConfigureAwait(false);
                if (closed > 0)
                {
                    ApiLog.SweepClosed(_logger, closed);
                }
            }
            catch (Exception exception) when (exception is DbException or DbUpdateException or InvalidOperationException or TimeoutException)
            {
                ApiLog.SweepFailed(_logger, exception);
            }
        }
    }
}
