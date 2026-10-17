using Banking.Application.Abstractions;
using Banking.Domain.Accounts;
using Banking.Domain.Lookups;
using Banking.Infrastructure.Parties;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Banking.Infrastructure.Accounts;

public sealed record AccountSearchFilter(string? Term, int? BranchId, int? ProductId, string? Status);

public sealed record AccountListRow(
    long AccountId,
    string AccountNumber,
    string AccountName,
    string BranchCode,
    string ProductCode,
    string ProductName,
    string? PrimaryHolderNumber,
    string? PrimaryHolderName,
    string Status,
    DateOnly DateOpened,
    DateTime CreatedOn);

/// <summary>
/// A5-08: account list and search (plan section 8). A term matches an account number prefix, or the accounts held by
/// the customers that customer search finds by number, name prefix, mobile or ID; every branch is an index seek. The
/// status filter uses the derived status (plan 6.10), first match in plan order.
/// </summary>
public sealed class AccountSearch
{
    private readonly BankingDbContext _db;
    private readonly PartySearch _parties;
    private readonly IActivityContext _activity;

    public AccountSearch(BankingDbContext db, PartySearch parties, IActivityContext activity)
    {
        _db = db;
        _parties = parties;
        _activity = activity;
    }

    public IQueryable<Account> Accounts() => _db.Set<Account>().AsNoTracking();

    public async Task<PagedResult<AccountListRow>> SearchAsync(AccountSearchFilter filter, int page, int pageSize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = Filtered(filter);
        var term = filter.Term?.Trim() ?? string.Empty;
        if (term.Length > 0)
        {
            query = await IndexedMatchesAsync(query, term, cancellationToken).ConfigureAwait(false);
        }

        _activity.Record(EventCodes.Search, nameof(Account), details: new Dictionary<string, object?> { ["term"] = term.Length > 0 });

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var ordered = term.Length > 0
            ? query.OrderBy(account => account.AccountNumber)
            : query.OrderByDescending(account => account.CreatedOn).ThenByDescending(account => account.AccountId);

        var primary = LookupCodes.AccountHolderRoles.Primary;
        var rows = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(account => new
            {
                account.AccountId,
                account.AccountNumber,
                account.AccountName,
                account.Branch!.BranchCode,
                account.Product!.ProductCode,
                account.Product!.ProductName,
                Holder = account.Holders
                    .Where(holder => holder.Role!.Code == primary)
                    .Select(holder => new { holder.Party!.PartyNumber, holder.Party!.DisplayName })
                    .FirstOrDefault(),
                Pending = account.IsSupervisionRequired && account.SupervisedByUserId == null,
                Closed = account.ClosedOn != null,
                Frozen = account.Condition != null && account.Condition.IsFreezed,
                account.IsDormant,
                account.DateOpened,
                account.CreatedOn
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = rows
            .Select(row => new AccountListRow(
                row.AccountId,
                row.AccountNumber,
                row.AccountName,
                row.BranchCode,
                row.ProductCode,
                row.ProductName,
                row.Holder?.PartyNumber,
                row.Holder?.DisplayName,
                AccountStatusRules.Derive(row.Pending, row.Closed, row.Frozen, row.IsDormant),
                row.DateOpened,
                row.CreatedOn))
            .ToList();

        return new PagedResult<AccountListRow>(items, page, pageSize, total);
    }

    /// <summary>The indexed part of a search, as a UNION of seeks. Public so a test can check the query plan.</summary>
    public async Task<IQueryable<Account>> IndexedMatchesAsync(IQueryable<Account> source, string term, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(term);

        var prefix = Escape(term) + "%";
        var partyIds = (await _parties.IndexedMatchesAsync(_parties.Parties(), term, cancellationToken).ConfigureAwait(false))
            .Select(party => party.PartyId);

        return source.Where(account => EF.Functions.Like(account.AccountNumber, prefix))
            .Union(source.Where(account => account.Holders.Any(holder => partyIds.Contains(holder.PartyId))));
    }

    private IQueryable<Account> Filtered(AccountSearchFilter filter)
    {
        var query = Accounts();
        if (filter.BranchId is { } branchId)
        {
            query = query.Where(account => account.BranchId == branchId);
        }

        if (filter.ProductId is { } productId)
        {
            query = query.Where(account => account.ProductId == productId);
        }

        // Plan 6.10 order: pending, closed, frozen, dormant, active.
        switch (filter.Status?.Trim().ToUpperInvariant())
        {
            case AccountStatuses.PendingSupervision:
                query = query.Where(account => account.IsSupervisionRequired && account.SupervisedByUserId == null);
                break;
            case AccountStatuses.Closed:
                query = query.Where(account => !(account.IsSupervisionRequired && account.SupervisedByUserId == null) && account.ClosedOn != null);
                break;
            case AccountStatuses.Frozen:
                query = query.Where(account => !(account.IsSupervisionRequired && account.SupervisedByUserId == null)
                                               && account.ClosedOn == null
                                               && account.Condition!.IsFreezed);
                break;
            case AccountStatuses.Dormant:
                query = query.Where(account => !(account.IsSupervisionRequired && account.SupervisedByUserId == null)
                                               && account.ClosedOn == null
                                               && !account.Condition!.IsFreezed
                                               && account.IsDormant);
                break;
            case AccountStatuses.Active:
                query = query.Where(account => !(account.IsSupervisionRequired && account.SupervisedByUserId == null)
                                               && account.ClosedOn == null
                                               && !account.Condition!.IsFreezed
                                               && !account.IsDormant);
                break;
        }

        return query;
    }

    private static string Escape(string value) =>
        value.Replace("[", "[[]", StringComparison.Ordinal)
            .Replace("%", "[%]", StringComparison.Ordinal)
            .Replace("_", "[_]", StringComparison.Ordinal);
}
