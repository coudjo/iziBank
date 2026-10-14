using Banking.Application.Security;
using Banking.Domain.Security;
using Banking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Banking.Infrastructure.Security;

/// <summary>Bumped whenever a role group, its matrix or an assignment changes, so cached permissions expire at once.</summary>
public sealed class PermissionCacheVersion
{
    private long _current;

    public long Current => Interlocked.Read(ref _current);

    public void Bump() => Interlocked.Increment(ref _current);
}

/// <summary>
/// Addendum 2.2 and 2.3: effective permissions = union of the approved, active role groups the staff member holds
/// in the branch. Cached for at most <see cref="SecurityOptions.CacheSeconds"/>.
/// </summary>
public sealed class PermissionResolver
{
    private readonly BankingDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly PermissionCacheVersion _version;
    private readonly SecurityOptions _options;

    public PermissionResolver(
        BankingDbContext db,
        IMemoryCache cache,
        PermissionCacheVersion version,
        IOptions<SecurityOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _db = db;
        _cache = cache;
        _version = version;
        _options = options.Value;
    }

    public async Task<PermissionSet> ResolveAsync(long staffUserId, int branchId, CancellationToken cancellationToken)
    {
        var key = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"permissions:{_version.Current}:{staffUserId}:{branchId}");
        if (_cache.TryGetValue(key, out PermissionSet? cached) && cached is not null)
        {
            return cached;
        }

        var rows = await (
                from assignment in _db.Set<StaffUserRoleGroup>().AsNoTracking()
                where assignment.StaffUserId == staffUserId
                      && assignment.BranchId == branchId
                      && (!assignment.IsSupervisionRequired || assignment.SupervisedByUserId != null)
                join roleGroup in _db.Set<RoleGroup>().AsNoTracking() on assignment.RoleGroupId equals roleGroup.RoleGroupId
                where roleGroup.IsActive && (!roleGroup.IsSupervisionRequired || roleGroup.SupervisedByUserId != null)
                join permission in _db.Set<RoleGroupPermission>().AsNoTracking() on roleGroup.RoleGroupId equals permission.RoleGroupId
                select new PermissionGrant(
                    permission.Module!.ModuleCode,
                    permission.SubModuleId == null ? null : permission.SubModule!.SubModuleCode,
                    permission.AllowView,
                    permission.AllowAdd,
                    permission.AllowEdit,
                    permission.AllowDelete,
                    permission.AllowSupervise))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var set = new PermissionSet(rows);
        _cache.Set(key, set, TimeSpan.FromSeconds(_options.CacheSeconds));
        return set;
    }

    /// <summary>Branch switching (plan 5.5): allowed only into a branch where the user holds an approved role group.</summary>
    public Task<bool> HasAssignmentInBranchAsync(long staffUserId, int branchId, CancellationToken cancellationToken) =>
        _db.Set<StaffUserRoleGroup>()
            .AsNoTracking()
            .AnyAsync(
                assignment => assignment.StaffUserId == staffUserId
                    && assignment.BranchId == branchId
                    && (!assignment.IsSupervisionRequired || assignment.SupervisedByUserId != null),
                cancellationToken);
}
