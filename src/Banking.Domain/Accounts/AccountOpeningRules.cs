using Banking.Domain.Lookups;

namespace Banking.Domain.Accounts;

/// <summary>One proposed holder: the party, its type code and the holder role code.</summary>
public sealed record HolderSpec(long PartyId, string PartyTypeCode, string RoleCode, string DisplayName);

/// <summary>The result of the shape check: field errors, or the operating mode to store and who signs.</summary>
public sealed record AccountShape(
    IReadOnlyDictionary<string, string[]> Errors,
    string? OperatingModeCode,
    IReadOnlySet<long> Signers,
    string DefaultName)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Plan 6.9 rules 3 and 4 (shape by primary holder) and addendum 6.9a (who has signing rights). Pure: the caller
/// supplies party types, the requested mode and, for a group, its active members. Eligibility (supervision, KYC,
/// signatures) is checked by the caller against <see cref="AccountShape.Signers"/>.
/// </summary>
public static class AccountOpeningRules
{
    public const int MaxNameLength = 150;

    private static readonly string[] SigningModes = [LookupCodes.OperatingModes.EitherToSign, LookupCodes.OperatingModes.AllToSign];

    public static AccountShape Check(IReadOnlyList<HolderSpec> holders, string? requestedModeCode, IReadOnlySet<long> activeGroupMembers)
    {
        ArgumentNullException.ThrowIfNull(holders);
        ArgumentNullException.ThrowIfNull(activeGroupMembers);

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var empty = new HashSet<long>();

        var primaries = holders.Where(static holder => holder.RoleCode == LookupCodes.AccountHolderRoles.Primary).ToList();
        if (primaries.Count != 1)
        {
            errors["holders"] = ["Choose exactly one primary holder."];
            return new AccountShape(errors, null, empty, string.Empty);
        }

        if (holders.Select(static holder => holder.PartyId).Distinct().Count() != holders.Count)
        {
            errors["holders"] = ["A customer can appear only once on an account."];
            return new AccountShape(errors, null, empty, string.Empty);
        }

        var primary = primaries[0];
        var joints = holders.Where(static holder => holder.RoleCode == LookupCodes.AccountHolderRoles.Joint).ToList();
        var signatories = holders.Where(static holder => holder.RoleCode == LookupCodes.AccountHolderRoles.Signatory).ToList();
        var holderErrors = new List<string>();

        if (signatories.Any(static holder => holder.PartyTypeCode != LookupCodes.PartyTypes.Individual))
        {
            holderErrors.Add("Signatories must be individuals.");
        }

        string? mode = null;
        var signers = new HashSet<long>();
        var name = primary.DisplayName;

        switch (primary.PartyTypeCode)
        {
            case LookupCodes.PartyTypes.Individual when joints.Count == 0:
                // A single-holder individual account; optional signatories sign alongside the holder.
                if (requestedModeCode is not null && requestedModeCode != LookupCodes.OperatingModes.SingleMode)
                {
                    errors["operatingModeId"] = ["An account with one holder uses the single operating mode."];
                }

                mode = LookupCodes.OperatingModes.SingleMode;
                signers.Add(primary.PartyId);
                signers.UnionWith(signatories.Select(static holder => holder.PartyId));
                break;

            case LookupCodes.PartyTypes.Individual:
                if (joints.Any(static holder => holder.PartyTypeCode != LookupCodes.PartyTypes.Individual))
                {
                    holderErrors.Add("Joint holders must be individuals.");
                }

                mode = RequireSigningMode(requestedModeCode, errors);
                signers.Add(primary.PartyId);
                signers.UnionWith(joints.Select(static holder => holder.PartyId));
                signers.UnionWith(signatories.Select(static holder => holder.PartyId));
                name = string.Join(" & ", new[] { primary }.Concat(joints).Select(static holder => holder.DisplayName));
                break;

            case LookupCodes.PartyTypes.Business:
            case LookupCodes.PartyTypes.Group:
                var isGroup = primary.PartyTypeCode == LookupCodes.PartyTypes.Group;
                if (joints.Count > 0)
                {
                    holderErrors.Add("Only an individual's account can have joint holders.");
                }

                if (signatories.Count == 0)
                {
                    holderErrors.Add(isGroup ? "A group account needs at least one signatory." : "A business account needs at least one signatory.");
                }

                if (isGroup && signatories.Any(holder => !activeGroupMembers.Contains(holder.PartyId)))
                {
                    holderErrors.Add("Signatories of a group account must be active members of the group.");
                }

                mode = RequireSigningMode(requestedModeCode, errors);
                signers.UnionWith(signatories.Select(static holder => holder.PartyId));
                break;

            default:
                holderErrors.Add("The primary holder must be an individual, a business or a group.");
                break;
        }

        if (holderErrors.Count > 0)
        {
            errors["holders"] = [.. holderErrors];
        }

        return new AccountShape(
            errors,
            errors.Count == 0 ? mode : null,
            errors.Count == 0 ? signers : empty,
            name.Length > MaxNameLength ? name[..MaxNameLength] : name);
    }

    private static string? RequireSigningMode(string? requestedModeCode, Dictionary<string, string[]> errors)
    {
        if (requestedModeCode is not null && SigningModes.Contains(requestedModeCode, StringComparer.Ordinal))
        {
            return requestedModeCode;
        }

        errors["operatingModeId"] = ["Choose either to sign or all to sign."];
        return null;
    }
}
