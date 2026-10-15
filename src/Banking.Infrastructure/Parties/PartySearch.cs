using Banking.Application.Abstractions;
using Banking.Domain.Lookups;
using Banking.Domain.Parties;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Banking.Infrastructure.Parties;

public sealed record PartySearchFilter(
    string? Query,
    string? PartyTypeCode,
    string? KycStatusCode,
    string? SupervisionState,
    long? CreditOfficerId,
    bool OldestFirst = false);

/// <summary>A search result with the eligibility hints of UX request 5. No images, ID numbers masked.</summary>
public sealed record PartySearchRow(
    long PartyId,
    string PartyNumber,
    string DisplayName,
    string PartyTypeCode,
    string? Mobile,
    string? IdentificationNumberMasked,
    string EffectiveKycStatusCode,
    string SupervisionState,
    string? CreditOfficerName,
    DateTime CreatedOn,
    bool HasEffectiveSignature,
    bool CanBePrimaryHolder,
    bool CanBeJointHolder,
    bool CanBeSignatory);

public sealed record PartyOverview(
    PartyDetail Party,
    KycChecklist Checklist,
    PhotoView Photo,
    SignatureView? Signature,
    int DocumentCount,
    int NextOfKinCount,
    string? CreditOfficerName,
    PartyReference? ActiveGroup,
    IReadOnlyList<object> Accounts);

/// <summary>
/// A4-08 (plan section 8): search and the 360 overview. A search term is tried as an exact or prefix customer
/// number, an exact mobile, an exact ID number and a name prefix, all indexed; only when those find too few does it
/// fall back to "name contains". Full-Text Search is not used.
/// </summary>
public sealed class PartySearch
{
    private readonly BankingDbContext _db;
    private readonly IActivityContext _activity;
    private readonly KycService _kyc;
    private readonly PartySignatures _signatures;
    private readonly PartyAdmin _parties;
    private readonly PartyDocuments _documents;

    public PartySearch(
        BankingDbContext db,
        IActivityContext activity,
        KycService kyc,
        PartySignatures signatures,
        PartyAdmin parties,
        PartyDocuments documents)
    {
        _db = db;
        _activity = activity;
        _kyc = kyc;
        _signatures = signatures;
        _parties = parties;
        _documents = documents;
    }

    public async Task<PagedResult<PartySearchRow>> SearchAsync(PartySearchFilter filter, int page, int pageSize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        // The search term itself is personal data: only the filters reach the activity log.
        _activity.Record(EventCodes.Search, nameof(Party), details: new Dictionary<string, object?>
        {
            ["hasQuery"] = !string.IsNullOrWhiteSpace(filter.Query),
            ["partyType"] = filter.PartyTypeCode,
            ["kycStatus"] = filter.KycStatusCode,
            ["supervision"] = filter.SupervisionState
        });

        var term = filter.Query?.Trim() ?? string.Empty;
        var filtered = Filtered(filter);
        IQueryable<Party> query;

        if (term.Length == 0)
        {
            query = filtered;
        }
        else
        {
            query = await IndexedMatchesAsync(filtered, term, cancellationToken).ConfigureAwait(false);
            if (term.Length >= 3 && await query.CountAsync(cancellationToken).ConfigureAwait(false) < page * pageSize)
            {
                // Fallback: names containing the term. Slower (no seek), only reached when the indexed matches run out.
                var pattern = "%" + Escape(term) + "%";
                query = query.Union(filtered.Where(party => EF.Functions.Like(party.DisplayName, pattern)));
            }
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var ordered = term.Length > 0
            ? query.OrderBy(party => party.PartyNumber == term ? 0 : 1).ThenBy(party => party.DisplayName).ThenBy(party => party.PartyId)
            : filter.OldestFirst
                ? query.OrderBy(party => party.CreatedOn).ThenBy(party => party.PartyId)
                : query.OrderByDescending(party => party.CreatedOn).ThenByDescending(party => party.PartyId);

        var rows = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(party => new
            {
                party.PartyId,
                party.PartyNumber,
                party.DisplayName,
                TypeCode = party.PartyType!.Code,
                party.Mobile,
                IdNumber = party.IndividualDetail == null ? null : party.IndividualDetail.IdentificationNumber,
                Approved = !party.IsSupervisionRequired || party.SupervisedByUserId != null,
                SupervisionState = !party.IsSupervisionRequired ? PartySupervisionStates.NotRequired
                    : party.SupervisedByUserId == null ? PartySupervisionStates.Pending : PartySupervisionStates.Approved,
                CreditOfficerName = party.CreditOfficerId == null ? null : party.CreditOfficer!.StaffUser!.FullName,
                party.CreatedOn
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var ids = rows.Select(row => row.PartyId).ToList();
        var statuses = await _kyc.EffectiveStatusesAsync(ids, cancellationToken).ConfigureAwait(false);
        var signed = await _signatures.WithEffectiveSignatureAsync(ids, cancellationToken).ConfigureAwait(false);

        var items = rows.Select(row =>
            {
                var status = statuses.GetValueOrDefault(row.PartyId) ?? LookupCodes.KycStatuses.Pending;
                var ready = row.Approved && status == LookupCodes.KycStatuses.Verified;
                var individual = row.TypeCode == LookupCodes.PartyTypes.Individual;
                var hasSignature = signed.Contains(row.PartyId);
                return new PartySearchRow(
                    row.PartyId,
                    row.PartyNumber,
                    row.DisplayName,
                    row.TypeCode,
                    row.Mobile,
                    row.IdNumber is null ? null : PartyRules.MaskIdentificationNumber(row.IdNumber),
                    status,
                    row.SupervisionState,
                    row.CreditOfficerName,
                    row.CreatedOn,
                    hasSignature,
                    CanBePrimaryHolder: ready,
                    CanBeJointHolder: ready && individual && hasSignature,
                    CanBeSignatory: ready && individual && hasSignature);
            })
            .ToList();

        return new PagedResult<PartySearchRow>(items, page, pageSize, total);
    }

    /// <summary>
    /// The indexed part of a search, as a UNION of seeks: customer number prefix, name prefix, exact mobile, and the
    /// parties holding an exact ID number. Public so a test can check the query plan.
    /// </summary>
    public async Task<IQueryable<Party>> IndexedMatchesAsync(IQueryable<Party> source, string term, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(term);

        var prefix = Escape(term) + "%";
        var query = source.Where(party => EF.Functions.Like(party.PartyNumber, prefix))
            .Union(source.Where(party => EF.Functions.Like(party.DisplayName, prefix)));

        if (PartyRules.NormalizePhone(term) is { Length: > 0 } phone)
        {
            query = query.Union(source.Where(party => party.Mobile == phone));
        }

        // The ID index leads with the document type, so name every type to keep this a seek.
        var idNumber = PartyRules.NormalizeIdentificationNumber(term);
        var typeIds = await _db.Set<IdentificationDocumentType>()
            .AsNoTracking()
            .Select(type => (int?)type.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var holders = await _db.Set<IndividualDetail>()
            .AsNoTracking()
            .Where(detail => typeIds.Contains(detail.IdentificationTypeId) && detail.IdentificationNumber == idNumber)
            .Select(detail => detail.PartyId)
            .Take(20)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var holder in holders)
        {
            query = query.Union(source.Where(party => party.PartyId == holder));
        }

        return query;
    }

    public IQueryable<Party> Parties() => _db.Set<Party>().AsNoTracking();

    /// <summary>The 360 view (UX 7.3): the party, its checklist, photo and signature state, counts, and accounts (none until A6).</summary>
    public async Task<PartyOverview?> OverviewAsync(long partyId, CancellationToken cancellationToken)
    {
        var party = await _parties.GetAsync(partyId, cancellationToken).ConfigureAwait(false);
        if (party is null)
        {
            return null;
        }

        var checklist = (await _kyc.ChecklistAsync(partyId, cancellationToken).ConfigureAwait(false))!;
        var photo = (await _documents.GetPhotoAsync(partyId, cancellationToken).ConfigureAwait(false))!;
        var signature = party.PartyTypeCode == LookupCodes.PartyTypes.Individual
            ? await _signatures.GetAsync(partyId, cancellationToken).ConfigureAwait(false)
            : null;

        var counts = await _db.Set<Party>()
            .AsNoTracking()
            .Where(row => row.PartyId == partyId)
            .Select(row => new
            {
                Documents = row.IdentificationDocuments.Count(),
                NextOfKin = row.NextOfKin.Count(),
                CreditOfficerName = row.CreditOfficerId == null ? null : row.CreditOfficer!.StaffUser!.FullName,
                ActiveGroup = row.GroupMemberships
                    .Where(member => member.LeftOn == null)
                    .Select(member => new PartyReference(
                        member.GroupPartyId,
                        member.GroupParty!.PartyNumber,
                        member.GroupParty.DisplayName,
                        member.GroupParty.PartyType!.Code))
                    .FirstOrDefault()
            })
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PartyOverview(party, checklist, photo, signature, counts.Documents, counts.NextOfKin, counts.CreditOfficerName, counts.ActiveGroup, []);
    }

    private IQueryable<Party> Filtered(PartySearchFilter filter)
    {
        var query = _db.Set<Party>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.PartyTypeCode))
        {
            var type = filter.PartyTypeCode.Trim().ToUpperInvariant();
            query = query.Where(party => party.PartyType!.Code == type);
        }

        if (!string.IsNullOrWhiteSpace(filter.KycStatusCode))
        {
            var status = filter.KycStatusCode.Trim().ToUpperInvariant();
            query = query.Where(party => party.KYCStatus!.Code == status);
        }

        switch (filter.SupervisionState?.Trim().ToUpperInvariant())
        {
            case PartySupervisionStates.Pending:
                query = query.Where(party => party.IsSupervisionRequired && party.SupervisedByUserId == null);
                break;
            case PartySupervisionStates.Approved:
                query = query.Where(party => !party.IsSupervisionRequired || party.SupervisedByUserId != null);
                break;
        }

        if (filter.CreditOfficerId is { } officerId)
        {
            query = query.Where(party => party.CreditOfficerId == officerId);
        }

        return query;
    }

    private static string Escape(string value) =>
        value.Replace("[", "[[]", StringComparison.Ordinal)
            .Replace("%", "[%]", StringComparison.Ordinal)
            .Replace("_", "[_]", StringComparison.Ordinal);
}
