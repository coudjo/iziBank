using Banking.Application.Abstractions;
using Banking.Application.Accounts;
using Banking.Application.Errors;
using Banking.Application.Supervision;
using Banking.Domain.Accounts;
using Banking.Domain.Lookups;
using Banking.Domain.Parties;
using Banking.Domain.Products;
using Banking.Domain.Supervision;
using Banking.Infrastructure.Parties;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Banking.Infrastructure.Accounts;

public sealed record AccountHolderDraft(long? PartyId, int? AccountHolderRoleId);

public sealed record OpenAccountDraft(int? ProductId, string? AccountName, int? OperatingModeId, IReadOnlyList<AccountHolderDraft>? Holders);

public sealed record AccountOpened(long AccountId, string AccountNumber, string AccountName, string Status);

/// <summary>
/// A5-04: <c>POST /accounts</c> (plan 6.9, addendum rule 3a). Every check runs before anything is written; the
/// accounts-per-customer count and the insert share one transaction holding a lock on the primary holder, so two
/// tellers cannot both open the last allowed account. The account starts Pending (R1).
/// </summary>
public sealed class AccountOpening
{
    private readonly BankingDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IActivityContext _activity;
    private readonly IWorkingDateProvider _workingDate;
    private readonly KycService _kyc;
    private readonly PartySignatures _signatures;
    private readonly AccountNumbers _numbers;
    private readonly SupervisionOptions _supervision;
    private readonly AccountOptions _accounts;

    public AccountOpening(
        BankingDbContext db,
        ICurrentUser currentUser,
        IActivityContext activity,
        IWorkingDateProvider workingDate,
        KycService kyc,
        PartySignatures signatures,
        AccountNumbers numbers,
        IOptions<SupervisionOptions> supervision,
        IOptions<AccountOptions> accounts)
    {
        ArgumentNullException.ThrowIfNull(supervision);
        ArgumentNullException.ThrowIfNull(accounts);
        _db = db;
        _currentUser = currentUser;
        _activity = activity;
        _workingDate = workingDate;
        _kyc = kyc;
        _signatures = signatures;
        _numbers = numbers;
        _supervision = supervision.Value;
        _accounts = accounts.Value;
    }

    public async Task<OperationResult<AccountOpened>> OpenAsync(OpenAccountDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var branchId = _currentUser.BranchId ?? throw new InvalidOperationException("Opening an account needs a branch.");
        var userId = _currentUser.StaffUserId ?? throw new InvalidOperationException("Opening an account needs a signed-in user.");

        // Rule 1: the account opens on the branch's working date.
        DateOnly workingDate;
        try
        {
            workingDate = await _workingDate.GetAsync(branchId, cancellationToken).ConfigureAwait(false);
        }
        catch (BusinessRuleException exception)
        {
            return OperationResult.Fail<AccountOpened>(exception.ProblemCode);
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var holderDrafts = draft.Holders ?? [];
        if (holderDrafts.Count == 0)
        {
            errors["holders"] = ["Add the primary holder."];
        }

        var roles = await CodesAsync<AccountHolderRoleType>(cancellationToken).ConfigureAwait(false);
        var modes = await CodesAsync<OperatingModeType>(cancellationToken).ConfigureAwait(false);

        if (holderDrafts.Any(holder => holder.PartyId is null || holder.AccountHolderRoleId is not { } roleId || !roles.ContainsKey(roleId)))
        {
            errors["holders"] = ["Choose a customer and a role for each holder."];
        }

        string? modeCode = null;
        if (draft.OperatingModeId is { } modeId && !modes.TryGetValue(modeId, out modeCode))
        {
            errors["operatingModeId"] = ["Choose an operating mode from the list."];
        }

        var name = draft.AccountName?.Trim();
        if (name is { Length: > AccountOpeningRules.MaxNameLength })
        {
            errors["accountName"] = ["Enter a name of up to 150 characters."];
        }

        var product = draft.ProductId is { } productId
            ? await _db.Set<Product>()
                .AsNoTracking()
                .Where(row => row.ProductId == productId)
                .Select(row => new
                {
                    row.ProductId,
                    row.AccountPrefix,
                    row.AccountsPerCustomer,
                    TypeCode = row.ProductType!.Code,
                    Approved = !row.IsSupervisionRequired || row.SupervisedByUserId != null,
                    row.IsActive,
                    Frozen = row.IsFreezed == true
                })
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false)
            : null;
        if (product is null)
        {
            errors["productId"] = ["Choose a product."];
        }

        if (errors.Count > 0)
        {
            return OperationResult.Fail<AccountOpened>(ProblemCodes.Validation, errors);
        }

        // Rule 2: only approved, active, unfrozen products of a directly openable type.
        if (!product!.Approved || !product.IsActive || product.Frozen
            || !_accounts.DirectOpeningProductTypes.Contains(product.TypeCode, StringComparer.Ordinal))
        {
            return OperationResult.Fail<AccountOpened>(ProblemCodes.AccountProductNotOpenable);
        }

        var partyIds = holderDrafts.Select(holder => holder.PartyId!.Value).Distinct().ToList();
        var parties = await _db.Set<Party>()
            .AsNoTracking()
            .Where(row => partyIds.Contains(row.PartyId))
            .Select(row => new
            {
                row.PartyId,
                row.PartyNumber,
                row.DisplayName,
                TypeCode = row.PartyType!.Code,
                Approved = !row.IsSupervisionRequired || row.SupervisedByUserId != null
            })
            .ToDictionaryAsync(row => row.PartyId, cancellationToken)
            .ConfigureAwait(false);
        if (parties.Count != partyIds.Count)
        {
            return OperationResult.Fail<AccountOpened>(
                ProblemCodes.Validation,
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["holders"] = ["One of the customers could not be found."] });
        }

        var specs = holderDrafts
            .Select(holder =>
            {
                var party = parties[holder.PartyId!.Value];
                return new HolderSpec(party.PartyId, party.TypeCode, roles[holder.AccountHolderRoleId!.Value], party.DisplayName);
            })
            .ToList();

        // Rule 4: shape by primary holder. A group's signatories must be its active members.
        var primary = specs.FirstOrDefault(spec => spec.RoleCode == LookupCodes.AccountHolderRoles.Primary);
        var members = primary is { PartyTypeCode: LookupCodes.PartyTypes.Group }
            ? (await _db.Set<GroupMember>()
                .AsNoTracking()
                .Where(row => row.GroupPartyId == primary.PartyId && row.LeftOn == null)
                .Select(row => row.MemberPartyId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false)).ToHashSet()
            : new HashSet<long>();

        var shape = AccountOpeningRules.Check(specs, modeCode, members);
        if (!shape.IsValid)
        {
            return OperationResult.Fail<AccountOpened>(ProblemCodes.Validation, shape.Errors);
        }

        // Rule 3: every holder is approved and KYC verified.
        var statuses = await _kyc.EffectiveStatusesAsync(partyIds, cancellationToken).ConfigureAwait(false);
        var notReady = parties.Values
            .Where(party => !party.Approved || statuses.GetValueOrDefault(party.PartyId) != LookupCodes.KycStatuses.Verified)
            .Select(party => party.PartyNumber)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (notReady.Count > 0)
        {
            return OperationResult.FailWith<AccountOpened>(ProblemCodes.AccountHolderNotReady, PartyNumbers(notReady));
        }

        // Rule 3a: every person who signs has an effective specimen signature.
        var signed = await _signatures.WithEffectiveSignatureAsync(shape.Signers, cancellationToken).ConfigureAwait(false);
        var unsigned = shape.Signers
            .Where(id => !signed.Contains(id))
            .Select(id => parties[id].PartyNumber)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (unsigned.Count > 0)
        {
            return OperationResult.FailWith<AccountOpened>(ProblemCodes.AccountSignatureMissing, PartyNumbers(unsigned));
        }

        var modeIds = modes.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);
        var roleIds = roles.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);
        var primaryRoleId = roleIds[LookupCodes.AccountHolderRoles.Primary];
        var accountName = string.IsNullOrEmpty(name) ? shape.DefaultName : name;

        var transaction = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            await _db.Database
                .ExecuteSqlInterpolatedAsync($"SELECT [PartyId] FROM [Party] WITH (UPDLOCK, HOLDLOCK) WHERE [PartyId] = {primary!.PartyId}", cancellationToken)
                .ConfigureAwait(false);

            // Rule 5: open accounts of this product where the customer is the primary holder.
            if (product.AccountsPerCustomer is { } limit && limit > 0)
            {
                var open = await _db.Set<Account>()
                    .CountAsync(
                        account => account.ProductId == product.ProductId
                                   && account.ClosedOn == null
                                   && account.Holders.Any(holder => holder.PartyId == primary.PartyId && holder.AccountHolderRoleId == primaryRoleId),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (open >= limit)
                {
                    return OperationResult.Fail<AccountOpened>(ProblemCodes.AccountLimitReached);
                }
            }

            // Rules 6 and 7.
            var account = new Account
            {
                AccountNumber = await _numbers.NextAsync(product.AccountPrefix, cancellationToken).ConfigureAwait(false),
                BranchId = branchId,
                ProductId = product.ProductId,
                AccountName = accountName,
                DateOpened = workingDate,
                OpenedByUserId = userId,
                OperatingModeId = modeIds[shape.OperatingModeCode!],
                Balance = new AccountBalance(),
                Condition = new AccountCondition()
            };
            SupervisionRules.StartNew(account, _supervision.Enabled);
            foreach (var spec in specs)
            {
                account.Holders.Add(new AccountHolder { PartyId = spec.PartyId, AccountHolderRoleId = roleIds[spec.RoleCode] });
            }

            _db.Set<Account>().Add(account);
            _activity.Record(EventCodes.AccountOpen, nameof(Account), account.AccountNumber);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            var status = AccountStatusRules.Derive(SupervisionRules.StateOf(account) == SupervisionState.Pending, closed: false, frozen: false, dormant: false);
            return OperationResult.Ok(new AccountOpened(account.AccountId, account.AccountNumber, account.AccountName, status));
        }
    }

    private async Task<Dictionary<int, string>> CodesAsync<TLookup>(CancellationToken cancellationToken)
        where TLookup : Lookup =>
        await _db.Set<TLookup>()
            .AsNoTracking()
            .Where(row => row.IsActive)
            .ToDictionaryAsync(row => row.Id, row => row.Code, cancellationToken)
            .ConfigureAwait(false);

    private static Dictionary<string, object?> PartyNumbers(IReadOnlyList<string> numbers) =>
        new(StringComparer.Ordinal) { ["partyNumbers"] = numbers };
}
