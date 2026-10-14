using System.Data.Common;
using Banking.Api.Problems;
using Banking.Application.Abstractions;
using Banking.Application.Errors;
using Banking.Domain.Lookups;
using Banking.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Banking.Api.Security;

/// <summary>
/// Runs on every <c>/api/v1</c> endpoint: forced password change, <see cref="RequirePermissionAttribute"/>
/// (deny by default), and fail-closed logging for <see cref="SensitiveReadAttribute"/> endpoints.
/// </summary>
internal sealed class AccessFilter : IEndpointFilter
{
    private readonly PermissionResolver _permissions;
    private readonly ICurrentUser _currentUser;
    private readonly IActivityContext _activity;
    private readonly ActivityLogWriter _activityWriter;
    private readonly ILogger<AccessFilter> _logger;

    public AccessFilter(
        PermissionResolver permissions,
        ICurrentUser currentUser,
        IActivityContext activity,
        ActivityLogWriter activityWriter,
        ILogger<AccessFilter> logger)
    {
        _permissions = permissions;
        _currentUser = currentUser;
        _activity = activity;
        _activityWriter = activityWriter;
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var http = context.HttpContext;
        var metadata = http.GetEndpoint()?.Metadata;

        if (metadata?.GetMetadata<IAllowAnonymous>() is not null)
        {
            return await next(context).ConfigureAwait(false);
        }

        if (_currentUser.StaffUserId is not { } staffUserId || _currentUser.BranchId is not { } branchId)
        {
            return ApiProblems.Result(StatusCodes.Status401Unauthorized, ProblemCodes.Unauthenticated);
        }

        if (http.User.FindFirst(ClaimNames.MustChangeSecretKey)?.Value == "true"
            && metadata?.GetMetadata<AllowedWhileMustChangeAttribute>() is null)
        {
            await LogDeniedAsync(http, "must-change-secret-key").ConfigureAwait(false);
            return ApiProblems.Result(StatusCodes.Status403Forbidden, ProblemCodes.MustChangeSecretKey);
        }

        var required = metadata?.GetMetadata<RequirePermissionAttribute>();
        if (required is not null)
        {
            var set = await _permissions.ResolveAsync(staffUserId, branchId, http.RequestAborted).ConfigureAwait(false);
            if (!set.Allows(required.ModuleCode, required.SubModuleCode, required.Action))
            {
                await LogDeniedAsync(http, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{required.ModuleCode}/{required.SubModuleCode}/{required.Action}")).ConfigureAwait(false);
                return ApiProblems.Result(StatusCodes.Status403Forbidden, ProblemCodes.PermissionDenied);
            }
        }
        else if (metadata?.GetMetadata<AuthenticatedOnlyAttribute>() is null)
        {
            await LogDeniedAsync(http, "undeclared-endpoint").ConfigureAwait(false);
            return ApiProblems.Result(StatusCodes.Status403Forbidden, ProblemCodes.PermissionDenied);
        }

        var result = await next(context).ConfigureAwait(false);

        if (metadata?.GetMetadata<SensitiveReadAttribute>() is not null && !_activity.Written)
        {
            var status = (result as IStatusCodeHttpResult)?.StatusCode ?? StatusCodes.Status200OK;
            try
            {
                await _activityWriter.WriteAsync(
                        new ActivityEntry(
                            _activity.EventCode ?? EventCodes.ViewRecord,
                            status < 400 ? ActivityOutcomes.Success : ActivityOutcomes.Error,
                            _currentUser.SessionId,
                            branchId,
                            _activity.EntityType,
                            _activity.EntityId,
                            Details(http, status)),
                        http.RequestAborted)
                    .ConfigureAwait(false);
                _activity.Written = true;
            }
            catch (Exception exception) when (exception is DbException or DbUpdateException or InvalidOperationException or TimeoutException)
            {
                ApiLog.ActivityWriteFailed(_logger, exception);
                _activity.Written = true;
                return ApiProblems.Result(StatusCodes.Status500InternalServerError, ProblemCodes.ActivityLogUnavailable);
            }
        }

        return result;
    }

    private async Task LogDeniedAsync(HttpContext http, string required)
    {
        try
        {
            var details = Details(http, StatusCodes.Status403Forbidden);
            details["required"] = required;
            await _activityWriter.WriteAsync(
                    new ActivityEntry(
                        EventCodes.PermissionDenied,
                        ActivityOutcomes.Denied,
                        _currentUser.SessionId,
                        _currentUser.BranchId,
                        Details: details),
                    http.RequestAborted)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException or InvalidOperationException or TimeoutException)
        {
            ApiLog.ActivityWriteFailed(_logger, exception);
        }

        _activity.Written = true;
    }

    private static Dictionary<string, object?> Details(HttpContext http, int status) => new(StringComparer.Ordinal)
    {
        ["method"] = http.Request.Method,
        ["route"] = (http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText,
        ["status"] = status
    };
}
