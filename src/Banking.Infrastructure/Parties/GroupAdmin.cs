using System.Globalization;
using Banking.Application.Abstractions;
using Banking.Application.Errors;
using Banking.Domain.Lookups;
using Banking.Domain.Parties;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Banking.Infrastructure.Parties;

public sealed record NewMember(long MemberPartyId, int? GroupMemberRoleId, DateOnly? JoinedOn);

public sealed record NewGroup(
    string GroupName,
    DateOnly? FormationDate,
    string? MeetingFrequency,
    string? Mobile,
    string? Phone,
    string? EmailAddress,
    string? Address1,
    string? Address2,
    IReadOnlyList<NewMember> Members);

public sealed record GroupMemberView(
    long GroupMemberId,
    long MemberPartyId,
    string PartyNumber,
    string DisplayName,
    int? GroupMemberRoleId,
    string? RoleCode,
    DateOnly? JoinedOn,
    DateOnly? LeftOn,
    bool IsChairperson);

/// <summary>
/// A4-03: groups, members and the chairperson (plan 6.4). Members are individuals, active in one group at a time
/// (D8, enforced by <c>UX_GroupMember_ActiveMember</c>). The chairperson is the one active member with role
/// CHAIRPERSON and is kept in <c>GroupDetail.ChairpersonPartyId</c>, so a group can never have two.
/// </summary>
public sealed class GroupAdmin
{
    private readonly BankingDbContext _db;
    private readonly IActivityContext _activity;
    private readonly TimeProvider _timeProvider;
    private readonly LookupIdCache _lookups;
    private readonly PartyCommon _common;

    public GroupAdmin(BankingDbContext db, IActivityContext activity, TimeProvider timeProvider, LookupIdCache lookups, PartyCommon common)
    {
        _db = db;
        _activity = activity;
        _timeProvider = timeProvider;
        _lookups = lookups;
        _common = common;
    }

    public async Task<OperationResult<CreatedParty>> CreateAsync(NewGroup draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var name = draft.GroupName.Trim();
        var mobile = PartyRules.NormalizePhone(draft.Mobile);
        var phone = PartyRules.NormalizePhone(draft.Phone);
        var today = Today();
        var errors = PartyCommon.NewErrors();

        if (name.Length is 0 or > 100)
        {
            errors["groupName"] = ["Enter the group name (up to 100 characters)."];
        }

        if (draft.FormationDate is { } formed && formed > today)
        {
            errors["formationDate"] = ["The formation date cannot be in the future."];
        }

        if (PartyCommon.Blank(draft.MeetingFrequency) is { Length: > 30 })
        {
            errors["meetingFrequency"] = ["Keep the meeting frequency to 30 characters."];
        }

        PartyCommon.ValidateContact(errors, mobile, phone, draft.EmailAddress, draft.Address1, draft.Address2, requireAddress: false, requireNumber: false);

        var members = draft.Members ?? [];
        var chairpersonId = await LookupIdAsync(LookupCodes.GroupMemberRoles.Chairperson, cancellationToken).ConfigureAwait(false);
        var problems = new List<string>();
        var references = await _common.ReferencesAsync(members.Select(member => member.MemberPartyId), cancellationToken).ConfigureAwait(false);
        var roles = await ActiveRoleIdsAsync(cancellationToken).ConfigureAwait(false);

        foreach (var member in members)
        {
            problems.AddRange(MemberProblems(member.MemberPartyId, member.GroupMemberRoleId, member.JoinedOn, references, roles, today));
        }

        if (members.GroupBy(member => member.MemberPartyId).Any(group => group.Count() > 1))
        {
            problems.Add("Each member can appear only once.");
        }

        if (members.Count(member => member.GroupMemberRoleId == chairpersonId) > 1)
        {
            problems.Add("A group has one chairperson.");
        }

        if (problems.Count > 0)
        {
            errors["members"] = [.. problems];
        }

        if (errors.Count > 0)
        {
            return OperationResult.Fail<CreatedParty>(ProblemCodes.Validation, errors);
        }

        if (await ActiveElsewhereAsync(members.Select(member => member.MemberPartyId), groupPartyId: null, cancellationToken).ConfigureAwait(false) is { } busy)
        {
            return InOtherGroup<CreatedParty>(busy);
        }

        var party = await _common.NewPartyAsync(LookupCodes.PartyTypes.Group, name, cancellationToken).ConfigureAwait(false);
        party.Mobile = mobile;
        party.Phone = phone;
        party.EmailAddress = PartyCommon.Blank(draft.EmailAddress);
        party.Address1 = PartyCommon.Blank(draft.Address1);
        party.Address2 = PartyCommon.Blank(draft.Address2);
        party.GroupDetail = new GroupDetail
        {
            GroupName = name,
            FormationDate = draft.FormationDate,
            MeetingFrequency = PartyCommon.Blank(draft.MeetingFrequency),
            ChairpersonPartyId = members.FirstOrDefault(member => member.GroupMemberRoleId == chairpersonId)?.MemberPartyId
        };
        _db.Set<Party>().Add(party);

        foreach (var member in members)
        {
            _db.Set<GroupMember>().Add(new GroupMember
            {
                GroupParty = party,
                MemberPartyId = member.MemberPartyId,
                GroupMemberRoleId = member.GroupMemberRoleId,
                JoinedOn = member.JoinedOn ?? today
            });
        }

        _activity.Record(EventCodes.CreateRecord, nameof(Party), party.PartyNumber);
        var saved = await SaveMembershipAsync<CreatedParty>(members.Select(member => member.MemberPartyId), null, cancellationToken).ConfigureAwait(false);
        return saved ?? OperationResult.Ok(new CreatedParty(party.PartyId, party.PartyNumber));
    }

    /// <summary>A4-06: edits a group. The name is controlled (KYC back to PENDING); members have their own endpoints.</summary>
    public async Task<OperationResult<long>> UpdateAsync(long groupPartyId, NewGroup draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var group = await LoadGroupAsync(groupPartyId, cancellationToken).ConfigureAwait(false);
        if (group is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        var name = draft.GroupName.Trim();
        var mobile = PartyRules.NormalizePhone(draft.Mobile);
        var phone = PartyRules.NormalizePhone(draft.Phone);
        var errors = PartyCommon.NewErrors();
        if (name.Length is 0 or > 100)
        {
            errors["groupName"] = ["Enter the group name (up to 100 characters)."];
        }

        if (draft.FormationDate is { } formed && formed > Today())
        {
            errors["formationDate"] = ["The formation date cannot be in the future."];
        }

        if (PartyCommon.Blank(draft.MeetingFrequency) is { Length: > 30 })
        {
            errors["meetingFrequency"] = ["Keep the meeting frequency to 30 characters."];
        }

        PartyCommon.ValidateContact(errors, mobile, phone, draft.EmailAddress, draft.Address1, draft.Address2, requireAddress: false, requireNumber: false);
        if (errors.Count > 0)
        {
            return OperationResult.Fail<long>(ProblemCodes.Validation, errors);
        }

        var detail = group.GroupDetail!;
        var controlled = !string.Equals(detail.GroupName, name, StringComparison.Ordinal);
        detail.GroupName = name;
        detail.FormationDate = draft.FormationDate;
        detail.MeetingFrequency = PartyCommon.Blank(draft.MeetingFrequency);
        group.DisplayName = name;
        group.Mobile = mobile;
        group.Phone = phone;
        group.EmailAddress = PartyCommon.Blank(draft.EmailAddress);
        group.Address1 = PartyCommon.Blank(draft.Address1);
        group.Address2 = PartyCommon.Blank(draft.Address2);

        if (controlled)
        {
            await _common.MarkMaterialChangeAsync(group, cancellationToken).ConfigureAwait(false);
        }

        _activity.Record(EventCodes.UpdateRecord, nameof(Party), group.PartyNumber, new Dictionary<string, object?> { ["controlled"] = controlled });
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(groupPartyId);
    }

    public async Task<IReadOnlyList<GroupMemberView>?> MembersAsync(long groupPartyId, bool includeFormer, CancellationToken cancellationToken)
    {
        var chairperson = await _db.Set<GroupDetail>()
            .AsNoTracking()
            .Where(row => row.PartyId == groupPartyId)
            .Select(row => new { row.ChairpersonPartyId })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (chairperson is null)
        {
            return null;
        }

        return await _db.Set<GroupMember>()
            .AsNoTracking()
            .Where(row => row.GroupPartyId == groupPartyId && (includeFormer || row.LeftOn == null))
            .OrderBy(row => row.LeftOn != null)
            .ThenBy(row => row.MemberParty!.DisplayName)
            .Select(row => new GroupMemberView(
                row.GroupMemberId,
                row.MemberPartyId,
                row.MemberParty!.PartyNumber,
                row.MemberParty.DisplayName,
                row.GroupMemberRoleId,
                row.GroupMemberRoleId == null ? null : row.Role!.Code,
                row.JoinedOn,
                row.LeftOn,
                row.LeftOn == null && row.MemberPartyId == chairperson.ChairpersonPartyId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OperationResult<long>> AddMemberAsync(long groupPartyId, NewMember member, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(member);

        var group = await LoadGroupAsync(groupPartyId, cancellationToken).ConfigureAwait(false);
        if (group is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        var references = await _common.ReferencesAsync([member.MemberPartyId], cancellationToken).ConfigureAwait(false);
        var problems = MemberProblems(member.MemberPartyId, member.GroupMemberRoleId, member.JoinedOn, references, await ActiveRoleIdsAsync(cancellationToken).ConfigureAwait(false), Today());
        if (problems.Count > 0)
        {
            return OperationResult.Fail<long>(ProblemCodes.Validation, new Dictionary<string, string[]>(StringComparer.Ordinal) { ["memberPartyId"] = [.. problems] });
        }

        if (await ActiveElsewhereAsync([member.MemberPartyId], groupPartyId: null, cancellationToken).ConfigureAwait(false) is { } busy)
        {
            return InOtherGroup<long>(busy);
        }

        var chairpersonId = await LookupIdAsync(LookupCodes.GroupMemberRoles.Chairperson, cancellationToken).ConfigureAwait(false);
        if (member.GroupMemberRoleId == chairpersonId)
        {
            if (group.GroupDetail!.ChairpersonPartyId is not null)
            {
                return OperationResult.Fail<long>(ProblemCodes.GroupChairpersonExists);
            }

            group.GroupDetail.ChairpersonPartyId = member.MemberPartyId;
        }

        var row = new GroupMember
        {
            GroupPartyId = groupPartyId,
            MemberPartyId = member.MemberPartyId,
            GroupMemberRoleId = member.GroupMemberRoleId,
            JoinedOn = member.JoinedOn ?? Today()
        };
        _db.Set<GroupMember>().Add(row);
        await _common.MarkMaterialChangeAsync(group, cancellationToken).ConfigureAwait(false);
        _activity.Record(EventCodes.UpdateRecord, nameof(GroupMember), group.PartyNumber, Detail("add", references[member.MemberPartyId].PartyNumber));

        var failed = await SaveMembershipAsync<long>([member.MemberPartyId], groupPartyId, cancellationToken).ConfigureAwait(false);
        return failed ?? OperationResult.Ok(row.GroupMemberId);
    }

    /// <summary>The member leaves: <c>LeftOn</c> is set and the row stays as history. A leaving chairperson leaves the post empty.</summary>
    public async Task<OperationResult<long>> LeaveAsync(long groupPartyId, long memberPartyId, DateOnly? leftOn, CancellationToken cancellationToken)
    {
        var group = await LoadGroupAsync(groupPartyId, cancellationToken).ConfigureAwait(false);
        var row = group is null ? null : await ActiveRowAsync(groupPartyId, memberPartyId, cancellationToken).ConfigureAwait(false);
        if (group is null || row is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        var date = leftOn ?? Today();
        if (date > Today() || (row.JoinedOn is { } joined && date < joined))
        {
            return OperationResult.Fail<long>(
                ProblemCodes.Validation,
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["leftOn"] = ["The leaving date must be between the joining date and today."] });
        }

        row.LeftOn = date;
        if (group.GroupDetail!.ChairpersonPartyId == memberPartyId)
        {
            group.GroupDetail.ChairpersonPartyId = null;
        }

        await _common.MarkMaterialChangeAsync(group, cancellationToken).ConfigureAwait(false);
        _activity.Record(EventCodes.UpdateRecord, nameof(GroupMember), group.PartyNumber, Detail("leave", memberPartyId.ToString(CultureInfo.InvariantCulture)));
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(row.GroupMemberId);
    }

    /// <summary>Changes an active member's role. Promoting to chairperson needs the post to be empty.</summary>
    public async Task<OperationResult<long>> ChangeRoleAsync(long groupPartyId, long memberPartyId, int? roleId, CancellationToken cancellationToken)
    {
        var group = await LoadGroupAsync(groupPartyId, cancellationToken).ConfigureAwait(false);
        var row = group is null ? null : await ActiveRowAsync(groupPartyId, memberPartyId, cancellationToken).ConfigureAwait(false);
        if (group is null || row is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        if (roleId is not { } newRole || !(await ActiveRoleIdsAsync(cancellationToken).ConfigureAwait(false)).Contains(newRole))
        {
            return OperationResult.Fail<long>(
                ProblemCodes.Validation,
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["groupMemberRoleId"] = ["Choose a role from the list."] });
        }

        var chairpersonId = await LookupIdAsync(LookupCodes.GroupMemberRoles.Chairperson, cancellationToken).ConfigureAwait(false);
        var detail = group.GroupDetail!;
        if (newRole == chairpersonId && detail.ChairpersonPartyId is { } current && current != memberPartyId)
        {
            return OperationResult.Fail<long>(ProblemCodes.GroupChairpersonExists);
        }

        row.GroupMemberRoleId = newRole;
        if (newRole == chairpersonId)
        {
            detail.ChairpersonPartyId = memberPartyId;
        }
        else if (detail.ChairpersonPartyId == memberPartyId)
        {
            detail.ChairpersonPartyId = null;
        }

        await _common.MarkMaterialChangeAsync(group, cancellationToken).ConfigureAwait(false);
        _activity.Record(EventCodes.UpdateRecord, nameof(GroupMember), group.PartyNumber, Detail("role", memberPartyId.ToString(CultureInfo.InvariantCulture)));
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(row.GroupMemberId);
    }

    private static List<string> MemberProblems(
        long memberPartyId,
        int? roleId,
        DateOnly? joinedOn,
        Dictionary<long, PartyReference> references,
        HashSet<int> roles,
        DateOnly today)
    {
        var problems = new List<string>();
        if (!references.TryGetValue(memberPartyId, out var reference))
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"Member {memberPartyId} does not exist."));
        }
        else if (reference.PartyTypeCode != LookupCodes.PartyTypes.Individual)
        {
            problems.Add($"Member {reference.PartyNumber} is not an individual.");
        }

        if (roleId is not { } role || !roles.Contains(role))
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"Member {reference?.PartyNumber ?? memberPartyId.ToString(CultureInfo.InvariantCulture)}: choose a role from the list."));
        }

        if (joinedOn is { } joined && joined > today)
        {
            problems.Add("The joining date cannot be in the future.");
        }

        return problems;
    }

    /// <summary>The first of these people who is an active member of another group, with that group's number.</summary>
    private async Task<(string MemberPartyNumber, string GroupPartyNumber)?> ActiveElsewhereAsync(
        IEnumerable<long> memberPartyIds,
        long? groupPartyId,
        CancellationToken cancellationToken)
    {
        var ids = memberPartyIds.ToList();
        var busy = await _db.Set<GroupMember>()
            .AsNoTracking()
            .Where(row => ids.Contains(row.MemberPartyId) && row.LeftOn == null && row.GroupPartyId != groupPartyId)
            .Select(row => new { Member = row.MemberParty!.PartyNumber, Group = row.GroupParty!.PartyNumber })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return busy is null ? null : (busy.Member, busy.Group);
    }

    /// <summary>Saves; if the active-member index caught a concurrent join, reports which group won.</summary>
    private async Task<OperationResult<T>?> SaveMembershipAsync<T>(IEnumerable<long> memberPartyIds, long? groupPartyId, CancellationToken cancellationToken)
    {
        var ids = memberPartyIds.ToList();
        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            _activity.Written = false;
            if (await ActiveElsewhereAsync(ids, groupPartyId, cancellationToken).ConfigureAwait(false) is { } busy)
            {
                return InOtherGroup<T>(busy);
            }

            throw;
        }
    }

    private static OperationResult<T> InOtherGroup<T>((string MemberPartyNumber, string GroupPartyNumber) busy) =>
        OperationResult.FailWith<T>(
            ProblemCodes.GroupMemberInOtherGroup,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["memberPartyNumber"] = busy.MemberPartyNumber,
                ["groupPartyNumber"] = busy.GroupPartyNumber
            });

    private Task<Party?> LoadGroupAsync(long groupPartyId, CancellationToken cancellationToken) =>
        _db.Set<Party>()
            .Include(row => row.GroupDetail)
            .SingleOrDefaultAsync(row => row.PartyId == groupPartyId && row.GroupDetail != null, cancellationToken);

    private Task<GroupMember?> ActiveRowAsync(long groupPartyId, long memberPartyId, CancellationToken cancellationToken) =>
        _db.Set<GroupMember>().SingleOrDefaultAsync(
            row => row.GroupPartyId == groupPartyId && row.MemberPartyId == memberPartyId && row.LeftOn == null,
            cancellationToken);

    private async Task<HashSet<int>> ActiveRoleIdsAsync(CancellationToken cancellationToken) =>
        (await _db.Set<GroupMemberRoleType>()
            .AsNoTracking()
            .Where(row => row.IsActive)
            .Select(row => row.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false)).ToHashSet();

    private Task<int> LookupIdAsync(string roleCode, CancellationToken cancellationToken) =>
        _lookups.GetIdAsync<GroupMemberRoleType>(_db, roleCode, cancellationToken);

    private DateOnly Today() => DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

    private static Dictionary<string, object?> Detail(string action, string member) =>
        new(StringComparer.Ordinal) { ["action"] = action, ["member"] = member };
}
