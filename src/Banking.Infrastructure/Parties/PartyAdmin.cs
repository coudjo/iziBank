using System.Globalization;
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

public sealed record NewIndividual(
    string? Title,
    string FirstName,
    string LastName,
    DateOnly? DateOfBirth,
    int? GenderId,
    string? Nationality,
    string? Mobile,
    string? Phone,
    string? EmailAddress,
    string? Address1,
    string? Address2,
    int? IdentificationTypeId,
    string? IdentificationNumber,
    DateOnly? IdentificationIssuedOn,
    DateOnly? IdentificationExpiresOn,
    string? SourceOfFunds,
    string? OccupationOrSector,
    bool IsPoliticallyExposed);

public sealed record CreatedParty(long PartyId, string PartyNumber);

public sealed record IndividualView(
    string? Title,
    string FirstName,
    string LastName,
    int? GenderId,
    string? GenderCode,
    DateOnly? DateOfBirth,
    string? Nationality,
    int? IdentificationTypeId,
    string? IdentificationTypeCode,
    string? IdentificationNumber);

public sealed record PartyDetail(
    long PartyId,
    string PartyNumber,
    string DisplayName,
    string PartyTypeCode,
    string? KycStatusCode,
    string SupervisionState,
    string? Mobile,
    string? Phone,
    string? EmailAddress,
    string? Address1,
    string? Address2,
    string? SourceOfFunds,
    string? OccupationOrSector,
    bool IsPoliticallyExposed,
    DateTime CreatedOn,
    long? CreatedByUserId,
    long? ModifiedByUserId,
    long? SupervisedByUserId,
    IndividualView? Individual,
    BusinessView? Business,
    GroupView? Group)
{
    /// <summary>KYC as it applies today: VERIFIED reads EXPIRED while the required document has expired.</summary>
    public string? EffectiveKycStatusCode { get; init; }
}

public sealed record BusinessView(
    string BusinessName,
    string? RegistrationNumber,
    string? TaxIdentificationNumber,
    DateOnly? IncorporationDate,
    int? BusinessCategoryId,
    string? BusinessCategoryCode,
    IReadOnlyList<BeneficialOwnerView> BeneficialOwners);

public sealed record GroupView(
    string GroupName,
    DateOnly? FormationDate,
    string? MeetingFrequency,
    long? ChairpersonPartyId,
    string? ChairpersonPartyNumber,
    string? ChairpersonName,
    int ActiveMemberCount);

public static class PartySupervisionStates
{
    public const string Pending = "PENDING";
    public const string Approved = "APPROVED";
    public const string NotRequired = "NOT_REQUIRED";
}

/// <summary>
/// A4-01: customer numbers, display names, and creating an individual (plan 6.1, 6.2). A new party is KYC
/// <c>PENDING</c> and Pending supervision; KYC verification approves it in the same step (D3).
/// </summary>
public sealed class PartyAdmin
{
    private const string NextPartyNumberSql = "SELECT NEXT VALUE FOR [dbo].[PartyNumberSequence] AS [Value]";

    private readonly BankingDbContext _db;
    private readonly IActivityContext _activity;
    private readonly LookupIdCache _lookups;
    private readonly TimeProvider _timeProvider;
    private readonly SupervisionOptions _supervision;
    private readonly PartyNumberOptions _partyNumbers;
    private readonly KycOptions _kyc;
    private readonly PartyCommon _common;
    private readonly KycService _kycService;

    public PartyAdmin(
        BankingDbContext db,
        IActivityContext activity,
        LookupIdCache lookups,
        TimeProvider timeProvider,
        IOptions<SupervisionOptions> supervision,
        IOptions<PartyNumberOptions> partyNumbers,
        IOptions<KycOptions> kyc,
        PartyCommon common,
        KycService kycService)
    {
        ArgumentNullException.ThrowIfNull(supervision);
        ArgumentNullException.ThrowIfNull(partyNumbers);
        ArgumentNullException.ThrowIfNull(kyc);
        _db = db;
        _activity = activity;
        _lookups = lookups;
        _timeProvider = timeProvider;
        _supervision = supervision.Value;
        _partyNumbers = partyNumbers.Value;
        _kyc = kyc.Value;
        _common = common;
        _kycService = kycService;
    }

    public async Task<OperationResult<CreatedParty>> CreateIndividualAsync(NewIndividual draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var firstName = draft.FirstName.Trim();
        var lastName = draft.LastName.Trim();
        var idNumber = PartyRules.NormalizeIdentificationNumber(draft.IdentificationNumber ?? string.Empty);
        var mobile = PartyRules.NormalizePhone(draft.Mobile);
        var phone = PartyRules.NormalizePhone(draft.Phone);
        var errors = await ValidateIndividualAsync(draft, firstName, lastName, idNumber, mobile, phone, cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return OperationResult.Fail<CreatedParty>(ProblemCodes.Validation, errors);
        }

        var idTypeId = draft.IdentificationTypeId!.Value;
        if (await ExistingHolderAsync(idTypeId, idNumber, cancellationToken).ConfigureAwait(false) is { } existing)
        {
            return Duplicate(existing);
        }

        var party = new Party
        {
            PartyNumber = await NextPartyNumberAsync(cancellationToken).ConfigureAwait(false),
            DisplayName = PartyRules.IndividualDisplayName(firstName, lastName),
            PartyTypeId = await _lookups.GetIdAsync<PartyType>(_db, LookupCodes.PartyTypes.Individual, cancellationToken).ConfigureAwait(false),
            KYCStatusId = await _lookups.GetIdAsync<KYCStatusType>(_db, LookupCodes.KycStatuses.Pending, cancellationToken).ConfigureAwait(false),
            Address1 = Blank(draft.Address1),
            Address2 = Blank(draft.Address2),
            Mobile = mobile,
            Phone = phone,
            EmailAddress = Blank(draft.EmailAddress),
            SourceOfFunds = Blank(draft.SourceOfFunds),
            OccupationOrSector = Blank(draft.OccupationOrSector),
            IsPoliticallyExposed = draft.IsPoliticallyExposed,
            IndividualDetail = new IndividualDetail
            {
                Title = Blank(draft.Title),
                FirstName = firstName,
                LastName = lastName,
                GenderId = draft.GenderId,
                DateOfBirth = draft.DateOfBirth,
                Nationality = Blank(draft.Nationality),
                IdentificationTypeId = idTypeId,
                IdentificationNumber = idNumber
            }
        };

        // The primary ID lives on IndividualDetail and as a document row; the row carries images and expiry (6.2).
        party.IdentificationDocuments.Add(new PartyIdentificationDocument
        {
            DocumentTypeId = idTypeId,
            DocumentNumber = idNumber,
            IssuedOn = draft.IdentificationIssuedOn,
            ExpiresOn = draft.IdentificationExpiresOn
        });
        SupervisionRules.StartNew(party, _supervision.Enabled);

        _db.Set<Party>().Add(party);
        _activity.Record(EventCodes.CreateRecord, nameof(Party), party.PartyNumber);

        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // A concurrent create with the same identification won the unique index.
            _db.ChangeTracker.Clear();
            _activity.Written = false;
            if (await ExistingHolderAsync(idTypeId, idNumber, cancellationToken).ConfigureAwait(false) is { } winner)
            {
                return Duplicate(winner);
            }

            throw;
        }

        return OperationResult.Ok(new CreatedParty(party.PartyId, party.PartyNumber));
    }

    /// <summary>
    /// A4-06: edits an individual. Changing a controlled field (names, date of birth, ID type or number) is a material
    /// change: KYC back to PENDING and the record back to Pending approval (R4). Other fields change in place.
    /// The primary ID document row follows the details.
    /// </summary>
    public async Task<OperationResult<long>> UpdateIndividualAsync(long partyId, NewIndividual draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var party = await _db.Set<Party>()
            .Include(row => row.IndividualDetail)
            .SingleOrDefaultAsync(row => row.PartyId == partyId && row.IndividualDetail != null, cancellationToken)
            .ConfigureAwait(false);
        if (party is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        var firstName = draft.FirstName.Trim();
        var lastName = draft.LastName.Trim();
        var idNumber = PartyRules.NormalizeIdentificationNumber(draft.IdentificationNumber ?? string.Empty);
        var mobile = PartyRules.NormalizePhone(draft.Mobile);
        var phone = PartyRules.NormalizePhone(draft.Phone);
        var errors = await ValidateIndividualAsync(draft, firstName, lastName, idNumber, mobile, phone, cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return OperationResult.Fail<long>(ProblemCodes.Validation, errors);
        }

        var detail = party.IndividualDetail!;
        var idTypeId = draft.IdentificationTypeId!.Value;
        var idChanged = detail.IdentificationTypeId != idTypeId || detail.IdentificationNumber != idNumber;
        if (idChanged && await OtherHolderAsync(idTypeId, idNumber, partyId, cancellationToken).ConfigureAwait(false) is { } holder)
        {
            return PartyCommon.Conflict<long>(ProblemCodes.PartyDuplicateIdentification, "existingPartyNumber", holder);
        }

        var controlled = idChanged
            || !string.Equals(detail.FirstName, firstName, StringComparison.Ordinal)
            || !string.Equals(detail.LastName, lastName, StringComparison.Ordinal)
            || detail.DateOfBirth != draft.DateOfBirth;

        var primary = await _db.Set<PartyIdentificationDocument>()
            .FirstOrDefaultAsync(
                row => row.PartyId == partyId && row.DocumentTypeId == detail.IdentificationTypeId && row.DocumentNumber == detail.IdentificationNumber,
                cancellationToken)
            .ConfigureAwait(false);
        if (primary is null)
        {
            primary = new PartyIdentificationDocument { PartyId = partyId };
            _db.Set<PartyIdentificationDocument>().Add(primary);
        }

        primary.DocumentTypeId = idTypeId;
        primary.DocumentNumber = idNumber;
        primary.IssuedOn = draft.IdentificationIssuedOn ?? primary.IssuedOn;
        primary.ExpiresOn = draft.IdentificationExpiresOn ?? primary.ExpiresOn;

        detail.Title = Blank(draft.Title);
        detail.FirstName = firstName;
        detail.LastName = lastName;
        detail.DateOfBirth = draft.DateOfBirth;
        detail.GenderId = draft.GenderId;
        detail.Nationality = Blank(draft.Nationality);
        detail.IdentificationTypeId = idTypeId;
        detail.IdentificationNumber = idNumber;

        party.DisplayName = PartyRules.IndividualDisplayName(firstName, lastName);
        party.Mobile = mobile;
        party.Phone = phone;
        party.EmailAddress = Blank(draft.EmailAddress);
        party.Address1 = Blank(draft.Address1);
        party.Address2 = Blank(draft.Address2);
        party.SourceOfFunds = Blank(draft.SourceOfFunds);
        party.OccupationOrSector = Blank(draft.OccupationOrSector);
        party.IsPoliticallyExposed = draft.IsPoliticallyExposed;

        if (controlled)
        {
            await _common.MarkMaterialChangeAsync(party, cancellationToken).ConfigureAwait(false);
        }

        _activity.Record(EventCodes.UpdateRecord, nameof(Party), party.PartyNumber, new Dictionary<string, object?> { ["controlled"] = controlled });

        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            _activity.Written = false;
            if (await OtherHolderAsync(idTypeId, idNumber, partyId, cancellationToken).ConfigureAwait(false) is { } winner)
            {
                return PartyCommon.Conflict<long>(ProblemCodes.PartyDuplicateIdentification, "existingPartyNumber", winner);
            }

            throw;
        }

        return OperationResult.Ok(partyId);
    }

    /// <summary>Full detail, including the full identification number (plan 6.6). Callers log it as a sensitive read.</summary>
    public async Task<PartyDetail?> GetAsync(long partyId, CancellationToken cancellationToken)
    {
        var detail = await _db.Set<Party>()
            .AsNoTracking()
            .Where(row => row.PartyId == partyId)
            .Select(row => new PartyDetail(
                row.PartyId,
                row.PartyNumber,
                row.DisplayName,
                row.PartyType!.Code,
                row.KYCStatusId == null ? null : row.KYCStatus!.Code,
                !row.IsSupervisionRequired
                    ? PartySupervisionStates.NotRequired
                    : row.SupervisedByUserId == null ? PartySupervisionStates.Pending : PartySupervisionStates.Approved,
                row.Mobile,
                row.Phone,
                row.EmailAddress,
                row.Address1,
                row.Address2,
                row.SourceOfFunds,
                row.OccupationOrSector,
                row.IsPoliticallyExposed,
                row.CreatedOn,
                row.CreatedByUserId,
                row.ModifiedByUserId,
                row.SupervisedByUserId,
                row.IndividualDetail == null
                    ? null
                    : new IndividualView(
                        row.IndividualDetail.Title,
                        row.IndividualDetail.FirstName,
                        row.IndividualDetail.LastName,
                        row.IndividualDetail.GenderId,
                        row.IndividualDetail.GenderId == null ? null : row.IndividualDetail.Gender!.Code,
                        row.IndividualDetail.DateOfBirth,
                        row.IndividualDetail.Nationality,
                        row.IndividualDetail.IdentificationTypeId,
                        row.IndividualDetail.IdentificationTypeId == null ? null : row.IndividualDetail.IdentificationType!.Code,
                        row.IndividualDetail.IdentificationNumber),
                null,
                null))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (detail is not null)
        {
            detail = detail with
            {
                Business = await BusinessViewAsync(partyId, cancellationToken).ConfigureAwait(false),
                Group = await GroupViewAsync(partyId, cancellationToken).ConfigureAwait(false),
                EffectiveKycStatusCode = (await _kycService.EffectiveStatusesAsync([partyId], cancellationToken).ConfigureAwait(false))[partyId]
            };
            _activity.Record(EventCodes.ViewRecord, nameof(Party), detail.PartyNumber);
        }

        return detail;
    }

    private async Task<BusinessView?> BusinessViewAsync(long partyId, CancellationToken cancellationToken)
    {
        var business = await _db.Set<BusinessDetail>()
            .AsNoTracking()
            .Where(row => row.PartyId == partyId)
            .Select(row => new
            {
                row.BusinessName,
                row.RegistrationNumber,
                row.TaxIdentificationNumber,
                row.IncorporationDate,
                row.BusinessCategoryId,
                CategoryCode = row.BusinessCategoryId == null ? null : row.BusinessCategory!.Code
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (business is null)
        {
            return null;
        }

        var owners = await _db.Set<PartyBeneficialOwner>()
            .AsNoTracking()
            .Where(row => row.BusinessPartyId == partyId)
            .OrderByDescending(row => row.OwnershipPercentage)
            .Select(row => new BeneficialOwnerView(row.OwnerPartyId, row.Owner!.PartyNumber, row.Owner.DisplayName, row.OwnershipPercentage))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new BusinessView(
            business.BusinessName,
            business.RegistrationNumber,
            business.TaxIdentificationNumber,
            business.IncorporationDate,
            business.BusinessCategoryId,
            business.CategoryCode,
            owners);
    }

    private async Task<GroupView?> GroupViewAsync(long partyId, CancellationToken cancellationToken) =>
        await _db.Set<GroupDetail>()
            .AsNoTracking()
            .Where(row => row.PartyId == partyId)
            .Select(row => new GroupView(
                row.GroupName,
                row.FormationDate,
                row.MeetingFrequency,
                row.ChairpersonPartyId,
                row.ChairpersonPartyId == null ? null : row.Chairperson!.PartyNumber,
                row.ChairpersonPartyId == null ? null : row.Chairperson!.DisplayName,
                _db.Set<GroupMember>().Count(member => member.GroupPartyId == row.PartyId && member.LeftOn == null)))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    private async Task<Dictionary<string, string[]>> ValidateIndividualAsync(
        NewIndividual draft,
        string firstName,
        string lastName,
        string idNumber,
        string? mobile,
        string? phone,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

        if (Blank(draft.Title) is { Length: > 10 })
        {
            errors["title"] = ["Keep the title to 10 characters."];
        }

        if (firstName.Length is 0 or > 60)
        {
            errors["firstName"] = ["Enter the first name (up to 60 characters)."];
        }

        if (lastName.Length is 0 or > 60)
        {
            errors["lastName"] = ["Enter the last name (up to 60 characters)."];
        }

        if (draft.DateOfBirth is not { } dateOfBirth)
        {
            errors["dateOfBirth"] = ["Enter the date of birth."];
        }
        else if (dateOfBirth > today)
        {
            errors["dateOfBirth"] = ["The date of birth cannot be in the future."];
        }
        else if (PartyRules.AgeOn(dateOfBirth, today) < _kyc.MinimumAgeYears)
        {
            errors["dateOfBirth"] = [string.Create(CultureInfo.InvariantCulture, $"The customer must be at least {_kyc.MinimumAgeYears} years old.")];
        }

        if (draft.GenderId is not { } genderId
            || !await _db.Set<GenderType>().AnyAsync(row => row.Id == genderId && row.IsActive, cancellationToken).ConfigureAwait(false))
        {
            errors["genderId"] = ["Choose a gender from the list."];
        }

        if (Blank(draft.Nationality) is not { Length: <= 50 })
        {
            errors["nationality"] = ["Enter the nationality (up to 50 characters)."];
        }

        if (string.IsNullOrEmpty(mobile))
        {
            errors["mobile"] = ["Enter a mobile number of 9 to 15 digits, for example 0241234567."];
        }

        if (phone is { Length: 0 })
        {
            errors["phone"] = ["Enter a phone number of 9 to 15 digits, or leave it empty."];
        }

        if (Blank(draft.EmailAddress) is { } email && (email.Length > 100 || !LooksLikeEmail(email)))
        {
            errors["emailAddress"] = ["Enter an email address such as name@example.com, or leave it empty."];
        }

        if (Blank(draft.Address1) is not { Length: <= 200 })
        {
            errors["address1"] = ["Enter the address (up to 200 characters)."];
        }

        if (Blank(draft.Address2) is { Length: > 200 })
        {
            errors["address2"] = ["Keep the second address line to 200 characters."];
        }

        if (Blank(draft.SourceOfFunds) is { Length: > 200 })
        {
            errors["sourceOfFunds"] = ["Keep the source of funds to 200 characters."];
        }

        if (Blank(draft.OccupationOrSector) is { Length: > 100 })
        {
            errors["occupationOrSector"] = ["Keep the occupation to 100 characters."];
        }

        if (draft.IdentificationTypeId is not { } idTypeId
            || !await IsIndividualIdTypeAsync(idTypeId, cancellationToken).ConfigureAwait(false))
        {
            errors["identificationTypeId"] = ["Choose an identification type for a person."];
        }

        if (idNumber.Length is 0 or > 30)
        {
            errors["identificationNumber"] = ["Enter the identification number (up to 30 characters)."];
        }

        if (draft.IdentificationExpiresOn is { } expires)
        {
            if (expires < today)
            {
                errors["identificationExpiresOn"] = ["This document has expired. Use a valid document."];
            }
            else if (draft.IdentificationIssuedOn is { } issued && expires < issued)
            {
                errors["identificationExpiresOn"] = ["The expiry date must be after the issue date."];
            }
        }

        if (draft.IdentificationIssuedOn is { } issuedOn && issuedOn > today)
        {
            errors["identificationIssuedOn"] = ["The issue date cannot be in the future."];
        }

        return errors;
    }

    private async Task<bool> IsIndividualIdTypeAsync(int idTypeId, CancellationToken cancellationToken)
    {
        var code = await _db.Set<IdentificationDocumentType>()
            .AsNoTracking()
            .Where(row => row.Id == idTypeId && row.IsActive)
            .Select(row => row.Code)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return code is not null && LookupCodes.IdentificationDocumentTypes.Individual.Contains(code);
    }

    /// <summary>The party number of whoever already holds this document, on the detail row or as any document.</summary>
    private async Task<string?> ExistingHolderAsync(int idTypeId, string idNumber, CancellationToken cancellationToken)
    {
        var onDetail = await _db.Set<IndividualDetail>()
            .AsNoTracking()
            .Where(row => row.IdentificationTypeId == idTypeId && row.IdentificationNumber == idNumber)
            .Select(row => row.Party!.PartyNumber)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return onDetail ?? await _db.Set<PartyIdentificationDocument>()
            .AsNoTracking()
            .Where(row => row.DocumentTypeId == idTypeId && row.DocumentNumber == idNumber)
            .Select(row => row.Party!.PartyNumber)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<string?> OtherHolderAsync(int idTypeId, string idNumber, long partyId, CancellationToken cancellationToken)
    {
        var onDetail = await _db.Set<IndividualDetail>()
            .AsNoTracking()
            .Where(row => row.IdentificationTypeId == idTypeId && row.IdentificationNumber == idNumber && row.PartyId != partyId)
            .Select(row => row.Party!.PartyNumber)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return onDetail ?? await _db.Set<PartyIdentificationDocument>()
            .AsNoTracking()
            .Where(row => row.DocumentTypeId == idTypeId && row.DocumentNumber == idNumber && row.PartyId != partyId)
            .Select(row => row.Party!.PartyNumber)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Plan 5.9: <c>NEXT VALUE FOR PartyNumberSequence</c>, zero padded. Gaps are acceptable.</summary>
    private async Task<string> NextPartyNumberAsync(CancellationToken cancellationToken)
    {
        // Not composed (no Single/First): SQL Server does not allow NEXT VALUE FOR inside a derived table.
        var values = await _db.Database
            .SqlQueryRaw<long>(NextPartyNumberSql)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return PartyRules.FormatPartyNumber(values[0], _partyNumbers.Length);
    }

    private static OperationResult<CreatedParty> Duplicate(string existingPartyNumber) =>
        OperationResult.FailWith<CreatedParty>(
            ProblemCodes.PartyDuplicateIdentification,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["existingPartyNumber"] = existingPartyNumber });

    private static bool LooksLikeEmail(string value)
    {
        var at = value.IndexOf('@', StringComparison.Ordinal);
        return at > 0 && at == value.LastIndexOf('@') && value.IndexOf('.', at) > at + 1
               && !value.EndsWith('.') && !value.Any(char.IsWhiteSpace);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
