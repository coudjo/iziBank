using Banking.Application.Parties;
using Banking.Application.Supervision;
using Banking.Domain.Lookups;
using Banking.Domain.Parties;
using Banking.Domain.Supervision;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Banking.Infrastructure.Parties;

/// <summary>A party referred to by another (owner, member): its id, number, name and type.</summary>
public sealed record PartyReference(long PartyId, string PartyNumber, string DisplayName, string PartyTypeCode);

/// <summary>Shared pieces of party creation and change (plan 6.1 to 6.5).</summary>
public sealed class PartyCommon
{
    private const string NextPartyNumberSql = "SELECT NEXT VALUE FOR [dbo].[PartyNumberSequence] AS [Value]";

    private readonly BankingDbContext _db;
    private readonly LookupIdCache _lookups;
    private readonly SupervisionOptions _supervision;
    private readonly PartyNumberOptions _partyNumbers;

    public PartyCommon(
        BankingDbContext db,
        LookupIdCache lookups,
        IOptions<SupervisionOptions> supervision,
        IOptions<PartyNumberOptions> partyNumbers)
    {
        ArgumentNullException.ThrowIfNull(supervision);
        ArgumentNullException.ThrowIfNull(partyNumbers);
        _db = db;
        _lookups = lookups;
        _supervision = supervision.Value;
        _partyNumbers = partyNumbers.Value;
    }

    /// <summary>A new party of the given type: number, display name, KYC PENDING, Pending supervision (R1).</summary>
    public async Task<Party> NewPartyAsync(string partyTypeCode, string displayName, CancellationToken cancellationToken)
    {
        var party = new Party
        {
            PartyNumber = await NextPartyNumberAsync(cancellationToken).ConfigureAwait(false),
            DisplayName = displayName,
            PartyTypeId = await _lookups.GetIdAsync<PartyType>(_db, partyTypeCode, cancellationToken).ConfigureAwait(false),
            KYCStatusId = await _lookups.GetIdAsync<KYCStatusType>(_db, LookupCodes.KycStatuses.Pending, cancellationToken).ConfigureAwait(false)
        };
        SupervisionRules.StartNew(party, _supervision.Enabled);
        return party;
    }

    /// <summary>
    /// Plan 6.5: a material change (owners, members, chairperson, documents, controlled fields) sets KYC back to
    /// PENDING and, under R4, puts the record back to Pending supervision.
    /// </summary>
    public async Task MarkMaterialChangeAsync(Party party, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(party);
        party.KYCStatusId = await _lookups.GetIdAsync<KYCStatusType>(_db, LookupCodes.KycStatuses.Pending, cancellationToken).ConfigureAwait(false);
        SupervisionRules.ResetAfterControlledEdit(party, _supervision.Enabled);
    }

    /// <summary>Plan 5.9: <c>NEXT VALUE FOR PartyNumberSequence</c>, zero padded. Gaps are acceptable.</summary>
    public async Task<string> NextPartyNumberAsync(CancellationToken cancellationToken)
    {
        // Not composed (no Single/First): SQL Server does not allow NEXT VALUE FOR inside a derived table.
        var values = await _db.Database
            .SqlQueryRaw<long>(NextPartyNumberSql)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return PartyRules.FormatPartyNumber(values[0], _partyNumbers.Length);
    }

    public async Task<bool> IsDocumentTypeForAsync(int documentTypeId, IReadOnlyList<string> allowedCodes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(allowedCodes);
        var code = await _db.Set<IdentificationDocumentType>()
            .AsNoTracking()
            .Where(row => row.Id == documentTypeId && row.IsActive)
            .Select(row => row.Code)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return code is not null && allowedCodes.Contains(code);
    }

    /// <summary>Whoever already holds this document type and number, on an individual's detail row or as any document.</summary>
    public async Task<string?> DocumentHolderAsync(int documentTypeId, string documentNumber, CancellationToken cancellationToken)
    {
        var onDetail = await _db.Set<IndividualDetail>()
            .AsNoTracking()
            .Where(row => row.IdentificationTypeId == documentTypeId && row.IdentificationNumber == documentNumber)
            .Select(row => row.Party!.PartyNumber)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return onDetail ?? await _db.Set<PartyIdentificationDocument>()
            .AsNoTracking()
            .Where(row => row.DocumentTypeId == documentTypeId && row.DocumentNumber == documentNumber)
            .Select(row => row.Party!.PartyNumber)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Dictionary<long, PartyReference>> ReferencesAsync(IEnumerable<long> partyIds, CancellationToken cancellationToken)
    {
        var ids = partyIds.Distinct().ToList();
        return await _db.Set<Party>()
            .AsNoTracking()
            .Where(row => ids.Contains(row.PartyId))
            .Select(row => new PartyReference(row.PartyId, row.PartyNumber, row.DisplayName, row.PartyType!.Code))
            .ToDictionaryAsync(row => row.PartyId, cancellationToken)
            .ConfigureAwait(false);
    }

    public static OperationResult<T> Conflict<T>(string problemCode, string key, string value) =>
        OperationResult.FailWith<T>(problemCode, new Dictionary<string, object?>(StringComparer.Ordinal) { [key] = value });

    /// <summary>Checks the shared contact fields. At least one of mobile or phone when <paramref name="requireNumber"/>.</summary>
    public static void ValidateContact(
        Dictionary<string, string[]> errors,
        string? mobile,
        string? phone,
        string? email,
        string? address1,
        string? address2,
        bool requireAddress,
        bool requireNumber)
    {
        ArgumentNullException.ThrowIfNull(errors);

        if (mobile is { Length: 0 })
        {
            errors["mobile"] = ["Enter a mobile number of 9 to 15 digits, for example 0241234567."];
        }

        if (phone is { Length: 0 })
        {
            errors["phone"] = ["Enter a phone number of 9 to 15 digits, or leave it empty."];
        }

        if (requireNumber && mobile is null && phone is null)
        {
            errors["mobile"] = ["Enter a mobile or phone number."];
        }

        if (Blank(email) is { } address && (address.Length > 100 || !LooksLikeEmail(address)))
        {
            errors["emailAddress"] = ["Enter an email address such as name@example.com, or leave it empty."];
        }

        var line1 = Blank(address1);
        if ((requireAddress && line1 is null) || line1 is { Length: > 200 })
        {
            errors["address1"] = ["Enter the address (up to 200 characters)."];
        }

        if (Blank(address2) is { Length: > 200 })
        {
            errors["address2"] = ["Keep the second address line to 200 characters."];
        }
    }

    public static bool LooksLikeEmail(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var at = value.IndexOf('@', StringComparison.Ordinal);
        return at > 0 && at == value.LastIndexOf('@') && value.IndexOf('.', at) > at + 1
               && !value.EndsWith('.') && !value.Any(char.IsWhiteSpace);
    }

    public static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static Dictionary<string, string[]> NewErrors() => new(StringComparer.Ordinal);
}
