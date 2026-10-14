using Banking.Api.Problems;
using Banking.Api.Security;
using Banking.Application.Abstractions;
using Banking.Application.Errors;
using Banking.Domain.Security;
using Banking.Infrastructure.Security;

namespace Banking.Api.Endpoints;

internal sealed record LoginRequest(string? StaffCode, string? SecretKey, int? BranchId);

internal sealed record ChangeSecretKeyRequest(string? CurrentSecretKey, string? NewSecretKey);

internal sealed record TokenResponse(
    string AccessToken,
    DateTime ExpiresAt,
    DateTime SessionMaxExpiresAt,
    long SessionId,
    int BranchId,
    bool MustChangeSecretKey);

internal sealed record MeResponse(
    long StaffUserId,
    string StaffCode,
    string FullName,
    int HomeBranchId,
    int BranchId,
    bool CanSwitchBranch,
    bool MustChangeSecretKey,
    IReadOnlyList<BranchSummary> Branches,
    IReadOnlyList<PermissionGrant> Permissions);

/// <summary>Plan 5.5 and frontend plan section 11: login, refresh, logout, change secret key, current user.</summary>
internal static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Auth");

        auth.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .Produces<TokenResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesValidationProblem();

        auth.MapPost("/refresh", RefreshAsync)
            .AuthenticatedOnly()
            .AllowedWhileMustChange()
            .Produces<TokenResponse>();

        auth.MapPost("/logout", LogoutAsync)
            .AuthenticatedOnly()
            .AllowedWhileMustChange()
            .Produces(StatusCodes.Status204NoContent);

        auth.MapPost("/change-secret-key", ChangeSecretKeyAsync)
            .AuthenticatedOnly()
            .AllowedWhileMustChange()
            .Produces<TokenResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesValidationProblem();

        auth.MapGet("/me", MeAsync)
            .AuthenticatedOnly()
            .AllowedWhileMustChange()
            .Produces<MeResponse>();

        return api;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        AuthService auth,
        TokenIssuer tokens,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(request.StaffCode))
        {
            errors["staffCode"] = ["Enter your staff code."];
        }

        if (string.IsNullOrEmpty(request.SecretKey))
        {
            errors["secretKey"] = ["Enter your password."];
        }

        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var result = await auth.LoginAsync(
                request.StaffCode!,
                request.SecretKey!,
                request.BranchId,
                http.Connection.RemoteIpAddress?.ToString(),
                http.Request.Headers.UserAgent.ToString(),
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return result.ProblemCode switch
            {
                ProblemCodes.Locked => ApiProblems.Result(
                    StatusCodes.Status401Unauthorized,
                    ProblemCodes.Locked,
                    new Dictionary<string, object?> { ["retryAfterSeconds"] = result.RetryAfterSeconds }),
                ProblemCodes.BranchNotAllowed => ApiProblems.Result(StatusCodes.Status403Forbidden, ProblemCodes.BranchNotAllowed),
                _ => ApiProblems.Result(StatusCodes.Status401Unauthorized, ProblemCodes.InvalidCredentials)
            };
        }

        return TypedResults.Ok(ToResponse(tokens, result.Session!));
    }

    private static async Task<IResult> RefreshAsync(
        ICurrentUser user,
        AuthService auth,
        TokenIssuer tokens,
        CancellationToken cancellationToken)
    {
        var session = await auth.RefreshAsync(user.SessionId!.Value, user.StaffUserId!.Value, user.BranchId!.Value, cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(ToResponse(tokens, session));
    }

    private static async Task<IResult> LogoutAsync(ICurrentUser user, AuthService auth, CancellationToken cancellationToken)
    {
        await auth.LogoutAsync(user.SessionId!.Value, cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ChangeSecretKeyAsync(
        ChangeSecretKeyRequest request,
        ICurrentUser user,
        AuthService auth,
        TokenIssuer tokens,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.CurrentSecretKey) || string.IsNullOrEmpty(request.NewSecretKey))
        {
            return ApiProblems.Validation(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["currentSecretKey"] = string.IsNullOrEmpty(request.CurrentSecretKey) ? ["Enter your current password."] : [],
                ["newSecretKey"] = string.IsNullOrEmpty(request.NewSecretKey) ? ["Enter a new password."] : []
            }.Where(static pair => pair.Value.Length > 0).ToDictionary(static pair => pair.Key, static pair => pair.Value));
        }

        var result = await auth.ChangeSecretKeyAsync(
                user.SessionId!.Value,
                user.StaffUserId!.Value,
                user.BranchId!.Value,
                request.CurrentSecretKey,
                request.NewSecretKey,
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return result.ProblemCode == ProblemCodes.SecretKeyRules
                ? ApiProblems.Result(
                    StatusCodes.Status400BadRequest,
                    ProblemCodes.SecretKeyRules,
                    new Dictionary<string, object?> { ["rules"] = result.RuleFailures })
                : ApiProblems.Result(StatusCodes.Status400BadRequest, result.ProblemCode!);
        }

        return TypedResults.Ok(ToResponse(tokens, result.Session!));
    }

    private static async Task<IResult> MeAsync(
        ICurrentUser user,
        AuthService auth,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var profile = await auth.GetProfileAsync(user.StaffUserId!.Value, user.BranchId!.Value, cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new MeResponse(
            profile.StaffUserId,
            profile.StaffCode,
            profile.FullName,
            profile.HomeBranchId,
            profile.EffectiveBranchId,
            profile.CanSwitchBranch,
            http.User.FindFirst(ClaimNames.MustChangeSecretKey)?.Value == "true",
            profile.Branches,
            profile.Permissions));
    }

    private static TokenResponse ToResponse(TokenIssuer tokens, SessionInfo session)
    {
        var token = tokens.Issue(session);
        return new TokenResponse(
            token.AccessToken,
            token.ExpiresAtUtc,
            token.SessionMaxExpiresAtUtc,
            session.SessionId,
            session.BranchId,
            session.MustChangeSecretKey);
    }
}
