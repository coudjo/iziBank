using System.Data.Common;
using Banking.Application.Abstractions;
using Banking.Domain.Lookups;
using Banking.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Banking.Api.Activity;

/// <summary>
/// Plan 5.6: exactly one <c>UserActivityLog</c> row per authenticated request. Mutating requests get their row
/// inside the business transaction (see <c>ActivityLogInterceptor</c>); everything else is written here, after the
/// response status is known. A failure here is logged and the request continues (sensitive reads are handled by
/// the access filter, which fails closed).
/// </summary>
internal sealed class ActivityLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ActivityLoggingMiddleware> _logger;

    public ActivityLoggingMiddleware(RequestDelegate next, ILogger<ActivityLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext http,
        IActivityContext activity,
        ICurrentUser currentUser,
        ActivityLogWriter activityWriter,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(activityWriter);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var authenticated = currentUser.StaffUserId is not null;
        var method = http.Request.Method;
        activity.HttpMethod = method;
        activity.RouteTemplate = (http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
        activity.IsMutatingRequest = authenticated
            && !HttpMethods.IsGet(method)
            && !HttpMethods.IsHead(method)
            && !HttpMethods.IsOptions(method);

        var started = timeProvider.GetTimestamp();
        var status = StatusCodes.Status500InternalServerError;
        try
        {
            await _next(http).ConfigureAwait(false);
            status = http.Response.StatusCode;
        }
        finally
        {
            if (authenticated && !activity.Written)
            {
                await WriteAsync(http, activity, currentUser, activityWriter, status, timeProvider.GetElapsedTime(started))
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task WriteAsync(
        HttpContext http,
        IActivityContext activity,
        ICurrentUser currentUser,
        ActivityLogWriter activityWriter,
        int status,
        TimeSpan duration)
    {
        var details = new Dictionary<string, object?>(activity.Details, StringComparer.Ordinal)
        {
            ["method"] = activity.HttpMethod,
            ["route"] = activity.RouteTemplate ?? http.Request.Path.Value,
            ["status"] = status,
            ["durationMs"] = (long)duration.TotalMilliseconds
        };

        var outcome = status switch
        {
            < 400 => ActivityOutcomes.Success,
            StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden => ActivityOutcomes.Denied,
            _ => ActivityOutcomes.Error
        };

        try
        {
            await activityWriter.WriteAsync(
                    new ActivityEntry(
                        activity.EventCode ?? EventCodes.ApiRequest,
                        outcome,
                        currentUser.SessionId,
                        currentUser.BranchId,
                        activity.EntityType,
                        activity.EntityId,
                        details),
                    CancellationToken.None)
                .ConfigureAwait(false);
            activity.Written = true;
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException or InvalidOperationException or TimeoutException)
        {
            ApiLog.ActivityWriteFailed(_logger, exception);
        }
    }
}
