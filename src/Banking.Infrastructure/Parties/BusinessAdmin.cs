using System.Globalization;
using Banking.Application.Abstractions;
using Banking.Application.Errors;
using Banking.Domain.Lookups;
using Banking.Domain.Parties;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Banking.Infrastructure.Parties;

public sealed record OwnerShare(long OwnerPartyId, decimal OwnershipPercentage);

public sealed record NewDocument(int? DocumentTypeId, string? DocumentNumber, DateOnly? IssuedOn, DateOnly? ExpiresOn);

public sealed record NewBusiness(
    string BusinessName,
    string? RegistrationNumber,
    string? TaxIdentificationNumber,
    DateOnly? IncorporationDate,
    int? BusinessCategoryId,
    string? Mobile,
    string? Phone,
    string? EmailAddress,
    string? Address1,
    string? Address2,
    string? SourceOfFunds,
    string? OccupationOrSector,
    bool IsPoliticallyExposed,
    NewDocument? Document,
    IReadOnlyList<OwnerShare> BeneficialOwners);

public sealed record BeneficialOwnerView(long OwnerPartyId, string PartyNumber, string DisplayName, decimal OwnershipPercentage);

/// <summary>A4-02: business customers and their beneficial owners (plan 6.3).</summary>
public sealed class BusinessAdmin
{
    private readonly BankingDbContext _db;
    private readonly IActivityContext _activity;
    private readonly TimeProvider _timeProvider;
    private readonly PartyCommon _common;

    public BusinessAdmin(BankingDbContext db, IActivityContext activity, TimeProvider timeProvider, PartyCommon common)
    {
        _db = db;
        _activity = activity;
        _timeProvider = timeProvider;
        _common = common;
    }

    public async Task<OperationResult<CreatedParty>> CreateAsync(NewBusiness draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var name = draft.BusinessName.Trim();
        var registration = PartyRules.NormalizeIdentificationNumber(draft.RegistrationNumber ?? string.Empty);
        var tin = PartyCommon.Blank(draft.TaxIdentificationNumber) is { } rawTin ? PartyRules.NormalizeIdentificationNumber(rawTin) : null;
        var mobile = PartyRules.NormalizePhone(draft.Mobile);
        var phone = PartyRules.NormalizePhone(draft.Phone);
        var documentNumber = draft.Document is null ? null : PartyRules.NormalizeIdentificationNumber(draft.Document.DocumentNumber ?? string.Empty);
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

        var errors = PartyCommon.NewErrors();
        if (name.Length is 0 or > 150)
        {
            errors["businessName"] = ["Enter the business name (up to 150 characters)."];
        }

        if (registration.Length is 0 or > 40)
        {
            errors["registrationNumber"] = ["Enter the registration number (up to 40 characters)."];
        }

        if (tin is { Length: > 30 })
        {
            errors["taxIdentificationNumber"] = ["Keep the TIN to 30 characters."];
        }

        if (draft.IncorporationDate is { } incorporated && incorporated > today)
        {
            errors["incorporationDate"] = ["The incorporation date cannot be in the future."];
        }

        if (draft.BusinessCategoryId is not { } categoryId
            || !await _db.Set<BusinessCategoryType>().AnyAsync(row => row.Id == categoryId && row.IsActive, cancellationToken).ConfigureAwait(false))
        {
            errors["businessCategoryId"] = ["Choose the legal form from the list."];
        }

        PartyCommon.ValidateContact(errors, mobile, phone, draft.EmailAddress, draft.Address1, draft.Address2, requireAddress: true, requireNumber: true);

        if (PartyCommon.Blank(draft.SourceOfFunds) is not { Length: <= 200 })
        {
            errors["sourceOfFunds"] = ["Enter the source of funds (up to 200 characters)."];
        }

        if (PartyCommon.Blank(draft.OccupationOrSector) is { Length: > 100 })
        {
            errors["occupationOrSector"] = ["Keep the sector to 100 characters."];
        }

        if (draft.Document is { } document)
        {
            if (document.DocumentTypeId is not { } typeId
                || !await _common.IsDocumentTypeForAsync(typeId, LookupCodes.IdentificationDocumentTypes.Business, cancellationToken).ConfigureAwait(false))
            {
                errors["document.documentTypeId"] = ["Choose a document type for a business."];
            }

            if (documentNumber is not { Length: > 0 and <= 40 })
            {
                errors["document.documentNumber"] = ["Enter the document number (up to 40 characters)."];
            }

            if (document.ExpiresOn is { } expires && (expires < today || (document.IssuedOn is { } issued && expires < issued)))
            {
                errors["document.expiresOn"] = ["The document has expired or its dates are the wrong way round."];
            }
        }

        var owners = await ValidateOwnersAsync(draft.BeneficialOwners, businessPartyId: null, errors, cancellationToken).ConfigureAwait(false);

        if (errors.Count > 0)
        {
            return OperationResult.Fail<CreatedParty>(ProblemCodes.Validation, errors);
        }

        if (await RegistrationHolderAsync(registration, cancellationToken).ConfigureAwait(false) is { } existing)
        {
            return PartyCommon.Conflict<CreatedParty>(ProblemCodes.PartyDuplicateRegistration, "existingPartyNumber", existing);
        }

        if (draft.Document is { DocumentTypeId: { } docTypeId }
            && await _common.DocumentHolderAsync(docTypeId, documentNumber!, cancellationToken).ConfigureAwait(false) is { } holder)
        {
            return PartyCommon.Conflict<CreatedParty>(ProblemCodes.PartyDuplicateIdentification, "existingPartyNumber", holder);
        }

        var party = await _common.NewPartyAsync(LookupCodes.PartyTypes.Business, name, cancellationToken).ConfigureAwait(false);
        party.Mobile = mobile;
        party.Phone = phone;
        party.EmailAddress = PartyCommon.Blank(draft.EmailAddress);
        party.Address1 = PartyCommon.Blank(draft.Address1);
        party.Address2 = PartyCommon.Blank(draft.Address2);
        party.SourceOfFunds = PartyCommon.Blank(draft.SourceOfFunds);
        party.OccupationOrSector = PartyCommon.Blank(draft.OccupationOrSector);
        party.IsPoliticallyExposed = draft.IsPoliticallyExposed;
        party.BusinessDetail = new BusinessDetail
        {
            BusinessName = name,
            RegistrationNumber = registration,
            TaxIdentificationNumber = tin,
            IncorporationDate = draft.IncorporationDate,
            BusinessCategoryId = draft.BusinessCategoryId
        };

        foreach (var owner in owners)
        {
            party.BusinessDetail.BeneficialOwners.Add(new PartyBeneficialOwner
            {
                OwnerPartyId = owner.OwnerPartyId,
                OwnershipPercentage = owner.OwnershipPercentage
            });
        }

        if (draft.Document is { DocumentTypeId: { } typeIdToSave })
        {
            party.IdentificationDocuments.Add(new PartyIdentificationDocument
            {
                DocumentTypeId = typeIdToSave,
                DocumentNumber = documentNumber!,
                IssuedOn = draft.Document.IssuedOn,
                ExpiresOn = draft.Document.ExpiresOn
            });
        }

        _db.Set<Party>().Add(party);
        _activity.Record(EventCodes.CreateRecord, nameof(Party), party.PartyNumber);

        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // A concurrent create with the same registration number won the unique index.
            _db.ChangeTracker.Clear();
            _activity.Written = false;
            if (await RegistrationHolderAsync(registration, cancellationToken).ConfigureAwait(false) is { } winner)
            {
                return PartyCommon.Conflict<CreatedParty>(ProblemCodes.PartyDuplicateRegistration, "existingPartyNumber", winner);
            }

            throw;
        }

        return OperationResult.Ok(new CreatedParty(party.PartyId, party.PartyNumber));
    }

    /// <summary>
    /// A4-06: edits a business. Name, registration number and TIN are controlled: changing one sends KYC back to
    /// PENDING and the record back to Pending approval. Owners and documents have their own endpoints.
    /// </summary>
    public async Task<OperationResult<long>> UpdateAsync(long partyId, NewBusiness draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var party = await _db.Set<Party>()
            .Include(row => row.BusinessDetail)
            .SingleOrDefaultAsync(row => row.PartyId == partyId && row.BusinessDetail != null, cancellationToken)
            .ConfigureAwait(false);
        if (party is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        var name = draft.BusinessName.Trim();
        var registration = PartyRules.NormalizeIdentificationNumber(draft.RegistrationNumber ?? string.Empty);
        var tin = PartyCommon.Blank(draft.TaxIdentificationNumber) is { } rawTin ? PartyRules.NormalizeIdentificationNumber(rawTin) : null;
        var mobile = PartyRules.NormalizePhone(draft.Mobile);
        var phone = PartyRules.NormalizePhone(draft.Phone);
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

        var errors = PartyCommon.NewErrors();
        if (name.Length is 0 or > 150)
        {
            errors["businessName"] = ["Enter the business name (up to 150 characters)."];
        }

        if (registration.Length is 0 or > 40)
        {
            errors["registrationNumber"] = ["Enter the registration number (up to 40 characters)."];
        }

        if (tin is { Length: > 30 })
        {
            errors["taxIdentificationNumber"] = ["Keep the TIN to 30 characters."];
        }

        if (draft.IncorporationDate is { } incorporated && incorporated > today)
        {
            errors["incorporationDate"] = ["The incorporation date cannot be in the future."];
        }

        if (draft.BusinessCategoryId is not { } categoryId
            || !await _db.Set<BusinessCategoryType>().AnyAsync(row => row.Id == categoryId && row.IsActive, cancellationToken).ConfigureAwait(false))
        {
            errors["businessCategoryId"] = ["Choose the legal form from the list."];
        }

        PartyCommon.ValidateContact(errors, mobile, phone, draft.EmailAddress, draft.Address1, draft.Address2, requireAddress: true, requireNumber: true);

        if (PartyCommon.Blank(draft.SourceOfFunds) is not { Length: <= 200 })
        {
            errors["sourceOfFunds"] = ["Enter the source of funds (up to 200 characters)."];
        }

        if (PartyCommon.Blank(draft.OccupationOrSector) is { Length: > 100 })
        {
            errors["occupationOrSector"] = ["Keep the sector to 100 characters."];
        }

        if (errors.Count > 0)
        {
            return OperationResult.Fail<long>(ProblemCodes.Validation, errors);
        }

        var detail = party.BusinessDetail!;
        if (registration != detail.RegistrationNumber
            && await _db.Set<BusinessDetail>().AsNoTracking()
                .Where(row => row.RegistrationNumber == registration && row.PartyId != partyId)
                .Select(row => row.Party!.PartyNumber)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false) is { } holder)
        {
            return PartyCommon.Conflict<long>(ProblemCodes.PartyDuplicateRegistration, "existingPartyNumber", holder);
        }

        var controlled = !string.Equals(detail.BusinessName, name, StringComparison.Ordinal)
            || registration != detail.RegistrationNumber
            || tin != detail.TaxIdentificationNumber;

        detail.BusinessName = name;
        detail.RegistrationNumber = registration;
        detail.TaxIdentificationNumber = tin;
        detail.IncorporationDate = draft.IncorporationDate;
        detail.BusinessCategoryId = draft.BusinessCategoryId;
        party.DisplayName = name;
        party.Mobile = mobile;
        party.Phone = phone;
        party.EmailAddress = PartyCommon.Blank(draft.EmailAddress);
        party.Address1 = PartyCommon.Blank(draft.Address1);
        party.Address2 = PartyCommon.Blank(draft.Address2);
        party.SourceOfFunds = PartyCommon.Blank(draft.SourceOfFunds);
        party.OccupationOrSector = PartyCommon.Blank(draft.OccupationOrSector);
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
            if (await RegistrationHolderAsync(registration, cancellationToken).ConfigureAwait(false) is { } winner)
            {
                return PartyCommon.Conflict<long>(ProblemCodes.PartyDuplicateRegistration, "existingPartyNumber", winner);
            }

            throw;
        }

        return OperationResult.Ok(partyId);
    }

    public async Task<IReadOnlyList<BeneficialOwnerView>?> OwnersAsync(long businessPartyId, CancellationToken cancellationToken)
    {
        if (!await _db.Set<BusinessDetail>().AnyAsync(row => row.PartyId == businessPartyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return await _db.Set<PartyBeneficialOwner>()
            .AsNoTracking()
            .Where(row => row.BusinessPartyId == businessPartyId)
            .OrderByDescending(row => row.OwnershipPercentage)
            .ThenBy(row => row.Owner!.DisplayName)
            .Select(row => new BeneficialOwnerView(row.OwnerPartyId, row.Owner!.PartyNumber, row.Owner.DisplayName, row.OwnershipPercentage))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Replaces the owner list. A material change: KYC goes back to PENDING (plan 6.5).</summary>
    public async Task<OperationResult<long>> ReplaceOwnersAsync(long businessPartyId, IReadOnlyList<OwnerShare> owners, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owners);

        var party = await _db.Set<Party>()
            .Include(row => row.BusinessDetail!)
            .ThenInclude(row => row.BeneficialOwners)
            .SingleOrDefaultAsync(row => row.PartyId == businessPartyId && row.BusinessDetail != null, cancellationToken)
            .ConfigureAwait(false);
        if (party is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        var errors = PartyCommon.NewErrors();
        var valid = await ValidateOwnersAsync(owners, businessPartyId, errors, cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return OperationResult.Fail<long>(ProblemCodes.Validation, errors);
        }

        var business = party.BusinessDetail!;
        var wanted = valid.ToDictionary(owner => owner.OwnerPartyId);
        _db.Set<PartyBeneficialOwner>().RemoveRange(business.BeneficialOwners.Where(row => !wanted.ContainsKey(row.OwnerPartyId)).ToList());
        foreach (var row in business.BeneficialOwners.Where(row => wanted.ContainsKey(row.OwnerPartyId)))
        {
            row.OwnershipPercentage = wanted[row.OwnerPartyId].OwnershipPercentage;
        }

        foreach (var owner in valid.Where(owner => business.BeneficialOwners.All(row => row.OwnerPartyId != owner.OwnerPartyId)).ToList())
        {
            business.BeneficialOwners.Add(new PartyBeneficialOwner
            {
                BusinessPartyId = businessPartyId,
                OwnerPartyId = owner.OwnerPartyId,
                OwnershipPercentage = owner.OwnershipPercentage
            });
        }

        await _common.MarkMaterialChangeAsync(party, cancellationToken).ConfigureAwait(false);
        _activity.Record(EventCodes.UpdateRecord, nameof(PartyBeneficialOwner), party.PartyNumber);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(businessPartyId);
    }

    /// <summary>
    /// Plan 6.3: each owner is an individual party, appears once, holds more than 0 and at most 100 percent (two
    /// decimals), and the total does not exceed 100.
    /// </summary>
    private async Task<List<OwnerShare>> ValidateOwnersAsync(
        IReadOnlyList<OwnerShare>? owners,
        long? businessPartyId,
        Dictionary<string, string[]> errors,
        CancellationToken cancellationToken)
    {
        var list = owners?.ToList() ?? [];
        if (list.Count == 0)
        {
            return list;
        }

        var problems = new List<string>();
        var references = await _common.ReferencesAsync(list.Select(owner => owner.OwnerPartyId), cancellationToken).ConfigureAwait(false);

        foreach (var owner in list)
        {
            var label = references.TryGetValue(owner.OwnerPartyId, out var reference)
                ? reference.PartyNumber
                : owner.OwnerPartyId.ToString(CultureInfo.InvariantCulture);

            if (reference is null || owner.OwnerPartyId == businessPartyId)
            {
                problems.Add($"Owner {label} does not exist.");
            }
            else if (reference.PartyTypeCode != LookupCodes.PartyTypes.Individual)
            {
                problems.Add($"Owner {label} is not an individual.");
            }

            if (owner.OwnershipPercentage is <= 0m or > 100m || decimal.Round(owner.OwnershipPercentage, 2) != owner.OwnershipPercentage)
            {
                problems.Add($"Owner {label}: enter a percentage above 0 and up to 100, with at most two decimals.");
            }
        }

        if (list.GroupBy(owner => owner.OwnerPartyId).Any(group => group.Count() > 1))
        {
            problems.Add("Each owner can appear only once.");
        }

        if (list.Sum(owner => owner.OwnershipPercentage) > 100m)
        {
            problems.Add("Ownership adds up to more than 100 percent.");
        }

        if (problems.Count > 0)
        {
            errors["beneficialOwners"] = [.. problems];
        }

        return list;
    }

    private Task<string?> RegistrationHolderAsync(string registration, CancellationToken cancellationToken) =>
        _db.Set<BusinessDetail>()
            .AsNoTracking()
            .Where(row => row.RegistrationNumber == registration)
            .Select(row => row.Party!.PartyNumber)
            .FirstOrDefaultAsync(cancellationToken);
}
