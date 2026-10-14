using Banking.Api.Problems;
using Banking.Api.Security;
using Banking.Application.Abstractions;
using Banking.Application.Errors;
using Banking.Domain.Lookups;
using Banking.Domain.Security;
using Banking.Domain.Supervision;
using Banking.Infrastructure.Security;

namespace Banking.Api.Endpoints;

internal sealed record RoleGroupRequest(
    string? Code,
    string? Name,
    string? Description,
    IReadOnlyList<PermissionGrant>? Permissions);

internal sealed record AssignmentRequest(int BranchId, IReadOnlyList<string>? RoleGroupCodes);

internal sealed record RejectRequest(string? Reason);

internal sealed record ChangeRequestAccepted(long ChangeRequestId);

/// <summary>
/// Role groups, assignments and their approvals (addendum section 5, tasks A2-09 and A2-10). Every change answers
/// 202 with a change request; nothing takes effect until a different supervisor approves it.
/// </summary>
internal static class RoleGroupEndpoints
{
    private const string Permissions = "PERMISSIONS";

    public static RouteGroupBuilder MapRoleGroupEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/modules", ListModulesAsync)
            .WithTags("Role groups")
            .RequirePermission(ModuleCatalog.Security, Permissions, PermissionAction.View)
            .Produces<IReadOnlyList<ModuleView>>();

        var groups = api.MapGroup("/role-groups").WithTags("Role groups");

        groups.MapGet("/", ListAsync)
            .RequirePermission(ModuleCatalog.Security, Permissions, PermissionAction.View)
            .Produces<IReadOnlyList<RoleGroupSummary>>();

        groups.MapGet("/{roleGroupId:int}", GetAsync)
            .RequirePermission(ModuleCatalog.Security, Permissions, PermissionAction.View)
            .Produces<RoleGroupDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        groups.MapGet("/{roleGroupId:int}/members", MembersAsync)
            .RequirePermission(ModuleCatalog.Security, Permissions, PermissionAction.View)
            .Produces<IReadOnlyList<RoleGroupMember>>();

        groups.MapPost("/", CreateAsync)
            .RequirePermission(ModuleCatalog.Security, Permissions, PermissionAction.Add)
            .Produces<ChangeRequestAccepted>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        groups.MapPut("/{roleGroupId:int}", UpdateAsync)
            .RequirePermission(ModuleCatalog.Security, Permissions, PermissionAction.Edit)
            .Produces<ChangeRequestAccepted>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        groups.MapPost("/{roleGroupId:int}/deactivate", DeactivateAsync)
            .RequirePermission(ModuleCatalog.Security, Permissions, PermissionAction.Edit)
            .Produces<ChangeRequestAccepted>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status409Conflict);

        api.MapPut("/staff-users/{staffUserId:long}/role-groups", AssignAsync)
            .WithTags("Role groups")
            .RequirePermission(ModuleCatalog.Security, Permissions, PermissionAction.Edit)
            .Produces<ChangeRequestAccepted>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        var changes = api.MapGroup("/change-requests").WithTags("Change requests");

        changes.MapGet("/", ListChangesAsync)
            .RequirePermission(ModuleCatalog.Security, Permissions, PermissionAction.View)
            .Produces<IReadOnlyList<ChangeRequestView>>();

        changes.MapGet("/{changeRequestId:long}", GetChangeAsync)
            .RequirePermission(ModuleCatalog.Security, Permissions, PermissionAction.View)
            .Produces<ChangeRequestView>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        changes.MapPost("/{changeRequestId:long}/approve", ApproveAsync)
            .RequirePermission(ModuleCatalog.Security, Permissions, PermissionAction.Supervise)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status409Conflict);

        changes.MapPost("/{changeRequestId:long}/reject", RejectAsync)
            .RequirePermission(ModuleCatalog.Security, Permissions, PermissionAction.Supervise)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return api;
    }

    private static async Task<IResult> ListModulesAsync(RoleGroupAdmin admin, CancellationToken cancellationToken) =>
        TypedResults.Ok(await admin.ListModulesAsync(cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> ListAsync(RoleGroupAdmin admin, CancellationToken cancellationToken) =>
        TypedResults.Ok(await admin.ListAsync(cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> GetAsync(int roleGroupId, RoleGroupAdmin admin, CancellationToken cancellationToken)
    {
        var detail = await admin.GetAsync(roleGroupId, cancellationToken).ConfigureAwait(false);
        return detail is null
            ? ApiProblems.Result(StatusCodes.Status404NotFound, ProblemCodes.NotFound)
            : TypedResults.Ok(detail);
    }

    private static async Task<IResult> MembersAsync(int roleGroupId, RoleGroupAdmin admin, CancellationToken cancellationToken) =>
        TypedResults.Ok(await admin.MembersAsync(roleGroupId, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> CreateAsync(RoleGroupRequest request, RoleGroupAdmin admin, CancellationToken cancellationToken)
    {
        var errors = Required(request, requireCode: true);
        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var result = await admin.RequestCreateAsync(
                new RoleGroupDraft(request.Code!, request.Name!, request.Description, request.Permissions!),
                cancellationToken)
            .ConfigureAwait(false);
        return Accepted(result);
    }

    private static async Task<IResult> UpdateAsync(
        int roleGroupId,
        RoleGroupRequest request,
        RoleGroupAdmin admin,
        CancellationToken cancellationToken)
    {
        var errors = Required(request, requireCode: false);
        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var result = await admin.RequestUpdateAsync(
                roleGroupId,
                new RoleGroupDraft(string.Empty, request.Name!, request.Description, request.Permissions!),
                cancellationToken)
            .ConfigureAwait(false);
        return Accepted(result);
    }

    private static async Task<IResult> DeactivateAsync(int roleGroupId, RoleGroupAdmin admin, CancellationToken cancellationToken) =>
        Accepted(await admin.RequestDeactivateAsync(roleGroupId, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> AssignAsync(
        long staffUserId,
        AssignmentRequest request,
        RoleGroupAdmin admin,
        CancellationToken cancellationToken)
    {
        if (request.RoleGroupCodes is null)
        {
            return ApiProblems.Validation(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["roleGroupCodes"] = ["List the role groups (an empty list removes them all)."]
            });
        }

        var result = await admin.RequestAssignmentsAsync(
                new AssignmentDraft(staffUserId, request.BranchId, request.RoleGroupCodes),
                cancellationToken)
            .ConfigureAwait(false);
        return Accepted(result);
    }

    private static async Task<IResult> ListChangesAsync(
        RoleGroupAdmin admin,
        IActivityContext activity,
        string? status = ChangeRequestStatuses.Pending,
        CancellationToken cancellationToken = default)
    {
        activity.Record(EventCodes.AuditView, nameof(ChangeRequest));
        var result = await admin.ListChangeRequestsAsync(status, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(result.Value);
    }

    private static async Task<IResult> GetChangeAsync(long changeRequestId, RoleGroupAdmin admin, CancellationToken cancellationToken)
    {
        var view = await admin.GetChangeRequestAsync(changeRequestId, cancellationToken).ConfigureAwait(false);
        return view is null
            ? ApiProblems.Result(StatusCodes.Status404NotFound, ProblemCodes.NotFound)
            : TypedResults.Ok(view);
    }

    private static async Task<IResult> ApproveAsync(long changeRequestId, RoleGroupAdmin admin, CancellationToken cancellationToken) =>
        Done(await admin.ApproveAsync(changeRequestId, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> RejectAsync(
        long changeRequestId,
        RejectRequest request,
        RoleGroupAdmin admin,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return ApiProblems.Validation(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["reason"] = ["Give a reason for rejecting this change."]
            });
        }

        return Done(await admin.RejectAsync(changeRequestId, request.Reason, cancellationToken).ConfigureAwait(false));
    }

    private static Dictionary<string, string[]> Required(RoleGroupRequest request, bool requireCode)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (requireCode && string.IsNullOrWhiteSpace(request.Code))
        {
            errors["code"] = ["Enter a code."];
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors["name"] = ["Enter a name."];
        }

        if (request.Permissions is null)
        {
            errors["permissions"] = ["Send the permission matrix (it may be empty)."];
        }

        return errors;
    }

    private static IResult Accepted(OperationResult<long> result) =>
        result.Succeeded
            ? TypedResults.Accepted((string?)null, new ChangeRequestAccepted(result.Value))
            : Failure(result);

    private static IResult Done(OperationResult<long> result) =>
        result.Succeeded ? TypedResults.NoContent() : Failure(result);

    private static IResult Failure(OperationResult<long> result) => result.ProblemCode switch
    {
        ProblemCodes.Validation => ApiProblems.Validation(
            result.Errors?.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal)
            ?? new Dictionary<string, string[]>(StringComparer.Ordinal)),
        ProblemCodes.NotFound => ApiProblems.Result(StatusCodes.Status404NotFound, ProblemCodes.NotFound),
        _ => ApiProblems.Result(StatusCodes.Status409Conflict, result.ProblemCode!)
    };
}
