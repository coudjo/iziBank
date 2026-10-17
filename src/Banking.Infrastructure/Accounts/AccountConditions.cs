using Banking.Application.Abstractions;
using Banking.Application.Errors;
using Banking.Domain.Accounts;
using Banking.Domain.Lookups;
using Banking.Domain.Supervision;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Banking.Infrastructure.Accounts;

public sealed record FreezeActionDraft(string? Action, decimal? Amount, string? Remarks);

public sealed record FreezeActionResult(long FreezeReleaseId, bool IsFreezed, AccountBalanceView Balance);

public sealed record FreezeActionView(long FreezeReleaseId, string Action, decimal? Amount, string? Remarks, long? CreatedByUserId, DateTime CreatedOn);

/// <summary>Checks shared by every change to an account (plan 6.10, D2, R3).</summary>
internal static class AccountGuards
{
    /// <summary>
    /// The problem that stops a change to <paramref name="account"/>, or null: another branch, closed (read-only), and,
    /// when <paramref name="requireApproved"/>, still waiting for approval.
    /// </summary>
    public static string? ProblemFor(Account account, int? effectiveBranchId, bool requireApproved)
    {
        if (account.BranchId != effectiveBranchId)
        {
            return ProblemCodes.RecordOtherBranch;
        }

        if (account.ClosedOn is not null)
        {
            return ProblemCodes.AccountClosed;
        }

        return requireApproved && !SupervisionRules.CheckUsable(account).Allowed ? SupervisionProblems.Pending : null;
    }
}

/// <summary>
/// A5-06: holds, releases, freeze and unfreeze (plan 6.10). Each call writes its history row and the current state in
/// one transaction. Writers take an update lock on the balance row, so parallel holds queue instead of colliding; the
/// row version still guards against any writer that does not lock, with up to three retries before
/// <c>record.changed</c> (plan 5.8).
/// </summary>
public sealed class AccountConditions
{
    public const int MaxAttempts = 3;
    private const int RemarksLength = 200;

    private readonly BankingDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IActivityContext _activity;

    public AccountConditions(BankingDbContext db, ICurrentUser currentUser, IActivityContext activity)
    {
        _db = db;
        _currentUser = currentUser;
        _activity = activity;
    }

    public async Task<IReadOnlyList<FreezeActionView>?> HistoryAsync(long accountId, CancellationToken cancellationToken)
    {
        if (!await _db.Set<Account>().AnyAsync(row => row.AccountId == accountId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return await _db.Set<FreezeReleaseAccountBalance>()
            .AsNoTracking()
            .Where(row => row.AccountId == accountId)
            .OrderByDescending(row => row.FreezeReleaseId)
            .Select(row => new FreezeActionView(row.FreezeReleaseId, row.Action!.Code, row.Amount, row.Remarks, row.CreatedByUserId, row.CreatedOn))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OperationResult<FreezeActionResult>> ApplyAsync(long accountId, FreezeActionDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var action = draft.Action?.Trim().ToUpperInvariant() ?? string.Empty;
        var actionId = await _db.Set<FreezeReleaseActionType>()
            .Where(row => row.Code == action && row.IsActive)
            .Select(row => (int?)row.Id)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var errors = Validate(action, actionId, draft);
        if (errors.Count > 0)
        {
            return OperationResult.Fail<FreezeActionResult>(ProblemCodes.Validation, errors);
        }

        var remarks = string.IsNullOrWhiteSpace(draft.Remarks) ? null : draft.Remarks.Trim();
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var transaction = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                await _db.Database
                    .ExecuteSqlInterpolatedAsync($"SELECT [AccountId] FROM [AccountBalance] WITH (UPDLOCK, ROWLOCK) WHERE [AccountId] = {accountId}", cancellationToken)
                    .ConfigureAwait(false);

                var account = await _db.Set<Account>()
                    .Include(row => row.Balance)
                    .Include(row => row.Condition)
                    .Include(row => row.Product!.Currency)
                    .SingleOrDefaultAsync(row => row.AccountId == accountId, cancellationToken)
                    .ConfigureAwait(false);
                if (account?.Balance is null || account.Condition is null)
                {
                    return OperationResult.Fail<FreezeActionResult>(ProblemCodes.NotFound);
                }

                if (AccountGuards.ProblemFor(account, _currentUser.BranchId, requireApproved: true) is { } problem)
                {
                    return OperationResult.Fail<FreezeActionResult>(problem);
                }

                var failure = ApplyTo(account, action, draft.Amount);
                if (failure is not null)
                {
                    _db.ChangeTracker.Clear();
                    return failure;
                }

                var history = new FreezeReleaseAccountBalance
                {
                    AccountId = accountId,
                    FreezeReleaseActionId = actionId!.Value,
                    Amount = draft.Amount,
                    Remarks = remarks
                };
                _db.Set<FreezeReleaseAccountBalance>().Add(history);
                _activity.Record(EventFor(action), nameof(Account), account.AccountNumber, draft.Amount is { } amount ? new Dictionary<string, object?> { ["amount"] = amount } : null);

                try
                {
                    await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return OperationResult.Ok(new FreezeActionResult(
                        history.FreezeReleaseId,
                        account.Condition.IsFreezed,
                        AccountAdmin.BalanceView(account.Balance, account.Product!.MinimumBalance, account.Product.Currency!.CurrencyCode)));
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Someone changed the balance without the lock: reload and try again.
                    _db.ChangeTracker.Clear();
                    _activity.Written = false;
                }
            }
        }

        return OperationResult.Fail<FreezeActionResult>(ProblemCodes.ConcurrencyConflict);
    }

    private static OperationResult<FreezeActionResult>? ApplyTo(Account account, string action, decimal? amount)
    {
        switch (action)
        {
            case LookupCodes.FreezeReleaseActions.HoldAmount:
                FreezeActionRules.TryApply(account.Balance!, isHold: true, amount!.Value);
                return null;

            case LookupCodes.FreezeReleaseActions.ReleaseAmount:
                return FreezeActionRules.TryApply(account.Balance!, isHold: false, amount!.Value)
                    ? null
                    : OperationResult.Fail<FreezeActionResult>(
                        ProblemCodes.Validation,
                        new Dictionary<string, string[]>(StringComparer.Ordinal) { ["amount"] = ["You cannot release more than is held."] });

            case LookupCodes.FreezeReleaseActions.FreezeAccount:
                if (account.Condition!.IsFreezed)
                {
                    return OperationResult.Fail<FreezeActionResult>(ProblemCodes.AccountAlreadyFrozen);
                }

                account.Condition.IsFreezed = true;
                return null;

            default:
                if (!account.Condition!.IsFreezed)
                {
                    return OperationResult.Fail<FreezeActionResult>(ProblemCodes.AccountNotFrozen);
                }

                account.Condition.IsFreezed = false;
                return null;
        }
    }

    private static Dictionary<string, string[]> Validate(string action, int? actionId, FreezeActionDraft draft)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (actionId is null || !LookupCodes.FreezeReleaseActions.All.Contains(action, StringComparer.Ordinal))
        {
            errors["action"] = ["Choose hold, release, freeze or unfreeze."];
            return errors;
        }

        var takesAmount = action is LookupCodes.FreezeReleaseActions.HoldAmount or LookupCodes.FreezeReleaseActions.ReleaseAmount;
        if (takesAmount && draft.Amount is not > 0m)
        {
            errors["amount"] = ["Enter an amount greater than zero."];
        }
        else if (takesAmount && decimal.Round(draft.Amount!.Value, 2) != draft.Amount.Value)
        {
            errors["amount"] = ["Use at most two decimal places."];
        }
        else if (!takesAmount && draft.Amount is not null)
        {
            errors["amount"] = ["Freezing or unfreezing an account takes no amount."];
        }

        if (draft.Remarks is { Length: > RemarksLength })
        {
            errors["remarks"] = ["Enter up to 200 characters."];
        }

        return errors;
    }

    private static string EventFor(string action) => action switch
    {
        LookupCodes.FreezeReleaseActions.HoldAmount => EventCodes.BalanceHold,
        LookupCodes.FreezeReleaseActions.ReleaseAmount => EventCodes.BalanceRelease,
        LookupCodes.FreezeReleaseActions.FreezeAccount => EventCodes.AccountFreeze,
        _ => EventCodes.AccountUnfreeze
    };
}
