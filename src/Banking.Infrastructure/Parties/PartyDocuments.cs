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

public sealed record DocumentSummary(
    long PartyIdentificationDocumentId,
    int DocumentTypeId,
    string DocumentTypeCode,
    string DocumentNumberMasked,
    DateOnly? IssuedOn,
    DateOnly? ExpiresOn,
    bool IsExpired,
    bool HasImage,
    bool IsPrimary);

public sealed record DocumentDetail(
    long PartyIdentificationDocumentId,
    int DocumentTypeId,
    string DocumentTypeCode,
    string DocumentNumber,
    DateOnly? IssuedOn,
    DateOnly? ExpiresOn,
    bool IsExpired,
    bool HasImage,
    bool IsPrimary);

public sealed record DocumentDraft(int? DocumentTypeId, string? DocumentNumber, DateOnly? IssuedOn, DateOnly? ExpiresOn);

public sealed record StoredImage(byte[] Content, string ContentType);

public sealed record PhotoView(bool HasPhoto, string? SupervisionState, DateTime? CapturedOn, long? CapturedByUserId, long? SupervisedByUserId);

/// <summary>Plan 6.6: upload checks shared by photos, documents and signatures.</summary>
public static class UploadCheck
{
    /// <summary>Returns the problem code, or null with <paramref name="detected"/> set when the file is acceptable.</summary>
    public static string? Validate(byte[]? content, string? declaredType, IReadOnlySet<string> allowed, int maxBytes, out string? detected)
    {
        ArgumentNullException.ThrowIfNull(allowed);
        detected = null;

        if (content is null || content.Length > maxBytes)
        {
            return ProblemCodes.UploadTooLarge;
        }

        if (content.Length == 0)
        {
            return ProblemCodes.UploadEmpty;
        }

        var type = FileSignatures.Detect(content);
        if (type is null || !allowed.Contains(type))
        {
            return ProblemCodes.UploadTypeNotAllowed;
        }

        var declared = declaredType?.Split(';')[0].Trim();
        if (!string.IsNullOrEmpty(declared) && !string.Equals(declared, type, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(declared, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            return ProblemCodes.UploadTypeMismatch;
        }

        detected = type;
        return null;
    }
}

/// <summary>
/// A4-04: identification documents and their images, and the customer photo (plan 6.6). Numbers are masked in
/// lists; images are only served by their own endpoints. Every change is material: KYC back to PENDING (6.5).
/// </summary>
public sealed class PartyDocuments
{
    private static readonly HashSet<string> PhotoTypes = new(StringComparer.OrdinalIgnoreCase) { FileSignatures.Jpeg, FileSignatures.Png };

    private readonly BankingDbContext _db;
    private readonly IActivityContext _activity;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly PartyCommon _common;
    private readonly SupervisionOptions _supervision;
    private readonly UploadOptions _uploads;

    public PartyDocuments(
        BankingDbContext db,
        IActivityContext activity,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        PartyCommon common,
        IOptions<SupervisionOptions> supervision,
        IOptions<UploadOptions> uploads)
    {
        ArgumentNullException.ThrowIfNull(supervision);
        ArgumentNullException.ThrowIfNull(uploads);
        _db = db;
        _activity = activity;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
        _common = common;
        _supervision = supervision.Value;
        _uploads = uploads.Value;
    }

    public int MaxImageBytes => _uploads.MaxImageBytes;

    // ---------------------------------------------------------------- Documents

    public async Task<IReadOnlyList<DocumentSummary>?> ListAsync(long partyId, CancellationToken cancellationToken)
    {
        var rows = await DocumentRowsAsync(partyId, documentId: null, cancellationToken).ConfigureAwait(false);
        if (rows is null)
        {
            return null;
        }

        _activity.Record(EventCodes.ViewRecord, nameof(PartyIdentificationDocument), PartyKey(partyId));
        return rows
            .Select(row => new DocumentSummary(
                row.PartyIdentificationDocumentId,
                row.DocumentTypeId,
                row.DocumentTypeCode,
                PartyRules.MaskIdentificationNumber(row.DocumentNumber),
                row.IssuedOn,
                row.ExpiresOn,
                row.IsExpired,
                row.HasImage,
                row.IsPrimary))
            .ToList();
    }

    public async Task<DocumentDetail?> GetAsync(long partyId, long documentId, CancellationToken cancellationToken)
    {
        var row = (await DocumentRowsAsync(partyId, documentId, cancellationToken).ConfigureAwait(false))?.SingleOrDefault();
        if (row is not null)
        {
            _activity.Record(EventCodes.ViewRecord, nameof(PartyIdentificationDocument), Key(documentId));
        }

        return row;
    }

    public async Task<OperationResult<long>> AddAsync(long partyId, DocumentDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var party = await LoadPartyAsync(partyId, cancellationToken).ConfigureAwait(false);
        if (party is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        var number = PartyRules.NormalizeIdentificationNumber(draft.DocumentNumber ?? string.Empty);
        var errors = await ValidateAsync(party.PartyType!.Code, draft, number, cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return OperationResult.Fail<long>(ProblemCodes.Validation, errors);
        }

        if (await _common.DocumentHolderAsync(draft.DocumentTypeId!.Value, number, cancellationToken).ConfigureAwait(false) is { } holder)
        {
            return PartyCommon.Conflict<long>(ProblemCodes.PartyDuplicateIdentification, "existingPartyNumber", holder);
        }

        var document = new PartyIdentificationDocument
        {
            PartyId = partyId,
            DocumentTypeId = draft.DocumentTypeId.Value,
            DocumentNumber = number,
            IssuedOn = draft.IssuedOn,
            ExpiresOn = draft.ExpiresOn
        };
        _db.Set<PartyIdentificationDocument>().Add(document);
        await _common.MarkMaterialChangeAsync(party, cancellationToken).ConfigureAwait(false);
        _activity.Record(EventCodes.CreateRecord, nameof(PartyIdentificationDocument), party.PartyNumber);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(document.PartyIdentificationDocumentId);
    }

    /// <summary>Changes the type, number or dates. The individual's primary ID on their details follows the document.</summary>
    public async Task<OperationResult<long>> UpdateAsync(long partyId, long documentId, DocumentDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var party = await LoadPartyAsync(partyId, cancellationToken).ConfigureAwait(false);
        var document = party is null ? null : await LoadDocumentAsync(partyId, documentId, cancellationToken).ConfigureAwait(false);
        if (party is null || document is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        var number = PartyRules.NormalizeIdentificationNumber(draft.DocumentNumber ?? string.Empty);
        var errors = await ValidateAsync(party.PartyType!.Code, draft, number, cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return OperationResult.Fail<long>(ProblemCodes.Validation, errors);
        }

        var typeId = draft.DocumentTypeId!.Value;
        if ((typeId != document.DocumentTypeId || number != document.DocumentNumber)
            && await OtherHolderAsync(typeId, number, partyId, cancellationToken).ConfigureAwait(false) is { } holder)
        {
            return PartyCommon.Conflict<long>(ProblemCodes.PartyDuplicateIdentification, "existingPartyNumber", holder);
        }

        if (IsPrimary(party, document))
        {
            party.IndividualDetail!.IdentificationTypeId = typeId;
            party.IndividualDetail.IdentificationNumber = number;
        }

        document.DocumentTypeId = typeId;
        document.DocumentNumber = number;
        document.IssuedOn = draft.IssuedOn;
        document.ExpiresOn = draft.ExpiresOn;
        await _common.MarkMaterialChangeAsync(party, cancellationToken).ConfigureAwait(false);
        _activity.Record(EventCodes.UpdateRecord, nameof(PartyIdentificationDocument), Key(documentId));
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(documentId);
    }

    public async Task<OperationResult<long>> DeleteAsync(long partyId, long documentId, CancellationToken cancellationToken)
    {
        var party = await LoadPartyAsync(partyId, cancellationToken).ConfigureAwait(false);
        var document = party is null ? null : await LoadDocumentAsync(partyId, documentId, cancellationToken).ConfigureAwait(false);
        if (party is null || document is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        if (IsPrimary(party, document))
        {
            return OperationResult.Fail<long>(ProblemCodes.PartyPrimaryDocument);
        }

        _db.Set<PartyIdentificationDocument>().Remove(document);
        await _common.MarkMaterialChangeAsync(party, cancellationToken).ConfigureAwait(false);
        _activity.Record(EventCodes.DeleteRecord, nameof(PartyIdentificationDocument), Key(documentId));
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(documentId);
    }

    public async Task<OperationResult<long>> SetImageAsync(
        long partyId,
        long documentId,
        byte[]? content,
        string? declaredType,
        CancellationToken cancellationToken)
    {
        var party = await LoadPartyAsync(partyId, cancellationToken).ConfigureAwait(false);
        var document = party is null ? null : await LoadDocumentAsync(partyId, documentId, cancellationToken).ConfigureAwait(false);
        if (party is null || document is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        if (UploadCheck.Validate(content, declaredType, _uploads.AllowedTypes(), _uploads.MaxImageBytes, out _) is { } problem)
        {
            return OperationResult.Fail<long>(problem);
        }

        document.ImageValue = content;
        await _common.MarkMaterialChangeAsync(party, cancellationToken).ConfigureAwait(false);
        _activity.Record(EventCodes.UpdateRecord, nameof(PartyIdentificationDocument), Key(documentId), new Dictionary<string, object?> { ["image"] = true });
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(documentId);
    }

    public async Task<StoredImage?> GetImageAsync(long partyId, long documentId, CancellationToken cancellationToken)
    {
        var bytes = await _db.Set<PartyIdentificationDocument>()
            .AsNoTracking()
            .Where(row => row.PartyId == partyId && row.PartyIdentificationDocumentId == documentId)
            .Select(row => row.ImageValue)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (bytes is null || bytes.Length == 0)
        {
            return null;
        }

        _activity.Record(EventCodes.ViewRecord, nameof(PartyIdentificationDocument), Key(documentId), new Dictionary<string, object?> { ["image"] = true });
        return new StoredImage(bytes, FileSignatures.Detect(bytes) ?? "application/octet-stream");
    }

    // ---------------------------------------------------------------- Photo

    public async Task<PhotoView?> GetPhotoAsync(long partyId, CancellationToken cancellationToken)
    {
        if (!await _db.Set<Party>().AnyAsync(row => row.PartyId == partyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var photo = await _db.Set<PartyImage>()
            .AsNoTracking()
            .Where(row => row.PartyId == partyId)
            .Select(row => new PhotoView(
                row.ImageValue != null,
                !row.IsSupervisionRequired ? PartySupervisionStates.NotRequired
                    : row.SupervisedByUserId == null ? PartySupervisionStates.Pending : PartySupervisionStates.Approved,
                row.ModifiedOn ?? row.CreatedOn,
                row.ModifiedByUserId ?? row.CreatedByUserId,
                row.SupervisedByUserId))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return photo ?? new PhotoView(false, null, null, null, null);
    }

    /// <summary>A new or replaced photo is Pending until a different supervisor approves it (plan 5.4, R4).</summary>
    public async Task<OperationResult<long>> SetPhotoAsync(long partyId, byte[]? content, string? declaredType, CancellationToken cancellationToken)
    {
        var party = await LoadPartyAsync(partyId, cancellationToken).ConfigureAwait(false);
        if (party is null || party.PartyType!.Code != LookupCodes.PartyTypes.Individual)
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        var allowed = new HashSet<string>(PhotoTypes.Where(type => _uploads.AllowedTypes().Contains(type)), StringComparer.OrdinalIgnoreCase);
        if (UploadCheck.Validate(content, declaredType, allowed, _uploads.MaxImageBytes, out _) is { } problem)
        {
            return OperationResult.Fail<long>(problem);
        }

        var photo = await _db.Set<PartyImage>().SingleOrDefaultAsync(row => row.PartyId == partyId, cancellationToken).ConfigureAwait(false);
        if (photo is null)
        {
            photo = new PartyImage { PartyId = partyId };
            _db.Set<PartyImage>().Add(photo);
        }

        photo.ImageValue = content;
        SupervisionRules.StartNew(photo, _supervision.Enabled);
        await _common.MarkMaterialChangeAsync(party, cancellationToken).ConfigureAwait(false);
        _activity.Record(EventCodes.UpdateRecord, nameof(PartyImage), party.PartyNumber);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(partyId);
    }

    public async Task<StoredImage?> GetPhotoImageAsync(long partyId, CancellationToken cancellationToken)
    {
        var bytes = await _db.Set<PartyImage>()
            .AsNoTracking()
            .Where(row => row.PartyId == partyId)
            .Select(row => row.ImageValue)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (bytes is null || bytes.Length == 0)
        {
            return null;
        }

        _activity.Record(EventCodes.ViewRecord, nameof(PartyImage), PartyKey(partyId), new Dictionary<string, object?> { ["image"] = true });
        return new StoredImage(bytes, FileSignatures.Detect(bytes) ?? "application/octet-stream");
    }

    /// <summary>R2: a different supervisor approves the pending photo.</summary>
    public async Task<OperationResult<long>> SupervisePhotoAsync(long partyId, CancellationToken cancellationToken)
    {
        var approverId = _currentUser.StaffUserId ?? throw new InvalidOperationException("Approval needs a signed-in user.");
        var photo = await _db.Set<PartyImage>().SingleOrDefaultAsync(row => row.PartyId == partyId, cancellationToken).ConfigureAwait(false);
        if (photo is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        var check = SupervisionRules.CheckApprove(photo, approverId, hasSupervisePermission: true, _supervision.AllowSelfApproval);
        if (!check.Allowed)
        {
            return OperationResult.Fail<long>(check.ProblemCode!);
        }

        SupervisionRules.Approve(photo, approverId, _timeProvider.GetUtcNow().UtcDateTime);
        _activity.Record(EventCodes.SuperviseApprove, nameof(PartyImage), PartyKey(partyId));
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(partyId);
    }

    /// <summary>R5 for photos: a pending photo is removed so a better one can be taken.</summary>
    public async Task<OperationResult<long>> RejectPhotoAsync(long partyId, string? reason, CancellationToken cancellationToken)
    {
        var photo = await _db.Set<PartyImage>().SingleOrDefaultAsync(row => row.PartyId == partyId, cancellationToken).ConfigureAwait(false);
        if (photo is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        if (SupervisionRules.StateOf(photo) != SupervisionState.Pending)
        {
            return OperationResult.Fail<long>(SupervisionProblems.NotPending);
        }

        _db.Set<PartyImage>().Remove(photo);
        _activity.Record(
            EventCodes.SuperviseReject,
            nameof(PartyImage),
            PartyKey(partyId),
            string.IsNullOrWhiteSpace(reason) ? null : new Dictionary<string, object?> { ["reason"] = reason.Length > 300 ? reason[..300] : reason });
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(partyId);
    }

    // ---------------------------------------------------------------- Helpers

    private async Task<Dictionary<string, string[]>> ValidateAsync(string partyTypeCode, DocumentDraft draft, string number, CancellationToken cancellationToken)
    {
        var errors = PartyCommon.NewErrors();
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var allowedTypes = partyTypeCode == LookupCodes.PartyTypes.Business
            ? LookupCodes.IdentificationDocumentTypes.Business
            : LookupCodes.IdentificationDocumentTypes.Individual;

        if (partyTypeCode == LookupCodes.PartyTypes.Group)
        {
            errors["documentTypeId"] = ["Groups do not hold identification documents; add them to the members."];
            return errors;
        }

        if (draft.DocumentTypeId is not { } typeId
            || !await _common.IsDocumentTypeForAsync(typeId, allowedTypes, cancellationToken).ConfigureAwait(false))
        {
            errors["documentTypeId"] = ["Choose a document type that applies to this customer."];
        }

        if (number.Length is 0 or > 30)
        {
            errors["documentNumber"] = ["Enter the document number (up to 30 characters)."];
        }

        if (draft.IssuedOn is { } issued && issued > today)
        {
            errors["issuedOn"] = ["The issue date cannot be in the future."];
        }

        if (draft.ExpiresOn is { } expires && draft.IssuedOn is { } issuedOn && expires < issuedOn)
        {
            errors["expiresOn"] = ["The expiry date must be after the issue date."];
        }

        return errors;
    }

    private async Task<List<DocumentDetail>?> DocumentRowsAsync(long partyId, long? documentId, CancellationToken cancellationToken)
    {
        var party = await _db.Set<Party>()
            .AsNoTracking()
            .Where(row => row.PartyId == partyId)
            .Select(row => new
            {
                PrimaryTypeId = row.IndividualDetail == null ? null : row.IndividualDetail.IdentificationTypeId,
                PrimaryNumber = row.IndividualDetail == null ? null : row.IndividualDetail.IdentificationNumber
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (party is null)
        {
            return null;
        }

        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var rows = await _db.Set<PartyIdentificationDocument>()
            .AsNoTracking()
            .Where(row => row.PartyId == partyId && (documentId == null || row.PartyIdentificationDocumentId == documentId))
            .OrderBy(row => row.PartyIdentificationDocumentId)
            .Select(row => new
            {
                row.PartyIdentificationDocumentId,
                row.DocumentTypeId,
                TypeCode = row.DocumentType!.Code,
                row.DocumentNumber,
                row.IssuedOn,
                row.ExpiresOn,
                HasImage = row.ImageValue != null
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(row => new DocumentDetail(
                row.PartyIdentificationDocumentId,
                row.DocumentTypeId,
                row.TypeCode,
                row.DocumentNumber,
                row.IssuedOn,
                row.ExpiresOn,
                row.ExpiresOn is { } expires && expires < today,
                row.HasImage,
                row.DocumentTypeId == party.PrimaryTypeId && row.DocumentNumber == party.PrimaryNumber))
            .ToList();
    }

    private Task<Party?> LoadPartyAsync(long partyId, CancellationToken cancellationToken) =>
        _db.Set<Party>()
            .Include(row => row.PartyType)
            .Include(row => row.IndividualDetail)
            .SingleOrDefaultAsync(row => row.PartyId == partyId, cancellationToken);

    private Task<PartyIdentificationDocument?> LoadDocumentAsync(long partyId, long documentId, CancellationToken cancellationToken) =>
        _db.Set<PartyIdentificationDocument>()
            .SingleOrDefaultAsync(row => row.PartyId == partyId && row.PartyIdentificationDocumentId == documentId, cancellationToken);

    private async Task<string?> OtherHolderAsync(int typeId, string number, long partyId, CancellationToken cancellationToken)
    {
        var onDetail = await _db.Set<IndividualDetail>()
            .AsNoTracking()
            .Where(row => row.IdentificationTypeId == typeId && row.IdentificationNumber == number && row.PartyId != partyId)
            .Select(row => row.Party!.PartyNumber)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return onDetail ?? await _db.Set<PartyIdentificationDocument>()
            .AsNoTracking()
            .Where(row => row.DocumentTypeId == typeId && row.DocumentNumber == number && row.PartyId != partyId)
            .Select(row => row.Party!.PartyNumber)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool IsPrimary(Party party, PartyIdentificationDocument document) =>
        party.IndividualDetail is { } individual
        && individual.IdentificationTypeId == document.DocumentTypeId
        && individual.IdentificationNumber == document.DocumentNumber;

    private static string Key(long id) => id.ToString(CultureInfo.InvariantCulture);

    private static string PartyKey(long partyId) => partyId.ToString(CultureInfo.InvariantCulture);
}
