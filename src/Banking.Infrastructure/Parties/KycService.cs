using Banking.Application.Abstractions;
using Banking.Application.Errors;
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

public sealed record KycChecklist(
    long PartyId,
    string PartyTypeCode,
    string? StoredStatus,
    string EffectiveStatus,
    string SupervisionState,
    bool IsComplete,
    IReadOnlyList<KycChecklistItem> Items);

/// <summary>
/// A4-07 (plan 6.5): the KYC checklist, verify (which is also the record's approval, D3), reject, and the computed
/// EXPIRED status.
/// </summary>
public sealed class KycService
{
    private readonly BankingDbContext _db;
    private readonly IActivityContext _activity;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly LookupIdCache _lookups;
    private readonly PartySignatures _signatures;
    private readonly SupervisionOptions _supervision;
    private readonly KycOptions _kyc;

    public KycService(
        BankingDbContext db,
        IActivityContext activity,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        LookupIdCache lookups,
        PartySignatures signatures,
        IOptions<SupervisionOptions> supervision,
        IOptions<KycOptions> kyc)
    {
        ArgumentNullException.ThrowIfNull(supervision);
        ArgumentNullException.ThrowIfNull(kyc);
        _db = db;
        _activity = activity;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
        _lookups = lookups;
        _signatures = signatures;
        _supervision = supervision.Value;
        _kyc = kyc.Value;
    }

    private DateOnly Today => DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

    /// <summary>Effective KYC status per party: stored, except VERIFIED reads EXPIRED while the required document has expired.</summary>
    public async Task<Dictionary<long, string>> EffectiveStatusesAsync(IEnumerable<long> partyIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(partyIds);
        var ids = partyIds.Distinct().ToList();
        var today = Today;

        var rows = await _db.Set<Party>()
            .AsNoTracking()
            .Where(party => ids.Contains(party.PartyId))
            .Select(party => new
            {
                party.PartyId,
                Stored = party.KYCStatusId == null ? null : party.KYCStatus!.Code,

                // Individual: the primary ID document has expired.
                PrimaryExpired = party.IndividualDetail != null && party.IdentificationDocuments.Any(document =>
                    document.DocumentTypeId == party.IndividualDetail.IdentificationTypeId
                    && document.DocumentNumber == party.IndividualDetail.IdentificationNumber
                    && document.ExpiresOn < today),

                // Business: it has documents and every one of them has expired.
                AllExpired = party.BusinessDetail != null
                    && party.IdentificationDocuments.Any()
                    && !party.IdentificationDocuments.Any(document => document.ExpiresOn == null || document.ExpiresOn >= today)
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.ToDictionary(
            row => row.PartyId,
            row => KycRequirementPolicy.EffectiveStatus(row.Stored, row.PrimaryExpired || row.AllExpired));
    }

    public async Task<KycChecklist?> ChecklistAsync(long partyId, CancellationToken cancellationToken)
    {
        var party = await _db.Set<Party>()
            .AsNoTracking()
            .Where(row => row.PartyId == partyId)
            .Select(row => new
            {
                row.PartyId,
                TypeCode = row.PartyType!.Code,
                Stored = row.KYCStatusId == null ? null : row.KYCStatus!.Code,
                SupervisionState = !row.IsSupervisionRequired ? PartySupervisionStates.NotRequired
                    : row.SupervisedByUserId == null ? PartySupervisionStates.Pending : PartySupervisionStates.Approved
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (party is null)
        {
            return null;
        }

        var facts = await FactsAsync(partyId, party.TypeCode, cancellationToken).ConfigureAwait(false);
        var items = KycRequirementPolicy.For(party.TypeCode, facts);
        var effective = (await EffectiveStatusesAsync([partyId], cancellationToken).ConfigureAwait(false))[partyId];
        return new KycChecklist(party.PartyId, party.TypeCode, party.Stored, effective, party.SupervisionState, KycRequirementPolicy.IsComplete(items), items);
    }

    /// <summary>
    /// D3: verifying KYC also approves the record. Needs a complete checklist, a PENDING or REJECTED status, and a
    /// verifier who neither created nor last changed the customer.
    /// </summary>
    public async Task<OperationResult<long>> VerifyAsync(long partyId, CancellationToken cancellationToken)
    {
        var verifierId = _currentUser.StaffUserId ?? throw new InvalidOperationException("Verification needs a signed-in user.");
        var party = await _db.Set<Party>()
            .Include(row => row.KYCStatus)
            .SingleOrDefaultAsync(row => row.PartyId == partyId, cancellationToken)
            .ConfigureAwait(false);
        if (party is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        if (party.KYCStatus?.Code is LookupCodes.KycStatuses.Verified)
        {
            var effective = (await EffectiveStatusesAsync([partyId], cancellationToken).ConfigureAwait(false))[partyId];
            if (effective == LookupCodes.KycStatuses.Verified)
            {
                return OperationResult.Fail<long>(ProblemCodes.KycNotPending);
            }
        }

        var checklist = (await ChecklistAsync(partyId, cancellationToken).ConfigureAwait(false))!;
        if (!checklist.IsComplete)
        {
            return OperationResult.FailWith<long>(
                ProblemCodes.KycChecklistIncomplete,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["missing"] = checklist.Items.Where(item => item.RequiredForKyc && !item.Done).Select(item => item.Code).ToArray()
                });
        }

        if (!_supervision.AllowSelfApproval && (party.CreatedByUserId == verifierId || party.ModifiedByUserId == verifierId))
        {
            return OperationResult.Fail<long>(SupervisionProblems.SelfApproval);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        party.KYCStatusId = await _lookups.GetIdAsync<KYCStatusType>(_db, LookupCodes.KycStatuses.Verified, cancellationToken).ConfigureAwait(false);
        if (SupervisionRules.StateOf(party) == SupervisionState.Pending)
        {
            SupervisionRules.Approve(party, verifierId, now);
        }

        _activity.Record(EventCodes.KycVerify, nameof(Party), party.PartyNumber);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(partyId);
    }

    /// <summary>PENDING or VERIFIED becomes REJECTED. The reason is kept in the activity log.</summary>
    public async Task<OperationResult<long>> RejectAsync(long partyId, string? reason, CancellationToken cancellationToken)
    {
        var text = PartyCommon.Blank(reason);
        if (text is null || text.Length > 300)
        {
            return OperationResult.Fail<long>(
                ProblemCodes.Validation,
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["reason"] = ["Give the reason for rejecting (up to 300 characters)."] });
        }

        var party = await _db.Set<Party>()
            .Include(row => row.KYCStatus)
            .SingleOrDefaultAsync(row => row.PartyId == partyId, cancellationToken)
            .ConfigureAwait(false);
        if (party is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        if (party.KYCStatus?.Code is LookupCodes.KycStatuses.Rejected)
        {
            return OperationResult.Fail<long>(ProblemCodes.KycNotPending);
        }

        party.KYCStatusId = await _lookups.GetIdAsync<KYCStatusType>(_db, LookupCodes.KycStatuses.Rejected, cancellationToken).ConfigureAwait(false);
        _activity.Record(EventCodes.KycReject, nameof(Party), party.PartyNumber, new Dictionary<string, object?> { ["reason"] = text });
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(partyId);
    }

    private async Task<KycFacts> FactsAsync(long partyId, string typeCode, CancellationToken cancellationToken)
    {
        var today = Today;
        var party = await _db.Set<Party>()
            .AsNoTracking()
            .Where(row => row.PartyId == partyId)
            .Select(row => new
            {
                row.Mobile,
                row.Phone,
                row.Address1,
                row.SourceOfFunds,
                row.OccupationOrSector,
                Individual = row.IndividualDetail == null ? null : new
                {
                    row.IndividualDetail.FirstName,
                    row.IndividualDetail.LastName,
                    row.IndividualDetail.DateOfBirth,
                    row.IndividualDetail.GenderId,
                    row.IndividualDetail.Nationality,
                    row.IndividualDetail.IdentificationTypeId,
                    row.IndividualDetail.IdentificationNumber
                },
                Business = row.BusinessDetail == null ? null : new
                {
                    row.BusinessDetail.BusinessName,
                    row.BusinessDetail.RegistrationNumber,
                    row.BusinessDetail.BusinessCategoryId,
                    row.BusinessDetail.TaxIdentificationNumber
                },
                ChairpersonPartyId = row.GroupDetail == null ? null : row.GroupDetail.ChairpersonPartyId,
                ApprovedPhoto = _db.Set<PartyImage>().Any(photo => photo.PartyId == row.PartyId && photo.ImageValue != null
                    && (!photo.IsSupervisionRequired || photo.SupervisedByUserId != null)),
                DocumentWithImage = row.IdentificationDocuments.Any(document => document.ImageValue != null
                    && (document.ExpiresOn == null || document.ExpiresOn >= today)),
                NextOfKin = row.NextOfKin.Count()
            })
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        var facts = new KycFacts
        {
            HasApprovedPhoto = party.ApprovedPhoto,
            HasDocumentWithImage = party.DocumentWithImage,
            NextOfKinCount = party.NextOfKin,
            HasSourceOfFunds = !string.IsNullOrWhiteSpace(party.SourceOfFunds),
            HasOccupationOrSector = !string.IsNullOrWhiteSpace(party.OccupationOrSector),
            MinimumMembers = _kyc.MinimumGroupMembers
        };

        switch (typeCode)
        {
            case LookupCodes.PartyTypes.Individual when party.Individual is { } individual:
                facts = facts with
                {
                    HasCreationFields = individual.FirstName.Length > 0 && individual.LastName.Length > 0
                        && individual.DateOfBirth != null && individual.GenderId != null
                        && !string.IsNullOrWhiteSpace(individual.Nationality) && !string.IsNullOrWhiteSpace(party.Mobile)
                        && !string.IsNullOrWhiteSpace(party.Address1) && individual.IdentificationTypeId != null
                        && !string.IsNullOrWhiteSpace(individual.IdentificationNumber),
                    HasEffectiveSignature = (await _signatures.WithEffectiveSignatureAsync([partyId], cancellationToken).ConfigureAwait(false)).Count > 0
                };
                break;

            case LookupCodes.PartyTypes.Business when party.Business is { } business:
            {
                var owners = await _db.Set<PartyBeneficialOwner>()
                    .AsNoTracking()
                    .Where(row => row.BusinessPartyId == partyId)
                    .Select(row => new { row.OwnerPartyId, row.OwnershipPercentage })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                var major = owners.Where(owner => owner.OwnershipPercentage >= _kyc.BeneficialOwnerThresholdPercent).Select(owner => owner.OwnerPartyId).ToList();
                var statuses = await EffectiveStatusesAsync(major, cancellationToken).ConfigureAwait(false);

                facts = facts with
                {
                    HasCreationFields = business.BusinessName.Length > 0 && !string.IsNullOrWhiteSpace(business.RegistrationNumber)
                        && business.BusinessCategoryId != null
                        && (!string.IsNullOrWhiteSpace(party.Mobile) || !string.IsNullOrWhiteSpace(party.Phone))
                        && !string.IsNullOrWhiteSpace(party.Address1) && !string.IsNullOrWhiteSpace(party.SourceOfFunds),
                    HasTaxNumber = !string.IsNullOrWhiteSpace(business.TaxIdentificationNumber),
                    BeneficialOwnerCount = owners.Count,
                    UnverifiedMajorOwnerCount = major.Count(id => statuses.GetValueOrDefault(id) != LookupCodes.KycStatuses.Verified)
                };
                break;
            }

            case LookupCodes.PartyTypes.Group:
            {
                var members = await _db.Set<GroupMember>()
                    .AsNoTracking()
                    .Where(row => row.GroupPartyId == partyId && row.LeftOn == null)
                    .Select(row => row.MemberPartyId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                var statuses = await EffectiveStatusesAsync(members, cancellationToken).ConfigureAwait(false);

                facts = facts with
                {
                    HasChairperson = party.ChairpersonPartyId != null,
                    ActiveMemberCount = members.Count,
                    UnverifiedMemberCount = members.Count(id => statuses.GetValueOrDefault(id) != LookupCodes.KycStatuses.Verified)
                };
                break;
            }
        }

        return facts;
    }
}
