using System.Globalization;
using System.Text;
using Banking.Application.Security;
using Banking.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Banking.Api.Security;

internal sealed record IssuedToken(string AccessToken, DateTime ExpiresAtUtc, DateTime SessionMaxExpiresAtUtc);

/// <summary>
/// Access tokens live for <c>Jwt:AccessTokenMinutes</c> (the idle window) and never beyond the session maximum.
/// </summary>
internal sealed class TokenIssuer
{
    private readonly JwtOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly JsonWebTokenHandler _handler = new();
    private readonly SigningCredentials _credentials;

    public TokenIssuer(IOptions<JwtOptions> options, TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
        _credentials = new SigningCredentials(KeyFrom(_options.SigningKey), SecurityAlgorithms.HmacSha256);
    }

    public static SymmetricSecurityKey KeyFrom(string signingKey) => new(Encoding.UTF8.GetBytes(signingKey));

    public IssuedToken Issue(SessionInfo session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var sessionMax = session.LoginAtUtc.AddHours(_options.MaxSessionHours);
        var idleExpiry = now.AddMinutes(_options.AccessTokenMinutes);
        var expires = idleExpiry < sessionMax ? idleExpiry : sessionMax;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            SigningCredentials = _credentials,
            Claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [ClaimNames.Subject] = session.StaffUserId.ToString(CultureInfo.InvariantCulture),
                [ClaimNames.SessionId] = session.SessionId.ToString(CultureInfo.InvariantCulture),
                [ClaimNames.Branch] = session.BranchId.ToString(CultureInfo.InvariantCulture),
                [ClaimNames.MustChangeSecretKey] = session.MustChangeSecretKey ? "true" : "false"
            }
        };

        return new IssuedToken(_handler.CreateToken(descriptor), expires, sessionMax);
    }
}
