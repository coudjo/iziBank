using System.Globalization;

namespace Banking.Domain.Accounts;

/// <summary>
/// Plan 5.9: <c>AccountNumber</c> = product prefix + zero-padded global sequence value (+ optional Luhn check digit),
/// <paramref name="length"/> characters in total. The sequence is global, so numbers are unique whatever the prefix.
/// </summary>
public static class AccountNumberFormat
{
    public static string Compose(string prefix, long sequenceValue, int length, bool useCheckDigit)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequenceValue);

        if (prefix.Length == 0 || !prefix.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("The prefix must be digits.", nameof(prefix));
        }

        var sequenceDigits = length - prefix.Length - (useCheckDigit ? 1 : 0);
        var sequence = sequenceValue.ToString(CultureInfo.InvariantCulture);
        if (sequenceDigits <= 0 || sequence.Length > sequenceDigits)
        {
            throw new InvalidOperationException("The account number sequence no longer fits the configured length.");
        }

        var body = prefix + sequence.PadLeft(sequenceDigits, '0');
        return useCheckDigit ? body + LuhnCheckDigit(body) : body;
    }

    /// <summary>The Luhn (mod 10) digit that makes <paramref name="digits"/> plus the digit pass the Luhn check.</summary>
    public static char LuhnCheckDigit(string digits)
    {
        ArgumentNullException.ThrowIfNull(digits);

        var sum = 0;
        var doubleIt = true;
        for (var index = digits.Length - 1; index >= 0; index--)
        {
            var value = digits[index] - '0';
            if (value is < 0 or > 9)
            {
                throw new ArgumentException("Only digits are allowed.", nameof(digits));
            }

            if (doubleIt)
            {
                value *= 2;
                if (value > 9)
                {
                    value -= 9;
                }
            }

            sum += value;
            doubleIt = !doubleIt;
        }

        return (char)('0' + ((10 - (sum % 10)) % 10));
    }

    /// <summary>True when the whole number, including its last digit, passes the Luhn check.</summary>
    public static bool IsLuhnValid(string number)
    {
        ArgumentNullException.ThrowIfNull(number);
        return number.Length > 1
               && number.All(char.IsAsciiDigit)
               && LuhnCheckDigit(number[..^1]) == number[^1];
    }
}
