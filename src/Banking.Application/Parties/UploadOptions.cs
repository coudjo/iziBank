namespace Banking.Application.Parties;

/// <summary>Configuration section <c>Uploads</c> (plan 6.6, addendum 4).</summary>
public sealed class UploadOptions
{
    public const string SectionName = "Uploads";

    private static readonly string[] DefaultContentTypes = ["image/jpeg", "image/png", "application/pdf"];

    public int MaxImageBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>Null means the default list. No default here: the binder appends to an existing array.</summary>
#pragma warning disable CA1819 // Bound from configuration as an array.
    public string[]? AllowedContentTypes { get; set; }
#pragma warning restore CA1819

    public IReadOnlySet<string> AllowedTypes() =>
        new HashSet<string>(AllowedContentTypes ?? DefaultContentTypes, StringComparer.OrdinalIgnoreCase);
}
