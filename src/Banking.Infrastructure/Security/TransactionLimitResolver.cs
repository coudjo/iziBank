using Banking.Domain.Security;
using Banking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Banking.Infrastructure.Security;

/// <summary>
/// B0-02: a staff member's limit for one kind of posting in one branch (plan B 2.2): the largest limit across their
/// approved assignments to active role groups in that branch, or null when none has one (every such posting then waits
/// for a supervisor). Read fresh on every posting, so an approved change applies at once.
/// </summary>
public sealed class TransactionLimitResolver
{
    private readonly BankingDbContext _db;

    public TransactionLimitResolver(BankingDbContext db)
    {
        _db = db;
    }

    public async Task<decimal?> LimitAsync(long staffUserId, int branchId, string limitCode, CancellationToken cancellationToken)
    {
        var limits = await (
                from assignment in _db.Set<StaffUserRoleGroup>().AsNoTracking()
                where assignment.StaffUserId == staffUserId && assignment.BranchId == branchId
                where !assignment.IsSupervisionRequired || assignment.SupervisedByUserId != null
                join roleGroup in _db.Set<RoleGroup>().AsNoTracking() on assignment.RoleGroupId equals roleGroup.RoleGroupId
                where roleGroup.IsActive
                join limit in _db.Set<RoleGroupTransactionLimit>().AsNoTracking() on roleGroup.RoleGroupId equals limit.RoleGroupId
                where limit.LimitCode == limitCode
                select new TransactionLimitValue(limit.LimitCode, limit.MaxAmount))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return TransactionLimitRules.Effective(limits, limitCode);
    }
}
