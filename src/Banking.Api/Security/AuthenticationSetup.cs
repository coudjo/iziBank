using System.Globalization;
using Banking.Api.Problems;
using Banking.Application.Errors;
using Banking.Application.Security;
using Banking.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Banking.Api.Security;

internal static class AuthenticationSetup
{
    public const string FailureItem = "ib.auth-failure";

    public static IServiceCollection AddBankingJwt(this IServiceCollection services, JwtOptions jwt)
    {
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = TokenIssuer.KeyFrom(jwt.SigningKey),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = ClaimNames.Subject
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = OnTokenValidatedAsync,
                    OnChallenge = OnChallengeAsync
                };
            });

        services.AddAuthorization();
        return services;
    }

    /// <summary>Plan 5.5: every request checks that the session is still open and within its maximum length.</summary>
    private static async Task OnTokenValidatedAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        if (!long.TryParse(principal?.FindFirst(ClaimNames.Subject)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var staffUserId)
            || !long.TryParse(principal?.FindFirst(ClaimNames.SessionId)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sessionId))
        {
            context.HttpContext.Items[FailureItem] = ProblemCodes.Unauthenticated;
            context.Fail(ProblemCodes.Unauthenticated);
            return;
        }

        var sessions = context.HttpContext.RequestServices.GetRequiredService<SessionStore>();
        var check = await sessions.CheckAsync(sessionId, staffUserId, context.HttpContext.RequestAborted).ConfigureAwait(false);
        if (!check.IsOpen)
        {
            context.HttpContext.Items[FailureItem] = check.ProblemCode;
            context.Fail(check.ProblemCode ?? ProblemCodes.SessionEnded);
        }
    }

    private static Task OnChallengeAsync(JwtBearerChallengeContext context)
    {
        context.HandleResponse();

        var code = context.HttpContext.Items[FailureItem] as string
            ?? (context.AuthenticateFailure is SecurityTokenExpiredException
                ? ProblemCodes.SessionTimedOut
                : ProblemCodes.Unauthenticated);

        return ApiProblems.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized, code);
    }
}
