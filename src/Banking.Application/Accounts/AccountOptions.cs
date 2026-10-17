namespace Banking.Application.Accounts;

/// <summary>Configuration section <c>AccountNumber</c> (plan 5.9). Fixed before the first account is opened.</summary>
public sealed class AccountNumberOptions
{
    public const string SectionName = "AccountNumber";

    /// <summary>Smallest number of sequence digits a product prefix must leave room for.</summary>
    public const int MinimumSequenceDigits = 6;

    /// <summary>Total length: product prefix, zero-padded sequence and the optional check digit.</summary>
    public int Length { get; set; } = 13;

    /// <summary>Adds a Luhn check digit at the end.</summary>
    public bool UseCheckDigit { get; set; }

    /// <summary>The longest product prefix that still leaves <see cref="MinimumSequenceDigits"/> for the sequence.</summary>
    public int MaxPrefixLength => Math.Min(6, Length - MinimumSequenceDigits - (UseCheckDigit ? 1 : 0));
}

/// <summary>Configuration section <c>Account</c> (plan 6.9).</summary>
public sealed class AccountOptions
{
    public const string SectionName = "Account";

    /// <summary>Product types that can be opened at <c>POST /accounts</c>; loans and deposits have their own workflows.</summary>
    public IList<string> DirectOpeningProductTypes { get; } = new List<string>();
}
