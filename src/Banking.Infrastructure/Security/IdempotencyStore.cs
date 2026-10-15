using System.Security.Cryptography;
using System.Text;
using Banking.Application.Abstractions;
using Banking.Application.Errors;
using Banking.Domain.Security;
using Banking.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Banking.Infrastructure.Security;

public enum IdempotencyDecision
{
    /// <summary>Run the request; the key is reserved.</summary>
    Proceed,

    /// <summary>Return the stored response.</summary>
    Replay,

    /// <summary>Refuse with <see cref="IdempotencyBegin.ProblemCode"/>.</summary>
    Refuse
}

public sealed record IdempotencyBegin(IdempotencyDecision Decision, int Status = 0, string? Body = null, string? ProblemCode = null);

/// <summary>
/// Addendum 4: an <c>Idempotency-Key</c> makes a retried upload or create return the first result instead of
/// running again. Keys are per staff member and kept for 24 hours. Only successful results are stored; a failed
/// request releases its key so the client can correct and resend.
/// All writes here are plain SQL so they never join (or trigger) the business save and its activity row.
/// </summary>
public sealed class IdempotencyStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    private readonly BankingDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;

    public IdempotencyStore(BankingDbContext db, ICurrentUser currentUser, TimeProvider timeProvider)
    {
        _db = db;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }

    public static bool IsValidKey(string? key) =>
        key is { Length: > 0 and <= 100 } && key.All(static character => character is > ' ' and <= '~');

    /// <summary>SHA-256 of the route and the request content, as 64 lower-case hex characters.</summary>
    public static string HashOf(string route, ReadOnlySpan<byte> content)
    {
        ArgumentNullException.ThrowIfNull(route);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(route));
        hash.AppendData(content);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    public async Task<IdempotencyBegin> BeginAsync(string key, string route, string requestHash, CancellationToken cancellationToken)
    {
        var staffUserId = _currentUser.StaffUserId ?? throw new InvalidOperationException("Idempotency needs a signed-in user.");
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // An expired key is free again.
        await _db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM [dbo].[IdempotencyKey] WHERE [StaffUserId] = {staffUserId} AND [KeyValue] = {key} AND [ExpiresOn] < {now}",
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await _db.Database.ExecuteSqlInterpolatedAsync(
                    $@"INSERT INTO [dbo].[IdempotencyKey] ([KeyValue], [StaffUserId], [Route], [RequestHash], [ResponseStatus], [ResponseBody], [CreatedOn], [ExpiresOn])
                       VALUES ({key}, {staffUserId}, {route}, {requestHash}, 0, NULL, {now}, {now + Lifetime})",
                    cancellationToken)
                .ConfigureAwait(false);
            return new IdempotencyBegin(IdempotencyDecision.Proceed);
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            var existing = await _db.Set<IdempotencyKey>()
                .AsNoTracking()
                .Where(row => row.StaffUserId == staffUserId && row.KeyValue == key)
                .Select(row => new { row.Route, row.RequestHash, row.ResponseStatus, row.ResponseBody })
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (existing is null)
            {
                return new IdempotencyBegin(IdempotencyDecision.Refuse, ProblemCode: ProblemCodes.IdempotencyInProgress);
            }

            if (existing.Route != route || existing.RequestHash != requestHash)
            {
                return new IdempotencyBegin(IdempotencyDecision.Refuse, ProblemCode: ProblemCodes.IdempotencyKeyReused);
            }

            return existing.ResponseStatus == 0
                ? new IdempotencyBegin(IdempotencyDecision.Refuse, ProblemCode: ProblemCodes.IdempotencyInProgress)
                : new IdempotencyBegin(IdempotencyDecision.Replay, existing.ResponseStatus, existing.ResponseBody);
        }
    }

    public async Task CompleteAsync(string key, int status, string? body, CancellationToken cancellationToken)
    {
        var staffUserId = _currentUser.StaffUserId ?? throw new InvalidOperationException("Idempotency needs a signed-in user.");
        await _db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [dbo].[IdempotencyKey] SET [ResponseStatus] = {status}, [ResponseBody] = {body} WHERE [StaffUserId] = {staffUserId} AND [KeyValue] = {key}",
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task ReleaseAsync(string key, CancellationToken cancellationToken)
    {
        var staffUserId = _currentUser.StaffUserId ?? throw new InvalidOperationException("Idempotency needs a signed-in user.");
        await _db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM [dbo].[IdempotencyKey] WHERE [StaffUserId] = {staffUserId} AND [KeyValue] = {key} AND [ResponseStatus] = 0",
                cancellationToken)
            .ConfigureAwait(false);
    }
}
