using System.Globalization;
using Banking.Application.Abstractions;
using Banking.Application.Errors;
using Banking.Application.Supervision;
using Banking.Domain.Lookups;
using Banking.Domain.Parties;
using Banking.Domain.Supervision;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Banking.Infrastructure.Parties;

public sealed record SignatureVersion(long PartySignatureId, DateTime CapturedOn, long CapturedByUserId, long? SupervisedByUserId, DateTime? SupervisedOn);

/// <summary>The specimen signature status of a customer: the effective (approved) one and any capture awaiting approval.</summary>
public sealed record SignatureView(bool HasEffective, SignatureVersion? Effective, SignatureVersion? Pending);

/// <summary>
/// A4-09 and A4-11 (addendum 3): specimen signatures. The effective specimen is the most recently approved one;
/// a recapture waits for a different supervisor and does not replace it until approved. Signatures do not change
/// the customer's own supervision or KYC state.
/// </summary>
public sealed class PartySignatures
{
    public const int MaxBytes = 1024 * 1024;
    public const int MaxWidth = 2000;
    public const int MaxHeight = 1000;

    private static readonly HashSet<string> PngOnly = new(StringComparer.OrdinalIgnoreCase) { FileSignatures.Png };

    private readonly BankingDbContext _db;
    private readonly IActivityContext _activity;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly SupervisionOptions _supervision;

    public PartySignatures(
        BankingDbContext db,
        IActivityContext activity,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        IOptions<SupervisionOptions> supervision)
    {
        ArgumentNullException.ThrowIfNull(supervision);
        _db = db;
        _activity = activity;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
        _supervision = supervision.Value;
    }

    /// <summary>Returns the problem code for a signature upload, or null when it is a PNG within size and dimensions.</summary>
    public static string? Check(byte[]? content, string? declaredType)
    {
        if (UploadCheck.Validate(content, declaredType, PngOnly, MaxBytes, out _) is { } problem)
        {
            return problem;
        }

        return FileSignatures.PngSize(content) is { } size && size.Width <= MaxWidth && size.Height <= MaxHeight
            ? null
            : ProblemCodes.UploadDimensions;
    }

    public async Task<SignatureView?> GetAsync(long partyId, CancellationToken cancellationToken)
    {
        if (!await IsIndividualAsync(partyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        // Approved rows by approval time (newest first); SQL Server sorts the unapproved NULLs last.
        var versions = await Versions(partyId)
            .OrderByDescending(row => row.SupervisedOn)
            .ThenByDescending(row => row.CapturedOn)
            .Select(row => new
            {
                Approved = !row.IsSupervisionRequired || row.SupervisedByUserId != null,
                Version = new SignatureVersion(row.PartySignatureId, row.CapturedOn, row.CapturedByUserId, row.SupervisedByUserId, row.SupervisedOn)
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var effective = versions.FirstOrDefault(row => row.Approved)?.Version;
        var pending = versions.FirstOrDefault(row => !row.Approved)?.Version;
        return new SignatureView(effective is not null, effective, pending);
    }

    /// <summary>
    /// Captures a specimen. A capture already awaiting approval is replaced; an approved one stays effective until
    /// the new one is approved.
    /// </summary>
    public async Task<OperationResult<long>> CaptureAsync(long partyId, byte[]? content, string? declaredType, CancellationToken cancellationToken)
    {
        if (!await IsIndividualAsync(partyId, cancellationToken).ConfigureAwait(false))
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        if (Check(content, declaredType) is { } problem)
        {
            return OperationResult.Fail<long>(problem);
        }

        var staffUserId = _currentUser.StaffUserId ?? throw new InvalidOperationException("Capture needs a signed-in user.");
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var pending = await PendingRowAsync(partyId, cancellationToken).ConfigureAwait(false);
        if (pending is null)
        {
            pending = new PartySignature { PartyId = partyId };
            _db.Set<PartySignature>().Add(pending);
        }

        pending.ImageValue = content!;
        pending.CapturedOn = now;
        pending.CapturedByUserId = staffUserId;
        SupervisionRules.StartNew(pending, _supervision.Enabled);
        if (!_supervision.Enabled)
        {
            // With supervision off a capture is effective at once; SupervisedOn orders the versions.
            pending.SupervisedOn = now;
        }

        _activity.Record(EventCodes.CreateRecord, nameof(PartySignature), Key(partyId));
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(pending.PartySignatureId);
    }

    /// <summary>The effective specimen by default; <paramref name="pending"/> returns the capture awaiting approval.</summary>
    public async Task<StoredImage?> GetImageAsync(long partyId, bool pending, CancellationToken cancellationToken)
    {
        var view = await GetAsync(partyId, cancellationToken).ConfigureAwait(false);
        var version = pending ? view?.Pending : view?.Effective;
        if (version is null)
        {
            return null;
        }

        var bytes = await _db.Set<PartySignature>()
            .AsNoTracking()
            .Where(row => row.PartySignatureId == version.PartySignatureId)
            .Select(row => row.ImageValue)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        _activity.Record(EventCodes.ViewRecord, nameof(PartySignature), Key(partyId), new Dictionary<string, object?> { ["image"] = true });
        return new StoredImage(bytes, FileSignatures.Png);
    }

    /// <summary>R2: a different supervisor approves the capture; it becomes the effective specimen.</summary>
    public async Task<OperationResult<long>> SuperviseAsync(long partyId, CancellationToken cancellationToken)
    {
        var approverId = _currentUser.StaffUserId ?? throw new InvalidOperationException("Approval needs a signed-in user.");
        var pending = await PendingRowAsync(partyId, cancellationToken).ConfigureAwait(false);
        if (pending is null)
        {
            return OperationResult.Fail<long>(
                await IsIndividualAsync(partyId, cancellationToken).ConfigureAwait(false) ? SupervisionProblems.NotPending : ProblemCodes.NotFound);
        }

        if (pending.CapturedByUserId == approverId && !_supervision.AllowSelfApproval)
        {
            return OperationResult.Fail<long>(SupervisionProblems.SelfApproval);
        }

        var check = SupervisionRules.CheckApprove(pending, approverId, hasSupervisePermission: true, _supervision.AllowSelfApproval);
        if (!check.Allowed)
        {
            return OperationResult.Fail<long>(check.ProblemCode!);
        }

        SupervisionRules.Approve(pending, approverId, _timeProvider.GetUtcNow().UtcDateTime);
        _activity.Record(EventCodes.SuperviseApprove, nameof(PartySignature), Key(partyId));
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(pending.PartySignatureId);
    }

    /// <summary>R5: the capture awaiting approval is deleted; the effective specimen, if any, stays.</summary>
    public async Task<OperationResult<long>> RejectAsync(long partyId, string? reason, CancellationToken cancellationToken)
    {
        var pending = await PendingRowAsync(partyId, cancellationToken).ConfigureAwait(false);
        if (pending is null)
        {
            return OperationResult.Fail<long>(
                await IsIndividualAsync(partyId, cancellationToken).ConfigureAwait(false) ? SupervisionProblems.NotPending : ProblemCodes.NotFound);
        }

        _db.Set<PartySignature>().Remove(pending);
        _activity.Record(
            EventCodes.SuperviseReject,
            nameof(PartySignature),
            Key(partyId),
            string.IsNullOrWhiteSpace(reason) ? null : new Dictionary<string, object?> { ["reason"] = reason.Length > 300 ? reason[..300] : reason });
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(pending.PartySignatureId);
    }

    /// <summary>
    /// Addendum rule 3a: of these parties, the ones with an effective (approved) specimen. Account opening (A5) uses
    /// it to block a signatory who has only a pending signature.
    /// </summary>
    public async Task<IReadOnlySet<long>> WithEffectiveSignatureAsync(IEnumerable<long> partyIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(partyIds);
        var ids = partyIds.Distinct().ToList();
        var found = await _db.Set<PartySignature>()
            .AsNoTracking()
            .Where(row => ids.Contains(row.PartyId) && (!row.IsSupervisionRequired || row.SupervisedByUserId != null))
            .Select(row => row.PartyId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return found.ToHashSet();
    }

    private IQueryable<PartySignature> Versions(long partyId) =>
        _db.Set<PartySignature>().AsNoTracking().Where(row => row.PartyId == partyId);

    private Task<PartySignature?> PendingRowAsync(long partyId, CancellationToken cancellationToken) =>
        _db.Set<PartySignature>()
            .Where(row => row.PartyId == partyId && row.IsSupervisionRequired && row.SupervisedByUserId == null)
            .OrderByDescending(row => row.CapturedOn)
            .FirstOrDefaultAsync(cancellationToken);

    private Task<bool> IsIndividualAsync(long partyId, CancellationToken cancellationToken) =>
        _db.Set<Party>().AnyAsync(
            row => row.PartyId == partyId && row.PartyType!.Code == LookupCodes.PartyTypes.Individual,
            cancellationToken);

    private static string Key(long partyId) => partyId.ToString(CultureInfo.InvariantCulture);
}
