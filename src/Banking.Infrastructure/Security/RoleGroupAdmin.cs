using System.Globalization;
using System.Text.Json;
using Banking.Application.Abstractions;
using Banking.Application.Errors;
using Banking.Application.Supervision;
using Banking.Domain.Institution;
using Banking.Domain.Lookups;
using Banking.Domain.Security;
using Banking.Domain.Supervision;
using Banking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Banking.Infrastructure.Security;

public sealed record OperationResult<T>(T? Value, string? ProblemCode, IReadOnlyDictionary<string, string[]>? Errors = null)
{
    public bool Succeeded => ProblemCode is null;
}

public static class OperationResult
{
    public static OperationResult<T> Ok<T>(T value) => new(value, null);

    public static OperationResult<T> Fail<T>(string problemCode, IReadOnlyDictionary<string, string[]>? errors = null) =>
        new(default, problemCode, errors);
}

public sealed record RoleGroupDraft(string Code, string Name, string? Description, IReadOnlyList<PermissionGrant> Permissions);

public sealed record AssignmentDraft(long StaffUserId, int BranchId, IReadOnlyList<string> RoleGroupCodes);

public sealed record RoleGroupSummary(
    int RoleGroupId,
    string RoleGroupCode,
    string RoleGroupName,
    string? Description,
    bool IsSystemGroup,
    bool IsActive,
    int MemberCount,
    long? PendingChangeRequestId);

public sealed record RoleGroupDetail(RoleGroupSummary Group, IReadOnlyList<PermissionGrant> Permissions);

public sealed record RoleGroupMember(long StaffUserId, string StaffCode, string FullName, int BranchId, string BranchCode);

public sealed record ChangeRequestView(
    long ChangeRequestId,
    string EntityType,
    string EntityId,
    string ChangeType,
    string Status,
    JsonElement? Before,
    JsonElement Proposed,
    long RequestedByUserId,
    string? RequestedByName,
    DateTime RequestedOn,
    long? DecidedByUserId,
    DateTime? DecidedOn,
    string? DecisionNote);

public sealed record ModuleView(string ModuleCode, string ModuleName, IReadOnlyList<SubModuleView> SubModules);

public sealed record SubModuleView(string SubModuleCode, string SubModuleName);

/// <summary>
/// Role groups and their assignments (A2-09) with the safety rules of addendum 2.4 (A2-10). Every change is a
/// <see cref="ChangeRequest"/> that applies only when a different supervisor approves it.
/// </summary>
public sealed class RoleGroupAdmin
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly BankingDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly PermissionCacheVersion _cacheVersion;
    private readonly IActivityContext _activity;
    private readonly SupervisionOptions _supervision;

    public RoleGroupAdmin(
        BankingDbContext db,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        PermissionCacheVersion cacheVersion,
        IActivityContext activity,
        IOptions<SupervisionOptions> supervision)
    {
        ArgumentNullException.ThrowIfNull(supervision);
        _db = db;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
        _cacheVersion = cacheVersion;
        _activity = activity;
        _supervision = supervision.Value;
    }

    public async Task<IReadOnlyList<ModuleView>> ListModulesAsync(CancellationToken cancellationToken)
    {
        var modules = await _db.Set<Module>()
            .AsNoTracking()
            .Where(module => module.IsActive)
            .OrderBy(module => module.ModuleCode)
            .Select(module => new
            {
                module.ModuleCode,
                module.ModuleName,
                SubModules = module.SubModules
                    .Where(subModule => subModule.IsActive)
                    .OrderBy(subModule => subModule.SubModuleCode)
                    .Select(subModule => new SubModuleView(subModule.SubModuleCode, subModule.SubModuleName))
                    .ToList()
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return modules.Select(module => new ModuleView(module.ModuleCode, module.ModuleName, module.SubModules)).ToList();
    }

    public async Task<IReadOnlyList<RoleGroupSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var pending = await PendingByGroupAsync(cancellationToken).ConfigureAwait(false);

        var groups = await _db.Set<RoleGroup>()
            .AsNoTracking()
            .OrderBy(group => group.RoleGroupCode)
            .Select(group => new
            {
                group.RoleGroupId,
                group.RoleGroupCode,
                group.RoleGroupName,
                group.Description,
                group.IsSystemGroup,
                group.IsActive,
                MemberCount = group.Members.Count(member => !member.IsSupervisionRequired || member.SupervisedByUserId != null)
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return groups.Select(group => new RoleGroupSummary(
                group.RoleGroupId,
                group.RoleGroupCode,
                group.RoleGroupName,
                group.Description,
                group.IsSystemGroup,
                group.IsActive,
                group.MemberCount,
                pending.TryGetValue(Key(group.RoleGroupId), out var id) ? id : null))
            .ToList();
    }

    public async Task<RoleGroupDetail?> GetAsync(int roleGroupId, CancellationToken cancellationToken)
    {
        var summary = (await ListAsync(cancellationToken).ConfigureAwait(false))
            .SingleOrDefault(group => group.RoleGroupId == roleGroupId);
        if (summary is null)
        {
            return null;
        }

        return new RoleGroupDetail(summary, await MatrixAsync(roleGroupId, cancellationToken).ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<RoleGroupMember>> MembersAsync(int roleGroupId, CancellationToken cancellationToken) =>
        await _db.Set<StaffUserRoleGroup>()
            .AsNoTracking()
            .Where(row => row.RoleGroupId == roleGroupId && (!row.IsSupervisionRequired || row.SupervisedByUserId != null))
            .OrderBy(row => row.StaffUser!.StaffCode)
            .Select(row => new RoleGroupMember(
                row.StaffUserId,
                row.StaffUser!.StaffCode,
                row.StaffUser.FullName,
                row.BranchId,
                row.Branch!.BranchCode))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<OperationResult<long>> RequestCreateAsync(RoleGroupDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var normalized = draft with { Code = draft.Code.Trim().ToUpperInvariant(), Name = draft.Name.Trim() };
        var errors = await ValidateAsync(normalized, cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return OperationResult.Fail<long>(ProblemCodes.Validation, errors);
        }

        if (await _db.Set<RoleGroup>().AnyAsync(group => group.RoleGroupCode == normalized.Code, cancellationToken).ConfigureAwait(false))
        {
            return OperationResult.Fail<long>(ProblemCodes.RoleGroupCodeTaken);
        }

        return await RequestAsync(
                ChangeRequestEntities.RoleGroup,
                "new:" + normalized.Code,
                ChangeRequestTypes.RoleGroupCreate,
                before: null,
                normalized,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OperationResult<long>> RequestUpdateAsync(int roleGroupId, RoleGroupDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var group = await _db.Set<RoleGroup>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.RoleGroupId == roleGroupId, cancellationToken)
            .ConfigureAwait(false);
        if (group is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        // The code is immutable after creation (addendum 2.1).
        var normalized = draft with { Code = group.RoleGroupCode, Name = draft.Name.Trim() };
        var errors = await ValidateAsync(normalized, cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return OperationResult.Fail<long>(ProblemCodes.Validation, errors);
        }

        var before = await SnapshotAsync(group, cancellationToken).ConfigureAwait(false);
        return await RequestAsync(
                ChangeRequestEntities.RoleGroup,
                Key(roleGroupId),
                ChangeRequestTypes.RoleGroupUpdate,
                before,
                normalized,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OperationResult<long>> RequestDeactivateAsync(int roleGroupId, CancellationToken cancellationToken)
    {
        var group = await _db.Set<RoleGroup>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.RoleGroupId == roleGroupId, cancellationToken)
            .ConfigureAwait(false);
        if (group is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        if (!group.IsActive)
        {
            return OperationResult.Fail<long>(ProblemCodes.RoleGroupInactive);
        }

        var before = await SnapshotAsync(group, cancellationToken).ConfigureAwait(false);
        return await RequestAsync(
                ChangeRequestEntities.RoleGroup,
                Key(roleGroupId),
                ChangeRequestTypes.RoleGroupDeactivate,
                before,
                new { group.RoleGroupCode, IsActive = false },
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Replaces one staff member's role groups in one branch. Nobody may change their own (rule 2.4.4).</summary>
    public async Task<OperationResult<long>> RequestAssignmentsAsync(AssignmentDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (draft.StaffUserId == _currentUser.StaffUserId)
        {
            return OperationResult.Fail<long>(ProblemCodes.SelfAssignment);
        }

        var staffExists = await _db.Set<StaffUser>()
            .AnyAsync(row => row.StaffUserId == draft.StaffUserId && row.StaffCode != StaffCodes.System, cancellationToken)
            .ConfigureAwait(false);
        var branchExists = await _db.Set<Branch>()
            .AnyAsync(row => row.BranchId == draft.BranchId, cancellationToken)
            .ConfigureAwait(false);
        if (!staffExists || !branchExists)
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        var codes = draft.RoleGroupCodes.Select(code => code.Trim().ToUpperInvariant()).Distinct(StringComparer.Ordinal).OrderBy(code => code, StringComparer.Ordinal).ToList();
        var known = await _db.Set<RoleGroup>()
            .Where(group => codes.Contains(group.RoleGroupCode) && group.IsActive)
            .Select(group => group.RoleGroupCode)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var unknown = codes.Except(known, StringComparer.Ordinal).ToList();
        if (unknown.Count > 0)
        {
            return OperationResult.Fail<long>(
                ProblemCodes.Validation,
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["roleGroupCodes"] = [$"Unknown or inactive role groups: {string.Join(", ", unknown)}."]
                });
        }

        var current = await _db.Set<StaffUserRoleGroup>()
            .AsNoTracking()
            .Where(row => row.StaffUserId == draft.StaffUserId && row.BranchId == draft.BranchId)
            .Select(row => row.RoleGroup!.RoleGroupCode)
            .OrderBy(code => code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await RequestAsync(
                ChangeRequestEntities.StaffBranchAssignment,
                AssignmentKey(draft.StaffUserId, draft.BranchId),
                ChangeRequestTypes.StaffRoleGroupsReplace,
                new AssignmentDraft(draft.StaffUserId, draft.BranchId, current),
                draft with { RoleGroupCodes = codes },
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OperationResult<IReadOnlyList<ChangeRequestView>>> ListChangeRequestsAsync(
        string? status,
        CancellationToken cancellationToken)
    {
        var query = _db.Set<ChangeRequest>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalized = status.Trim().ToUpperInvariant();
            query = query.Where(row => row.Status == normalized);
        }

        var rows = await query
            .OrderByDescending(row => row.RequestedOn)
            .Take(200)
            .Select(row => new { Row = row, RequestedByName = row.RequestedByUser!.FullName })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return OperationResult.Ok<IReadOnlyList<ChangeRequestView>>(rows.Select(row => View(row.Row, row.RequestedByName)).ToList());
    }

    public async Task<ChangeRequestView?> GetChangeRequestAsync(long changeRequestId, CancellationToken cancellationToken)
    {
        var row = await _db.Set<ChangeRequest>()
            .AsNoTracking()
            .Where(request => request.ChangeRequestId == changeRequestId)
            .Select(request => new { Row = request, RequestedByName = request.RequestedByUser!.FullName })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null ? null : View(row.Row, row.RequestedByName);
    }

    public async Task<OperationResult<long>> RejectAsync(long changeRequestId, string? reason, CancellationToken cancellationToken)
    {
        var request = await PendingRequestAsync(changeRequestId, cancellationToken).ConfigureAwait(false);
        if (request is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.ChangeNotPending);
        }

        request.Status = ChangeRequestStatuses.Rejected;
        request.DecidedByUserId = _currentUser.StaffUserId;
        request.DecidedOn = _timeProvider.GetUtcNow().UtcDateTime;
        request.DecisionNote = reason is { Length: > 300 } ? reason[..300] : reason;

        _activity.Record(EventCodes.SuperviseReject, nameof(ChangeRequest), Key(changeRequestId));
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok<long>(changeRequestId);
    }

    /// <summary>
    /// Four-eyes approval. The change and the last-administrator check run in one transaction: if the institution
    /// would be left without anyone able to manage users, permissions and KYC, nothing is applied.
    /// </summary>
    public async Task<OperationResult<long>> ApproveAsync(long changeRequestId, CancellationToken cancellationToken)
    {
        var approverId = _currentUser.StaffUserId ?? throw new InvalidOperationException("Approval needs a signed-in user.");

        var request = await PendingRequestAsync(changeRequestId, cancellationToken).ConfigureAwait(false);
        if (request is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.ChangeNotPending);
        }

        if (request.RequestedByUserId == approverId && !_supervision.AllowSelfApproval)
        {
            return OperationResult.Fail<long>(SupervisionProblems.SelfApproval);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _activity.Record(EventCodes.SuperviseApprove, nameof(ChangeRequest), Key(changeRequestId));
        var transaction = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var applied = await ApplyAsync(request, approverId, now, cancellationToken).ConfigureAwait(false);
            if (!applied.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                _db.ChangeTracker.Clear();
                _activity.Written = false;
                return applied;
            }

            request.Status = ChangeRequestStatuses.Approved;
            request.DecidedByUserId = approverId;
            request.DecidedOn = now;
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (!await AdministratorRemainsAsync(cancellationToken).ConfigureAwait(false))
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                _db.ChangeTracker.Clear();
                _activity.Written = false;
                return OperationResult.Fail<long>(ProblemCodes.LastAdministrator);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        _cacheVersion.Bump();
        return OperationResult.Ok<long>(changeRequestId);
    }

    /// <summary>
    /// Addendum 2.4 rule 3: at least one active staff member must hold SEC/USERS edit, SEC/PERMISSIONS edit and
    /// PTY/KYC supervise (through the union of their approved groups in one branch).
    /// </summary>
    internal async Task<bool> AdministratorRemainsAsync(CancellationToken cancellationToken)
    {
        var rows = await (
                from assignment in _db.Set<StaffUserRoleGroup>().AsNoTracking()
                where !assignment.IsSupervisionRequired || assignment.SupervisedByUserId != null
                where !assignment.StaffUser!.IsDisabled
                join roleGroup in _db.Set<RoleGroup>().AsNoTracking() on assignment.RoleGroupId equals roleGroup.RoleGroupId
                where roleGroup.IsActive
                join permission in _db.Set<RoleGroupPermission>().AsNoTracking() on roleGroup.RoleGroupId equals permission.RoleGroupId
                select new
                {
                    assignment.StaffUserId,
                    assignment.BranchId,
                    Grant = new PermissionGrant(
                        permission.Module!.ModuleCode,
                        permission.SubModuleId == null ? null : permission.SubModule!.SubModuleCode,
                        permission.AllowView,
                        permission.AllowAdd,
                        permission.AllowEdit,
                        permission.AllowDelete,
                        permission.AllowSupervise)
                })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .GroupBy(row => (row.StaffUserId, row.BranchId))
            .Select(group => new PermissionSet(group.Select(row => row.Grant)))
            .Any(set =>
                set.Allows(ModuleCatalog.Security, "USERS", PermissionAction.Edit)
                && set.Allows(ModuleCatalog.Security, "PERMISSIONS", PermissionAction.Edit)
                && set.Allows(ModuleCatalog.Party, "KYC", PermissionAction.Supervise));
    }

    private async Task<OperationResult<long>> ApplyAsync(
        ChangeRequest request,
        long approverId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        switch (request.ChangeType)
        {
            case ChangeRequestTypes.RoleGroupCreate:
            {
                var draft = JsonSerializer.Deserialize<RoleGroupDraft>(request.PayloadJson, Json)!;
                if (await _db.Set<RoleGroup>().AnyAsync(group => group.RoleGroupCode == draft.Code, cancellationToken).ConfigureAwait(false))
                {
                    return OperationResult.Fail<long>(ProblemCodes.RoleGroupCodeTaken);
                }

                var group = new RoleGroup
                {
                    RoleGroupCode = draft.Code,
                    RoleGroupName = draft.Name,
                    Description = draft.Description,
                    IsSystemGroup = false,
                    IsActive = true,
                    IsSupervisionRequired = true,
                    SupervisedByUserId = approverId,
                    SupervisedOn = now
                };
                await AddMatrixAsync(group, draft.Permissions, cancellationToken).ConfigureAwait(false);
                _db.Set<RoleGroup>().Add(group);
                break;
            }

            case ChangeRequestTypes.RoleGroupUpdate:
            {
                var draft = JsonSerializer.Deserialize<RoleGroupDraft>(request.PayloadJson, Json)!;
                var group = await _db.Set<RoleGroup>()
                    .Include(row => row.Permissions)
                    .SingleAsync(row => row.RoleGroupCode == draft.Code, cancellationToken)
                    .ConfigureAwait(false);

                group.RoleGroupName = draft.Name;
                group.Description = draft.Description;
                group.IsSupervisionRequired = true;
                group.SupervisedByUserId = approverId;
                group.SupervisedOn = now;
                _db.Set<RoleGroupPermission>().RemoveRange(group.Permissions);
                await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await AddMatrixAsync(group, draft.Permissions, cancellationToken).ConfigureAwait(false);
                break;
            }

            case ChangeRequestTypes.RoleGroupDeactivate:
            {
                var roleGroupId = int.Parse(request.EntityId, CultureInfo.InvariantCulture);
                var group = await _db.Set<RoleGroup>().SingleAsync(row => row.RoleGroupId == roleGroupId, cancellationToken).ConfigureAwait(false);
                group.IsActive = false;
                group.IsSupervisionRequired = true;
                group.SupervisedByUserId = approverId;
                group.SupervisedOn = now;
                break;
            }

            case ChangeRequestTypes.StaffRoleGroupsReplace:
            {
                var draft = JsonSerializer.Deserialize<AssignmentDraft>(request.PayloadJson, Json)!;
                var groups = await _db.Set<RoleGroup>()
                    .Where(group => draft.RoleGroupCodes.Contains(group.RoleGroupCode))
                    .ToDictionaryAsync(group => group.RoleGroupCode, group => group.RoleGroupId, StringComparer.Ordinal, cancellationToken)
                    .ConfigureAwait(false);
                var existing = await _db.Set<StaffUserRoleGroup>()
                    .Where(row => row.StaffUserId == draft.StaffUserId && row.BranchId == draft.BranchId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                _db.Set<StaffUserRoleGroup>().RemoveRange(existing.Where(row => !groups.ContainsValue(row.RoleGroupId)));
                foreach (var roleGroupId in groups.Values.Where(id => existing.TrueForAll(row => row.RoleGroupId != id)))
                {
                    _db.Set<StaffUserRoleGroup>().Add(new StaffUserRoleGroup
                    {
                        StaffUserId = draft.StaffUserId,
                        BranchId = draft.BranchId,
                        RoleGroupId = roleGroupId,
                        IsSupervisionRequired = true,
                        SupervisedByUserId = approverId,
                        SupervisedOn = now
                    });
                }

                break;
            }

            default:
                return OperationResult.Fail<long>(ProblemCodes.ChangeNotPending);
        }

        return OperationResult.Ok<long>(request.ChangeRequestId);
    }

    private async Task AddMatrixAsync(RoleGroup group, IReadOnlyList<PermissionGrant> grants, CancellationToken cancellationToken)
    {
        var modules = await _db.Set<Module>().AsNoTracking()
            .ToDictionaryAsync(module => module.ModuleCode, module => module.ModuleId, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);
        var subModules = await _db.Set<SubModule>().AsNoTracking()
            .ToDictionaryAsync(subModule => subModule.SubModuleCode, subModule => subModule.SubModuleId, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        foreach (var grant in grants)
        {
            group.Permissions.Add(new RoleGroupPermission
            {
                RoleGroupId = group.RoleGroupId,
                ModuleId = modules[grant.ModuleCode],
                SubModuleId = grant.SubModuleCode is null ? null : subModules[grant.SubModuleCode],
                AllowView = grant.AllowView,
                AllowAdd = grant.AllowAdd,
                AllowEdit = grant.AllowEdit,
                AllowDelete = grant.AllowDelete,
                AllowSupervise = grant.AllowSupervise
            });
        }
    }

    private async Task<OperationResult<long>> RequestAsync(
        string entityType,
        string entityId,
        string changeType,
        object? before,
        object proposed,
        CancellationToken cancellationToken)
    {
        var pending = await _db.Set<ChangeRequest>()
            .AnyAsync(
                row => row.EntityType == entityType && row.EntityId == entityId && row.Status == ChangeRequestStatuses.Pending,
                cancellationToken)
            .ConfigureAwait(false);
        if (pending)
        {
            return OperationResult.Fail<long>(ProblemCodes.ChangePendingExists);
        }

        var request = new ChangeRequest
        {
            EntityType = entityType,
            EntityId = entityId,
            ChangeType = changeType,
            Status = ChangeRequestStatuses.Pending,
            BeforeJson = before is null ? null : JsonSerializer.Serialize(before, Json),
            PayloadJson = JsonSerializer.Serialize(proposed, Json),
            RequestedByUserId = _currentUser.StaffUserId ?? throw new InvalidOperationException("A change request needs a signed-in user."),
            RequestedOn = _timeProvider.GetUtcNow().UtcDateTime
        };

        _db.Set<ChangeRequest>().Add(request);
        _activity.Record(EventCodes.UserAdmin, entityType, entityId);

        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // The filtered unique index caught a concurrent request for the same entity.
            _db.Entry(request).State = EntityState.Detached;
            return OperationResult.Fail<long>(ProblemCodes.ChangePendingExists);
        }

        return OperationResult.Ok<long>(request.ChangeRequestId);
    }

    private async Task<Dictionary<string, string[]>> ValidateAsync(RoleGroupDraft draft, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (draft.Code.Length is 0 or > 30 || !draft.Code.All(character => char.IsAsciiLetterUpper(character) || char.IsAsciiDigit(character) || character == '_'))
        {
            errors["code"] = ["Use 1 to 30 capital letters, digits or underscores."];
        }

        if (draft.Name.Length is 0 or > 100)
        {
            errors["name"] = ["Enter a name of up to 100 characters."];
        }

        if (draft.Description is { Length: > 300 })
        {
            errors["description"] = ["Keep the description to 300 characters."];
        }

        var catalog = await ListModulesAsync(cancellationToken).ConfigureAwait(false);
        var problems = new List<string>();
        var seen = new HashSet<(string, string?)>();

        foreach (var grant in draft.Permissions)
        {
            var module = catalog.SingleOrDefault(row => row.ModuleCode == grant.ModuleCode);
            if (module is null)
            {
                problems.Add($"Unknown module {grant.ModuleCode}.");
                continue;
            }

            if (grant.SubModuleCode is not null && module.SubModules.All(row => row.SubModuleCode != grant.SubModuleCode))
            {
                problems.Add($"{grant.SubModuleCode} is not part of {grant.ModuleCode}.");
            }

            if (!seen.Add((grant.ModuleCode, grant.SubModuleCode)))
            {
                problems.Add($"{grant.ModuleCode}/{grant.SubModuleCode} appears more than once.");
            }

            if (!grant.AllowView && (grant.AllowAdd || grant.AllowEdit || grant.AllowDelete || grant.AllowSupervise))
            {
                problems.Add($"{grant.ModuleCode}/{grant.SubModuleCode}: view is required for any other permission.");
            }
        }

        if (problems.Count > 0)
        {
            errors["permissions"] = [.. problems];
        }

        return errors;
    }

    private async Task<object> SnapshotAsync(RoleGroup group, CancellationToken cancellationToken) =>
        new RoleGroupDraft(
            group.RoleGroupCode,
            group.RoleGroupName,
            group.Description,
            await MatrixAsync(group.RoleGroupId, cancellationToken).ConfigureAwait(false));

    private async Task<IReadOnlyList<PermissionGrant>> MatrixAsync(int roleGroupId, CancellationToken cancellationToken) =>
        await _db.Set<RoleGroupPermission>()
            .AsNoTracking()
            .Where(row => row.RoleGroupId == roleGroupId)
            .OrderBy(row => row.Module!.ModuleCode)
            .ThenBy(row => row.SubModuleId)
            .Select(row => new PermissionGrant(
                row.Module!.ModuleCode,
                row.SubModuleId == null ? null : row.SubModule!.SubModuleCode,
                row.AllowView,
                row.AllowAdd,
                row.AllowEdit,
                row.AllowDelete,
                row.AllowSupervise))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    private async Task<Dictionary<string, long>> PendingByGroupAsync(CancellationToken cancellationToken) =>
        await _db.Set<ChangeRequest>()
            .AsNoTracking()
            .Where(row => row.EntityType == ChangeRequestEntities.RoleGroup && row.Status == ChangeRequestStatuses.Pending)
            .ToDictionaryAsync(row => row.EntityId, row => row.ChangeRequestId, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

    private Task<ChangeRequest?> PendingRequestAsync(long changeRequestId, CancellationToken cancellationToken) =>
        _db.Set<ChangeRequest>()
            .SingleOrDefaultAsync(
                row => row.ChangeRequestId == changeRequestId && row.Status == ChangeRequestStatuses.Pending,
                cancellationToken);

    private static ChangeRequestView View(ChangeRequest row, string? requestedByName) => new(
        row.ChangeRequestId,
        row.EntityType,
        row.EntityId,
        row.ChangeType,
        row.Status,
        row.BeforeJson is null ? null : JsonSerializer.Deserialize<JsonElement>(row.BeforeJson),
        JsonSerializer.Deserialize<JsonElement>(row.PayloadJson),
        row.RequestedByUserId,
        requestedByName,
        row.RequestedOn,
        row.DecidedByUserId,
        row.DecidedOn,
        row.DecisionNote);

    private static string Key(long id) => id.ToString(CultureInfo.InvariantCulture);

    private static string AssignmentKey(long staffUserId, int branchId) =>
        string.Create(CultureInfo.InvariantCulture, $"{staffUserId}|{branchId}");
}
