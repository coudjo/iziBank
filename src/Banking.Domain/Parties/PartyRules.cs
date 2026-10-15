using System.Globalization;
using System.Text;

namespace Banking.Domain.Parties;

/// <summary>Plan 5.9 and 6.1 to 6.6: party numbers, display names, identification numbers. Pure functions.</summary>
public static class PartyRules
{
    /// <summary>Zero pads the sequence value to <paramref name="length"/> digits.</summary>
    public static string FormatPartyNumber(long sequenceValue, int length)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sequenceValue, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);

        var digits = sequenceValue.ToString(CultureInfo.InvariantCulture);
        if (digits.Length > length)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Party number {digits} no longer fits in {length} digits. Increase PartyNumber:Length."));
        }

        return digits.PadLeft(length, '0');
    }

    /// <summary>Individual: "FirstName LastName" with single spaces (plan 6.1).</summary>
    public static string IndividualDisplayName(string firstName, string lastName) =>
        CollapseSpaces($"{firstName} {lastName}");

    /// <summary>Upper case, no spaces: the form used for storage and the duplicate check.</summary>
    public static string NormalizeIdentificationNumber(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (!char.IsWhiteSpace(character))
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }

        return builder.ToString();
    }

    /// <summary>List responses show only the last 4 characters (plan 6.6).</summary>
    public static string MaskIdentificationNumber(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= 4 ? new string('*', value.Length) : string.Concat("****", value.AsSpan(value.Length - 4));
    }

    /// <summary>
    /// Whole years between <paramref name="dateOfBirth"/> and <paramref name="onDate"/>. Someone born on 29 February
    /// has their birthday on 1 March in other years (DateOnly.AddYears would move it to 28 February).
    /// </summary>
    public static int AgeOn(DateOnly dateOfBirth, DateOnly onDate)
    {
        var age = onDate.Year - dateOfBirth.Year;
        var birthdayThisYear = dateOfBirth is { Month: 2, Day: 29 } && !DateTime.IsLeapYear(onDate.Year)
            ? new DateOnly(onDate.Year, 3, 1)
            : new DateOnly(onDate.Year, dateOfBirth.Month, dateOfBirth.Day);
        if (onDate < birthdayThisYear)
        {
            age--;
        }

        return age;
    }

    /// <summary>
    /// Removes spaces and dashes. Valid: an optional leading + and 9 to 15 digits. Returns null for a blank value
    /// and an empty string for an invalid one.
    /// </summary>
    public static string? NormalizePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var compact = new StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            if (character is not (' ' or '-'))
            {
                compact.Append(character);
            }
        }

        var text = compact.ToString();
        var digits = text.StartsWith('+') ? text[1..] : text;
        return digits.Length is >= 9 and <= 15 && digits.All(char.IsAsciiDigit) ? text : string.Empty;
    }

    private static string CollapseSpaces(string value) =>
        string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
