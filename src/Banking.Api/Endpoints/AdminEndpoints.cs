using Banking.Api.Problems;
using Banking.Api.Security;
using Banking.Application.Abstractions;
using Banking.Application.Errors;
using Banking.Domain.Lookups;
using Banking.Domain.Security;
using Banking.Infrastructure.Reference;
using Banking.Infrastructure.Security;

namespace Banking.Api.Endpoints;

internal sealed record CreateLookupRequest(string? Code, string? Name);

internal sealed record UpdateLookupRequest(string? Code, string? Name, bool IsActive = true);

internal sealed record CreateStaffRequest(string? StaffCode, string? FullName, int BranchId, bool CanSwitchBranch, string? InitialSecretKey);

internal sealed record UpdateStaffRequest(string? FullName, bool CanSwitchBranch, bool IsDisabled, short? SecretKeyExpiryDays);

internal sealed record ResetSecretKeyRequest(string? NewSecretKey);

internal sealed record CreatedId(long Id);

/// <summary>Lookups (A2-03), staff users and audit viewers (A2-08).</summary>
internal static class AdminEndpoints
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    public static RouteGroupBuilder MapAdminEndpoints(this RouteGroupBuilder api)
    {
        var lookups = api.MapGroup("/lookups").WithTags("Lookups");

        lookups.MapGet("/", () => TypedResults.Ok(LookupAdmin.Types))
            .AuthenticatedOnly()
            .Produces<IReadOnlyList<LookupTypeInfo>>();

        lookups.MapGet("/{type}", ListLookupAsync)
            .AuthenticatedOnly()
            .Produces<IReadOnlyList<LookupRow>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        lookups.MapPost("/{type}", CreateLookupAsync)
            .RequirePermission(ModuleCatalog.ReferenceData, "LOOKUPS", PermissionAction.Add)
            .Produces<CreatedId>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        lookups.MapPut("/{type}/{id:int}", UpdateLookupAsync)
            .RequirePermission(ModuleCatalog.ReferenceData, "LOOKUPS", PermissionAction.Edit)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        var staff = api.MapGroup("/staff-users").WithTags("Staff users");

        staff.MapGet("/", ListStaffAsync)
            .RequirePermission(ModuleCatalog.Security, "USERS", PermissionAction.View)
            .Produces<PagedResult<StaffSummary>>();

        staff.MapGet("/{staffUserId:long}", GetStaffAsync)
            .RequirePermission(ModuleCatalog.Security, "USERS", PermissionAction.View)
            .Produces<StaffDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        staff.MapPost("/", CreateStaffAsync)
            .RequirePermission(ModuleCatalog.Security, "USERS", PermissionAction.Add)
            .Produces<CreatedId>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        staff.MapPut("/{staffUserId:long}", UpdateStaffAsync)
            .RequirePermission(ModuleCatalog.Security, "USERS", PermissionAction.Edit)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        staff.MapPost("/{staffUserId:long}/reset-secret-key", ResetSecretKeyAsync)
            .RequirePermission(ModuleCatalog.Security, "USERS", PermissionAction.Edit)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        var audit = api.MapGroup("/audit").WithTags("Audit");

        audit.MapGet("/activity", ActivityAsync)
            .RequirePermission(ModuleCatalog.Security, "AUDIT", PermissionAction.View)
            .SensitiveRead()
            .Produces<PagedResult<ActivityRow>>();

        audit.MapGet("/changes", ChangesAsync)
            .RequirePermission(ModuleCatalog.Security, "AUDIT", PermissionAction.View)
            .SensitiveRead()
            .Produces<PagedResult<ChangeRow>>();

        return api;
    }

    private static async Task<IResult> ListLookupAsync(
        string type,
        LookupAdmin admin,
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var task = admin.ListAsync(type, includeInactive, cancellationToken);
        return task is null
            ? ApiProblems.Result(StatusCodes.Status404NotFound, ProblemCodes.NotFound)
            : TypedResults.Ok(await task.ConfigureAwait(false));
    }

    private static async Task<IResult> CreateLookupAsync(
        string type,
        CreateLookupRequest request,
        LookupAdmin admin,
        CancellationToken cancellationToken)
    {
        var result = await admin.CreateAsync(type, request.Code, request.Name, cancellationToken).ConfigureAwait(false);
        return result.Succeeded
            ? TypedResults.Created((string?)null, new CreatedId(result.Value))
            : Failure(result.ProblemCode!, result.Errors);
    }

    private static async Task<IResult> UpdateLookupAsync(
        string type,
        int id,
        UpdateLookupRequest request,
        LookupAdmin admin,
        CancellationToken cancellationToken)
    {
        var result = await admin.UpdateAsync(type, id, request.Code, request.Name, request.IsActive, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? TypedResults.NoContent() : Failure(result.ProblemCode!, result.Errors);
    }

    private static async Task<IResult> ListStaffAsync(
        StaffAdmin admin,
        string? search = null,
        int page = 1,
        int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default) =>
        TypedResults.Ok(await admin.ListAsync(search, Math.Max(1, page), Math.Clamp(pageSize, 1, MaxPageSize), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> GetStaffAsync(long staffUserId, StaffAdmin admin, CancellationToken cancellationToken)
    {
        var detail = await admin.GetAsync(staffUserId, cancellationToken).ConfigureAwait(false);
        return detail is null ? ApiProblems.Result(StatusCodes.Status404NotFound, ProblemCodes.NotFound) : TypedResults.Ok(detail);
    }

    private static async Task<IResult> CreateStaffAsync(CreateStaffRequest request, StaffAdmin admin, CancellationToken cancellationToken)
    {
        var result = await admin.CreateAsync(
                new NewStaff(request.StaffCode ?? string.Empty, request.FullName ?? string.Empty, request.BranchId, request.CanSwitchBranch, request.InitialSecretKey ?? string.Empty),
                cancellationToken)
            .ConfigureAwait(false);
        return result.Succeeded
            ? TypedResults.Created((string?)null, new CreatedId(result.Value))
            : Failure(result.ProblemCode!, result.Errors);
    }

    private static async Task<IResult> UpdateStaffAsync(
        long staffUserId,
        UpdateStaffRequest request,
        StaffAdmin admin,
        CancellationToken cancellationToken)
    {
        var result = await admin.UpdateAsync(
                staffUserId,
                new StaffChanges(request.FullName ?? string.Empty, request.CanSwitchBranch, request.IsDisabled, request.SecretKeyExpiryDays),
                cancellationToken)
            .ConfigureAwait(false);
        return result.Succeeded ? TypedResults.NoContent() : Failure(result.ProblemCode!, result.Errors);
    }

    private static async Task<IResult> ResetSecretKeyAsync(
        long staffUserId,
        ResetSecretKeyRequest request,
        StaffAdmin admin,
        CancellationToken cancellationToken)
    {
        var result = await admin.ResetSecretKeyAsync(staffUserId, request.NewSecretKey ?? string.Empty, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? TypedResults.NoContent() : Failure(result.ProblemCode!, result.Errors);
    }

    private static async Task<IResult> ActivityAsync(
        AuditQueries queries,
        IActivityContext activity,
        long? staffUserId = null,
        long? sessionId = null,
        string? eventCode = null,
        string? entityType = null,
        string? entityId = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        activity.Record(EventCodes.AuditView, nameof(UserActivityLog));
        var result = await queries.ActivityAsync(
                new ActivityFilter(staffUserId, sessionId, eventCode, entityType, entityId, from, to),
                Math.Max(1, page),
                Math.Clamp(pageSize, 1, MaxPageSize),
                cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(result);
    }

    private static async Task<IResult> ChangesAsync(
        AuditQueries queries,
        IActivityContext activity,
        string? tableName = null,
        string? recordId = null,
        long? staffUserId = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        activity.Record(EventCodes.AuditView, nameof(DataChangeAuditLog));
        var result = await queries.ChangesAsync(
                new ChangeFilter(tableName, recordId, staffUserId, from, to),
                Math.Max(1, page),
                Math.Clamp(pageSize, 1, MaxPageSize),
                cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(result);
    }

    private static IResult Failure(string problemCode, IReadOnlyDictionary<string, string[]>? errors) => problemCode switch
    {
        ProblemCodes.Validation => ApiProblems.Validation(
            errors?.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal)
            ?? new Dictionary<string, string[]>(StringComparer.Ordinal)),
        ProblemCodes.NotFound => ApiProblems.Result(StatusCodes.Status404NotFound, ProblemCodes.NotFound),
        ProblemCodes.LookupCodeImmutable => ApiProblems.Result(StatusCodes.Status400BadRequest, ProblemCodes.LookupCodeImmutable),
        _ => ApiProblems.Result(StatusCodes.Status409Conflict, problemCode)
    };
}
