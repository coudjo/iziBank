namespace Banking.Application.Security;

/// <summary>Configuration section <c>Jwt</c>.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "iziBank";

    public string Audience { get; set; } = "iziBank";

    /// <summary>From the environment (<c>Jwt__SigningKey</c>), never from the repository. At least 32 characters.</summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Also the idle window.</summary>
    public int AccessTokenMinutes { get; set; } = 15;

    public int MaxSessionHours { get; set; } = 12;
}

/// <summary>Configuration section <c>Security</c>.</summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    public int PasswordMinLength { get; set; } = 8;

    public int LockoutThreshold { get; set; } = 5;

    public int LockoutWindowMinutes { get; set; } = 15;

    public int LockoutDurationMinutes { get; set; } = 15;

    /// <summary>How long a session or permission lookup may be served from memory.</summary>
    public int CacheSeconds { get; set; } = 30;
}
