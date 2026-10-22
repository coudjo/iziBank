using Banking.Application.Abstractions;
using Banking.Application.Errors;
using Banking.Domain.Lookups;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Banking.Infrastructure.Reference;

public sealed record LookupRow(int Id, string Code, string Name, bool IsActive, bool IsCoded);

public sealed record LookupTypeInfo(string Slug, string TableName);

/// <summary>
/// Plan 5.10 (A2-03): every lookup can be read; administrators add and rename rows and deactivate the ones that
/// application code does not depend on. Rows are never deleted and a code never changes.
/// </summary>
public sealed class LookupAdmin
{
    private static readonly ILookupSet[] Sets =
    [
        new LookupSet<RegulatoryCategoryType>("regulatory-categories", LookupCodes.RegulatoryCategories.All),
        new LookupSet<BusinessDayStatusType>("business-day-statuses", LookupCodes.BusinessDayStatuses.All),
        new LookupSet<LogoutReasonType>("logout-reasons", LookupCodes.LogoutReasons.All),
        new LookupSet<PartyType>("party-types", LookupCodes.PartyTypes.All),
        new LookupSet<KYCStatusType>("kyc-statuses", LookupCodes.KycStatuses.All),
        new LookupSet<GenderType>("genders", LookupCodes.Genders.All),
        new LookupSet<IdentificationDocumentType>("identification-document-types", LookupCodes.IdentificationDocumentTypes.All),
        new LookupSet<BusinessCategoryType>("business-categories", LookupCodes.BusinessCategories.All),
        new LookupSet<GroupMemberRoleType>("group-member-roles", LookupCodes.GroupMemberRoles.All),
        new LookupSet<ProductType>("product-types", LookupCodes.ProductTypes.All),
        new LookupSet<RateType>("rate-types", LookupCodes.RateTypes.All),
        new LookupSet<OperatingModeType>("operating-modes", LookupCodes.OperatingModes.All),
        new LookupSet<OperatingInstructionType>("operating-instructions", LookupCodes.OperatingInstructions.All),
        new LookupSet<AccountHolderRoleType>("account-holder-roles", LookupCodes.AccountHolderRoles.All),
        new LookupSet<CloseReasonType>("close-reasons", LookupCodes.CloseReasons.All),
        new LookupSet<FreezeReleaseActionType>("freeze-release-actions", LookupCodes.FreezeReleaseActions.All),
        new LookupSet<ChargeCategoryType>("charge-categories", LookupCodes.ChargeCategories.All),
        new LookupSet<ChargeConditionType>("charge-conditions", LookupCodes.ChargeConditions.All),
        new LookupSet<ChargeBasisType>("charge-bases", LookupCodes.ChargeBases.All),
        new LookupSet<ChargeShortfallPolicyType>("charge-shortfall-policies", LookupCodes.ChargeShortfallPolicies.All),
        new LookupSet<LedgerAccountType>("ledger-account-types", LookupCodes.LedgerAccountTypes.All)
    ];

    private readonly BankingDbContext _db;
    private readonly IActivityContext _activity;

    public LookupAdmin(BankingDbContext db, IActivityContext activity)
    {
        _db = db;
        _activity = activity;
    }

    public static IReadOnlyList<LookupTypeInfo> Types { get; } =
        Sets.Select(static set => new LookupTypeInfo(set.Slug, set.TableName)).ToArray();

    public Task<IReadOnlyList<LookupRow>>? ListAsync(string slug, bool includeInactive, CancellationToken cancellationToken) =>
        Find(slug)?.ListAsync(_db, includeInactive, cancellationToken);

    public async Task<OperationResult<int>> CreateAsync(string slug, string? code, string? name, CancellationToken cancellationToken)
    {
        var set = Find(slug);
        if (set is null)
        {
            return OperationResult.Fail<int>(ProblemCodes.NotFound);
        }

        var normalizedCode = code?.Trim().ToUpperInvariant() ?? string.Empty;
        var normalizedName = name?.Trim() ?? string.Empty;
        var errors = Validate(normalizedName);
        if (normalizedCode.Length is 0 or > LookupCodes.MaxCodeLength
            || !normalizedCode.All(static character => char.IsAsciiLetterUpper(character) || char.IsAsciiDigit(character) || character == '_'))
        {
            errors["code"] = ["Use 1 to 30 capital letters, digits or underscores."];
        }

        if (errors.Count > 0)
        {
            return OperationResult.Fail<int>(ProblemCodes.Validation, errors);
        }

        if (await set.CodeExistsAsync(_db, normalizedCode, cancellationToken).ConfigureAwait(false))
        {
            return OperationResult.Fail<int>(ProblemCodes.LookupCodeTaken);
        }

        _activity.Record(EventCodes.CreateRecord, set.TableName, normalizedCode);
        var id = await set.CreateAsync(_db, normalizedCode, normalizedName, cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(id);
    }

    public async Task<OperationResult<int>> UpdateAsync(
        string slug,
        int id,
        string? code,
        string? name,
        bool isActive,
        CancellationToken cancellationToken)
    {
        var set = Find(slug);
        if (set is null)
        {
            return OperationResult.Fail<int>(ProblemCodes.NotFound);
        }

        var row = await set.FindAsync(_db, id, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return OperationResult.Fail<int>(ProblemCodes.NotFound);
        }

        if (code is not null && !string.Equals(code.Trim(), row.Code, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Fail<int>(ProblemCodes.LookupCodeImmutable);
        }

        var normalizedName = name?.Trim() ?? string.Empty;
        var errors = Validate(normalizedName);
        if (errors.Count > 0)
        {
            return OperationResult.Fail<int>(ProblemCodes.Validation, errors);
        }

        if (!isActive && set.IsCoded(row.Code))
        {
            return OperationResult.Fail<int>(ProblemCodes.LookupCodedRow);
        }

        row.Name = normalizedName;
        row.IsActive = isActive;
        _activity.Record(EventCodes.UpdateRecord, set.TableName, row.Code);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(id);
    }

    private static Dictionary<string, string[]> Validate(string name)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (name.Length is 0 or > 100)
        {
            errors["name"] = ["Enter a name of up to 100 characters."];
        }

        return errors;
    }

    private static ILookupSet? Find(string slug) =>
        Sets.FirstOrDefault(set => string.Equals(set.Slug, slug, StringComparison.OrdinalIgnoreCase));

    private interface ILookupSet
    {
        string Slug { get; }

        string TableName { get; }

        bool IsCoded(string code);

        Task<IReadOnlyList<LookupRow>> ListAsync(BankingDbContext db, bool includeInactive, CancellationToken cancellationToken);

        Task<bool> CodeExistsAsync(BankingDbContext db, string code, CancellationToken cancellationToken);

        Task<Lookup?> FindAsync(BankingDbContext db, int id, CancellationToken cancellationToken);

        Task<int> CreateAsync(BankingDbContext db, string code, string name, CancellationToken cancellationToken);
    }

    private sealed class LookupSet<TLookup> : ILookupSet
        where TLookup : Lookup, new()
    {
        private readonly HashSet<string> _coded;

        public LookupSet(string slug, IReadOnlyList<string> codedValues)
        {
            Slug = slug;
            _coded = new HashSet<string>(codedValues, StringComparer.Ordinal);
        }

        public string Slug { get; }

        public string TableName => typeof(TLookup).Name;

        public bool IsCoded(string code) => _coded.Contains(code);

        public async Task<IReadOnlyList<LookupRow>> ListAsync(BankingDbContext db, bool includeInactive, CancellationToken cancellationToken)
        {
            var rows = await db.Set<TLookup>()
                .AsNoTracking()
                .Where(row => includeInactive || row.IsActive)
                .OrderBy(row => row.Name)
                .Select(row => new { row.Id, row.Code, row.Name, row.IsActive })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return rows.Select(row => new LookupRow(row.Id, row.Code, row.Name, row.IsActive, _coded.Contains(row.Code))).ToList();
        }

        public Task<bool> CodeExistsAsync(BankingDbContext db, string code, CancellationToken cancellationToken) =>
            db.Set<TLookup>().AnyAsync(row => row.Code == code, cancellationToken);

        public async Task<Lookup?> FindAsync(BankingDbContext db, int id, CancellationToken cancellationToken) =>
            await db.Set<TLookup>().SingleOrDefaultAsync(row => row.Id == id, cancellationToken).ConfigureAwait(false);

        public async Task<int> CreateAsync(BankingDbContext db, string code, string name, CancellationToken cancellationToken)
        {
            var row = new TLookup { Code = code, Name = name, IsActive = true };
            db.Set<TLookup>().Add(row);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return row.Id;
        }
    }
}
