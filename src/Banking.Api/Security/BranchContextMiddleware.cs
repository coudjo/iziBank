using System.Globalization;
using Banking.Api.Problems;
using Banking.Application.Abstractions;
using Banking.Application.Errors;
using Banking.Domain.Lookups;
using Banking.Domain.Security;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Banking.Api.Security;

/// <summary>
/// Plan 5.5 branch switching: <c>X-Branch-Id</c> is honoured only for a user with <c>CanSwitchBranch</c> who holds
/// an approved role group in that branch.
/// </summary>
internal sealed class BranchContextMiddleware
{
    public const string ItemKey = "ib.branch";
    public const string HeaderName = "X-Branch-Id";

    private readonly RequestDelegate _next;

    public BranchContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext http,
        ICurrentUser currentUser,
        PermissionResolver permissions,
        BankingDbContext db,
        ActivityLogWriter activityWriter,
        IActivityContext activity)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(activityWriter);
        ArgumentNullException.ThrowIfNull(activity);

        var header = http.Request.Headers[HeaderName].ToString();
        if (currentUser.StaffUserId is not { } staffUserId || string.IsNullOrWhiteSpace(header))
        {
            await _next(http).ConfigureAwait(false);
            return;
        }

        var tokenBranch = currentUser.BranchId;
        if (!int.TryParse(header, NumberStyles.Integer, CultureInfo.InvariantCulture, out var requested))
        {
            await DenyAsync(http, currentUser, activityWriter, activity, tokenBranch).ConfigureAwait(false);
            return;
        }

        if (requested != tokenBranch)
        {
            var canSwitch = await db.Set<StaffUser>()
                .AsNoTracking()
                .Where(row => row.StaffUserId == staffUserId)
                .Select(row => row.CanSwitchBranch)
                .SingleOrDefaultAsync(http.RequestAborted)
                .ConfigureAwait(false);

            if (canSwitch != true
                || !await permissions.HasAssignmentInBranchAsync(staffUserId, requested, http.RequestAborted).ConfigureAwait(false))
            {
                await DenyAsync(http, currentUser, activityWriter, activity, requested).ConfigureAwait(false);
                return;
            }

            http.Items[ItemKey] = requested;
        }

        await _next(http).ConfigureAwait(false);
    }

    private static async Task DenyAsync(
        HttpContext http,
        ICurrentUser currentUser,
        ActivityLogWriter activityWriter,
        IActivityContext activity,
        int? branchId)
    {
        await activityWriter.WriteAsync(
                new ActivityEntry(
                    EventCodes.PermissionDenied,
                    ActivityOutcomes.Denied,
                    currentUser.SessionId,
                    branchId,
                    Details: new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["reason"] = "branch",
                        ["method"] = http.Request.Method,
                        ["path"] = http.Request.Path.Value
                    }),
                http.RequestAborted)
            .ConfigureAwait(false);
        activity.Written = true;

        await ApiProblems.WriteAsync(http, StatusCodes.Status403Forbidden, ProblemCodes.BranchNotAllowed).ConfigureAwait(false);
    }
}
