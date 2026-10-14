using Banking.Api.Problems;
using Banking.Api.Security;
using Banking.Application.Abstractions;
using Banking.Application.Errors;
using Banking.Domain.Lookups;
using Banking.Domain.Security;
using Banking.Infrastructure.Security;

namespace Banking.Api.Endpoints;

/// <summary>Session viewer and forced logout (SEC/SESSIONS).</summary>
internal static class SessionEndpoints
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    public static RouteGroupBuilder MapSessionEndpoints(this RouteGroupBuilder api)
    {
        var sessions = api.MapGroup("/sessions").WithTags("Sessions");

        sessions.MapGet("/", ListAsync)
            .RequirePermission(ModuleCatalog.Security, "SESSIONS", PermissionAction.View)
            .SensitiveRead()
            .Produces<PagedResult<SessionRow>>();

        sessions.MapPost("/{sessionId:long}/end", EndAsync)
            .RequirePermission(ModuleCatalog.Security, "SESSIONS", PermissionAction.Edit)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return api;
    }

    private static async Task<IResult> ListAsync(
        SessionAdmin admin,
        IActivityContext activity,
        bool openOnly = true,
        int page = 1,
        int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        activity.Record(EventCodes.AuditView, nameof(UserSession));
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var result = await admin.ListAsync(openOnly, page, pageSize, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(result);
    }

    private static async Task<IResult> EndAsync(
        long sessionId,
        SessionAdmin admin,
        IActivityContext activity,
        CancellationToken cancellationToken)
    {
        activity.Record(EventCodes.UserAdmin, nameof(UserSession), sessionId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return await admin.ForceEndAsync(sessionId, cancellationToken).ConfigureAwait(false)
            ? TypedResults.NoContent()
            : ApiProblems.Result(StatusCodes.Status409Conflict, ProblemCodes.SessionNotOpen);
    }
}
