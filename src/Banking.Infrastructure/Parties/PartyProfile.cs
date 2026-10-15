using System.Globalization;
using Banking.Application.Abstractions;
using Banking.Application.Errors;
using Banking.Domain.Lookups;
using Banking.Domain.Parties;
using Banking.Domain.Security;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Banking.Infrastructure.Parties;

public sealed record NextOfKinView(long PartyNextOfKinId, string FullName, string? Relationship, string? MobileNumber);

public sealed record NextOfKinDraft(string FullName, string? Relationship, string? MobileNumber);

public sealed record RegulatoryAttributeView(string AttributeCode, string? AttributeValue, DateTime RecordedOn, long? RecordedByUserId);

public sealed record CreditOfficerView(
    long CreditOfficerId,
    long StaffUserId,
    string StaffCode,
    string FullName,
    int BranchId,
    string BranchCode,
    bool IsDisabled);

/// <summary>
/// A4-05 (plan 6.6, 6.7): next of kin (free edit), regulatory attributes (append-only history; the current value
/// is the newest version per code) and credit officers.
/// </summary>
public sealed class PartyProfile
{
    private readonly BankingDbContext _db;
    private readonly IActivityContext _activity;
    private readonly TimeProvider _timeProvider;

    public PartyProfile(BankingDbContext db, IActivityContext activity, TimeProvider timeProvider)
    {
        _db = db;
        _activity = activity;
        _timeProvider = timeProvider;
    }

    // ---------------------------------------------------------------- Next of kin

    public async Task<IReadOnlyList<NextOfKinView>?> NextOfKinAsync(long partyId, CancellationToken cancellationToken)
    {
        if (!await PartyExistsAsync(partyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return await _db.Set<PartyNextOfKin>()
            .AsNoTracking()
            .Where(row => row.PartyId == partyId)
            .OrderBy(row => row.PartyNextOfKinId)
            .Select(row => new NextOfKinView(row.PartyNextOfKinId, row.FullName, row.Relationship, row.MobileNumber))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OperationResult<long>> AddNextOfKinAsync(long partyId, NextOfKinDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var party = await _db.Set<Party>().AsNoTracking()
            .Where(row => row.PartyId == partyId)
            .Select(row => new { row.PartyNumber, TypeCode = row.PartyType!.Code })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (party is null || party.TypeCode != LookupCodes.PartyTypes.Individual)
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        if (ValidateNextOfKin(draft, out var mobile) is { Count: > 0 } errors)
        {
            return OperationResult.Fail<long>(ProblemCodes.Validation, errors);
        }

        var row = new PartyNextOfKin
        {
            PartyId = partyId,
            FullName = draft.FullName.Trim(),
            Relationship = PartyCommon.Blank(draft.Relationship),
            MobileNumber = mobile
        };
        _db.Set<PartyNextOfKin>().Add(row);
        _activity.Record(EventCodes.CreateRecord, nameof(PartyNextOfKin), party.PartyNumber);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(row.PartyNextOfKinId);
    }

    public async Task<OperationResult<long>> UpdateNextOfKinAsync(long partyId, long nextOfKinId, NextOfKinDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var row = await _db.Set<PartyNextOfKin>()
            .SingleOrDefaultAsync(item => item.PartyId == partyId && item.PartyNextOfKinId == nextOfKinId, cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        if (ValidateNextOfKin(draft, out var mobile) is { Count: > 0 } errors)
        {
            return OperationResult.Fail<long>(ProblemCodes.Validation, errors);
        }

        row.FullName = draft.FullName.Trim();
        row.Relationship = PartyCommon.Blank(draft.Relationship);
        row.MobileNumber = mobile;
        _activity.Record(EventCodes.UpdateRecord, nameof(PartyNextOfKin), Key(nextOfKinId));
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(nextOfKinId);
    }

    public async Task<OperationResult<long>> DeleteNextOfKinAsync(long partyId, long nextOfKinId, CancellationToken cancellationToken)
    {
        var row = await _db.Set<PartyNextOfKin>()
            .SingleOrDefaultAsync(item => item.PartyId == partyId && item.PartyNextOfKinId == nextOfKinId, cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        _db.Set<PartyNextOfKin>().Remove(row);
        _activity.Record(EventCodes.DeleteRecord, nameof(PartyNextOfKin), Key(nextOfKinId));
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(nextOfKinId);
    }

    // ---------------------------------------------------------------- Regulatory attributes

    /// <summary>The current value of each attribute: the newest version per code.</summary>
    public async Task<IReadOnlyList<RegulatoryAttributeView>?> CurrentAttributesAsync(long partyId, CancellationToken cancellationToken)
    {
        if (!await PartyExistsAsync(partyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var all = await AttributeRows(partyId, code: null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return all
            .GroupBy(row => row.AttributeCode, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(row => row.AttributeCode, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<IReadOnlyList<RegulatoryAttributeView>?> AttributeHistoryAsync(long partyId, string code, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (!await PartyExistsAsync(partyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var normalized = code.Trim().ToUpperInvariant();
        return await AttributeRows(partyId, normalized)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Appends a version. Setting the value it already has records nothing.</summary>
    public async Task<OperationResult<bool>> SetAttributeAsync(long partyId, string code, string? value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);

        var partyNumber = await _db.Set<Party>().AsNoTracking()
            .Where(row => row.PartyId == partyId)
            .Select(row => row.PartyNumber)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (partyNumber is null)
        {
            return OperationResult.Fail<bool>(ProblemCodes.NotFound);
        }

        var normalized = code.Trim().ToUpperInvariant();
        var text = PartyCommon.Blank(value);
        var errors = PartyCommon.NewErrors();
        if (!PartyRules.IsAttributeCode(normalized))
        {
            errors["attributeCode"] = ["Use upper case letters, digits and underscores, starting with a letter (up to 50)."];
        }

        if (text is { Length: > 500 })
        {
            errors["attributeValue"] = ["Keep the value to 500 characters."];
        }

        if (errors.Count > 0)
        {
            return OperationResult.Fail<bool>(ProblemCodes.Validation, errors);
        }

        var history = (await AttributeHistoryAsync(partyId, normalized, cancellationToken).ConfigureAwait(false))!;
        var current = history.Count > 0 ? history[0] : null;
        if (current is not null && string.Equals(current.AttributeValue, text, StringComparison.Ordinal))
        {
            return OperationResult.Ok(false);
        }

        _db.Set<PartyRegulatoryAttribute>().Add(new PartyRegulatoryAttribute
        {
            PartyId = partyId,
            AttributeCode = normalized,
            AttributeValue = text,
            RecordedOn = _timeProvider.GetUtcNow().UtcDateTime
        });
        _activity.Record(EventCodes.UpdateRecord, nameof(PartyRegulatoryAttribute), partyNumber, new Dictionary<string, object?> { ["code"] = normalized });
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(true);
    }

    // ---------------------------------------------------------------- Credit officers

    public async Task<IReadOnlyList<CreditOfficerView>> CreditOfficersAsync(int? branchId, bool includeDisabled, CancellationToken cancellationToken) =>
        await _db.Set<CreditOfficer>()
            .AsNoTracking()
            .Where(row => (includeDisabled || !row.IsDisabled) && (branchId == null || row.BranchId == branchId))
            .OrderBy(row => row.StaffUser!.FullName)
            .Select(row => new CreditOfficerView(
                row.CreditOfficerId,
                row.StaffUserId,
                row.StaffUser!.StaffCode,
                row.StaffUser.FullName,
                row.BranchId,
                row.Branch!.BranchCode,
                row.IsDisabled))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>Plan 6.7: designates a staff user who is not already an active credit officer; the branch is theirs.</summary>
    public async Task<OperationResult<long>> DesignateAsync(long staffUserId, CancellationToken cancellationToken)
    {
        var staff = await _db.Set<StaffUser>().AsNoTracking()
            .Where(row => row.StaffUserId == staffUserId && row.StaffCode != StaffCodes.System)
            .Select(row => new { row.BranchId, row.IsDisabled, row.StaffCode })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (staff is null || staff.IsDisabled)
        {
            return OperationResult.Fail<long>(
                ProblemCodes.Validation,
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["staffUserId"] = ["Choose an active staff member."] });
        }

        if (await _db.Set<CreditOfficer>().AnyAsync(row => row.StaffUserId == staffUserId && !row.IsDisabled, cancellationToken).ConfigureAwait(false))
        {
            return OperationResult.Fail<long>(ProblemCodes.CreditOfficerExists);
        }

        var officer = new CreditOfficer { StaffUserId = staffUserId, BranchId = staff.BranchId, IsDisabled = false };
        _db.Set<CreditOfficer>().Add(officer);
        _activity.Record(EventCodes.CreateRecord, nameof(CreditOfficer), staff.StaffCode);
        return await SaveOfficerAsync(officer, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Disables or re-enables a designation. Rows are never deleted; parties keep their history.</summary>
    public async Task<OperationResult<long>> SetOfficerDisabledAsync(long creditOfficerId, bool disabled, CancellationToken cancellationToken)
    {
        var officer = await _db.Set<CreditOfficer>().SingleOrDefaultAsync(row => row.CreditOfficerId == creditOfficerId, cancellationToken).ConfigureAwait(false);
        if (officer is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        if (officer.IsDisabled == disabled)
        {
            return OperationResult.Ok(creditOfficerId);
        }

        if (!disabled && await _db.Set<CreditOfficer>()
                .AnyAsync(row => row.StaffUserId == officer.StaffUserId && !row.IsDisabled && row.CreditOfficerId != creditOfficerId, cancellationToken)
                .ConfigureAwait(false))
        {
            return OperationResult.Fail<long>(ProblemCodes.CreditOfficerExists);
        }

        officer.IsDisabled = disabled;
        _activity.Record(EventCodes.UpdateRecord, nameof(CreditOfficer), Key(creditOfficerId));
        return await SaveOfficerAsync(officer, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Assigns an active credit officer to a party, or clears the assignment with null.</summary>
    public async Task<OperationResult<long>> AssignAsync(long partyId, long? creditOfficerId, CancellationToken cancellationToken)
    {
        var party = await _db.Set<Party>().SingleOrDefaultAsync(row => row.PartyId == partyId, cancellationToken).ConfigureAwait(false);
        if (party is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        if (creditOfficerId is { } officerId
            && !await _db.Set<CreditOfficer>().AnyAsync(row => row.CreditOfficerId == officerId && !row.IsDisabled, cancellationToken).ConfigureAwait(false))
        {
            return OperationResult.Fail<long>(
                ProblemCodes.Validation,
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["creditOfficerId"] = ["Choose an active credit officer."] });
        }

        party.CreditOfficerId = creditOfficerId;
        _activity.Record(EventCodes.UpdateRecord, nameof(Party), party.PartyNumber, new Dictionary<string, object?> { ["creditOfficerId"] = creditOfficerId });
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(partyId);
    }

    // ---------------------------------------------------------------- Helpers

    /// <summary>Filters on the table columns first: EF cannot translate a filter on the projected record.</summary>
    private IQueryable<RegulatoryAttributeView> AttributeRows(long partyId, string? code) =>
        _db.Set<PartyRegulatoryAttribute>()
            .AsNoTracking()
            .Where(row => row.PartyId == partyId && (code == null || row.AttributeCode == code))
            .OrderBy(row => row.AttributeCode)
            .ThenByDescending(row => row.RecordedOn)
            .ThenByDescending(row => row.PartyRegulatoryAttributeId)
            .Select(row => new RegulatoryAttributeView(row.AttributeCode, row.AttributeValue, row.RecordedOn, row.CreatedByUserId));

    private async Task<OperationResult<long>> SaveOfficerAsync(CreditOfficer officer, CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok(officer.CreditOfficerId);
        }
        catch (DbUpdateException)
        {
            // The filtered unique index caught a concurrent designation of the same person.
            _db.ChangeTracker.Clear();
            _activity.Written = false;
            return OperationResult.Fail<long>(ProblemCodes.CreditOfficerExists);
        }
    }

    private static Dictionary<string, string[]> ValidateNextOfKin(NextOfKinDraft draft, out string? mobile)
    {
        var errors = PartyCommon.NewErrors();
        var name = draft.FullName.Trim();
        mobile = PartyRules.NormalizePhone(draft.MobileNumber);

        if (name.Length is 0 or > 100)
        {
            errors["fullName"] = ["Enter the full name (up to 100 characters)."];
        }

        if (PartyCommon.Blank(draft.Relationship) is { Length: > 40 })
        {
            errors["relationship"] = ["Keep the relationship to 40 characters."];
        }

        if (mobile is { Length: 0 })
        {
            errors["mobileNumber"] = ["Enter a mobile number of 9 to 15 digits, or leave it empty."];
        }

        return errors;
    }

    private Task<bool> PartyExistsAsync(long partyId, CancellationToken cancellationToken) =>
        _db.Set<Party>().AnyAsync(row => row.PartyId == partyId, cancellationToken);

    private static string Key(long id) => id.ToString(CultureInfo.InvariantCulture);
}
