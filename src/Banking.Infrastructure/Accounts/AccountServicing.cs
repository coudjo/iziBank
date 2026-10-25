using Banking.Application.Abstractions;
using Banking.Application.Errors;
using Banking.Domain.Accounts;
using Banking.Domain.Lookups;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Banking.Infrastructure.Accounts;

public sealed record CloseAccountDraft(int? CloseReasonId);

public sealed record AccountCloseStatus(bool CanClose, IReadOnlyList<AccountCloseCheck> Checks);

public sealed record AccountClassificationView(long AssignmentId, int AccountClassificationId, string ClassificationCode, string ClassificationName, int? AccountSubClassificationId, string? SubClassificationCode, string? SubClassificationName);

public sealed record ClassificationDraft(int? AccountClassificationId, int? AccountSubClassificationId);

public sealed record SubClassificationOption(int AccountSubClassificationId, string SubClassificationCode, string SubClassificationName);

public sealed record ClassificationOption(int AccountClassificationId, string ClassificationCode, string ClassificationName, IReadOnlyList<SubClassificationOption> SubClassifications);

/// <summary>
/// A5-07 (plan 6.10): close, dormancy, classifications and reminders. A closed account is read-only: every change
/// here, and every hold or freeze, is refused with <c>account.closed</c>. Changes need the effective branch to be the
/// account's branch; dormancy and closing also need the account to be approved.
/// </summary>
public sealed class AccountServicing
{
    private const int ReminderLength = 200;

    private readonly BankingDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IActivityContext _activity;
    private readonly TimeProvider _timeProvider;

    public AccountServicing(BankingDbContext db, ICurrentUser currentUser, IActivityContext activity, TimeProvider timeProvider)
    {
        _db = db;
        _currentUser = currentUser;
        _activity = activity;
        _timeProvider = timeProvider;
    }

    /// <summary>What the account still owes in charge arrears (B5-02). An account with any cannot be closed.</summary>
    private Task<decimal> OutstandingArrearsAsync(long accountId, CancellationToken cancellationToken) =>
        _db.Set<Banking.Domain.Charges.AccountChargeArrear>()
            .AsNoTracking()
            .Where(row => row.AccountId == accountId)
            .SumAsync(row => row.OutstandingAmount, cancellationToken);

    // ---------------------------------------------------------------- Close

    public async Task<AccountCloseStatus?> CloseStatusAsync(long accountId, CancellationToken cancellationToken)
    {
        var balance = await _db.Set<AccountBalance>().AsNoTracking().SingleOrDefaultAsync(row => row.AccountId == accountId, cancellationToken).ConfigureAwait(false);
        if (balance is null)
        {
            return null;
        }

        var checks = AccountCloseRules.Check(balance, await OutstandingArrearsAsync(accountId, cancellationToken).ConfigureAwait(false));
        return new AccountCloseStatus(checks.All(static check => check.Passed), checks);
    }

    public async Task<OperationResult<long>> CloseAsync(long accountId, CloseAccountDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var userId = _currentUser.StaffUserId ?? throw new InvalidOperationException("Closing an account needs a signed-in user.");
        if (draft.CloseReasonId is not { } reasonId
            || !await _db.Set<CloseReasonType>().AnyAsync(row => row.Id == reasonId && row.IsActive, cancellationToken).ConfigureAwait(false))
        {
            return OperationResult.Fail<long>(
                ProblemCodes.Validation,
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["closeReasonId"] = ["Choose why the account is closing."] });
        }

        var transaction = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            // The same lock as holds, so a hold cannot slip in between the checks and the close.
            await _db.Database
                .ExecuteSqlInterpolatedAsync($"SELECT [AccountId] FROM [AccountBalance] WITH (UPDLOCK, ROWLOCK) WHERE [AccountId] = {accountId}", cancellationToken)
                .ConfigureAwait(false);

            var account = await _db.Set<Account>().Include(row => row.Balance).SingleOrDefaultAsync(row => row.AccountId == accountId, cancellationToken).ConfigureAwait(false);
            if (account?.Balance is null)
            {
                return OperationResult.Fail<long>(ProblemCodes.NotFound);
            }

            if (AccountGuards.ProblemFor(account, _currentUser.BranchId, requireApproved: true) is { } problem)
            {
                return OperationResult.Fail<long>(problem);
            }

            var arrears = await OutstandingArrearsAsync(accountId, cancellationToken).ConfigureAwait(false);
            var failed = AccountCloseRules.Check(account.Balance, arrears).Where(static check => !check.Passed).Select(static check => check.Code).ToList();
            if (failed.Count > 0)
            {
                return OperationResult.FailWith<long>(ProblemCodes.AccountCloseBlocked, new Dictionary<string, object?>(StringComparer.Ordinal) { ["failedChecks"] = failed });
            }

            account.ClosedOn = _timeProvider.GetUtcNow().UtcDateTime;
            account.ClosedByUserId = userId;
            account.CloseReasonId = reasonId;
            _activity.Record(EventCodes.AccountClose, nameof(Account), account.AccountNumber);

            var saved = await SaveAsync(accountId, cancellationToken).ConfigureAwait(false);
            if (saved.Succeeded)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            return saved;
        }
    }

    // ---------------------------------------------------------------- Dormancy

    public async Task<OperationResult<long>> SetDormantAsync(long accountId, bool dormant, CancellationToken cancellationToken)
    {
        var account = await _db.Set<Account>().SingleOrDefaultAsync(row => row.AccountId == accountId, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        if (AccountGuards.ProblemFor(account, _currentUser.BranchId, requireApproved: true) is { } problem)
        {
            return OperationResult.Fail<long>(problem);
        }

        if (account.IsDormant == dormant)
        {
            return OperationResult.Fail<long>(dormant ? ProblemCodes.AccountAlreadyDormant : ProblemCodes.AccountNotDormant);
        }

        account.IsDormant = dormant;
        if (!dormant)
        {
            _db.Set<DormantAccountHistory>().Add(new DormantAccountHistory
            {
                AccountId = accountId,
                ActivatedByUserId = _currentUser.StaffUserId,
                DateActivated = _timeProvider.GetUtcNow().UtcDateTime
            });
        }

        _activity.Record(EventCodes.DormancyChange, nameof(Account), account.AccountNumber, new Dictionary<string, object?> { ["dormant"] = dormant });
        return await SaveAsync(accountId, cancellationToken).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- Classifications

    public async Task<IReadOnlyList<ClassificationOption>> ClassificationCatalogAsync(CancellationToken cancellationToken)
    {
        var classifications = await _db.Set<AccountClassification>()
            .AsNoTracking()
            .OrderBy(row => row.ClassificationCode)
            .Select(row => new
            {
                row.AccountClassificationId,
                row.ClassificationCode,
                row.ClassificationName,
                Subs = row.SubClassifications
                    .OrderBy(sub => sub.SubClassificationCode)
                    .Select(sub => new SubClassificationOption(sub.AccountSubClassificationId, sub.SubClassificationCode, sub.SubClassificationName))
                    .ToList()
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return classifications
            .Select(row => new ClassificationOption(row.AccountClassificationId, row.ClassificationCode, row.ClassificationName, row.Subs))
            .ToList();
    }

    /// <summary>Adds a classification (<paramref name="parentId"/> null) or a sub-classification under one.</summary>
    public async Task<OperationResult<int>> CreateClassificationAsync(int? parentId, string? code, string? name, CancellationToken cancellationToken)
    {
        var cleanCode = code?.Trim().ToUpperInvariant() ?? string.Empty;
        var cleanName = name?.Trim() ?? string.Empty;
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (cleanCode.Length is 0 or > 10 || !cleanCode.All(static character => char.IsAsciiLetterUpper(character) || char.IsAsciiDigit(character) || character is '_' or '-'))
        {
            errors["code"] = ["Use 1 to 10 capital letters, digits, dashes or underscores."];
        }

        if (cleanName.Length is 0 or > 100)
        {
            errors["name"] = ["Enter a name of up to 100 characters."];
        }

        if (errors.Count > 0)
        {
            return OperationResult.Fail<int>(ProblemCodes.Validation, errors);
        }

        if (parentId is { } classificationId)
        {
            if (!await _db.Set<AccountClassification>().AnyAsync(row => row.AccountClassificationId == classificationId, cancellationToken).ConfigureAwait(false))
            {
                return OperationResult.Fail<int>(ProblemCodes.NotFound);
            }

            if (await _db.Set<AccountSubClassification>().AnyAsync(row => row.SubClassificationCode == cleanCode, cancellationToken).ConfigureAwait(false))
            {
                return OperationResult.Fail<int>(ProblemCodes.LookupCodeTaken);
            }

            var sub = new AccountSubClassification { AccountClassificationId = classificationId, SubClassificationCode = cleanCode, SubClassificationName = cleanName };
            _db.Set<AccountSubClassification>().Add(sub);
            _activity.Record(EventCodes.CreateRecord, nameof(AccountSubClassification), cleanCode);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok(sub.AccountSubClassificationId);
        }

        if (await _db.Set<AccountClassification>().AnyAsync(row => row.ClassificationCode == cleanCode, cancellationToken).ConfigureAwait(false))
        {
            return OperationResult.Fail<int>(ProblemCodes.LookupCodeTaken);
        }

        var classification = new AccountClassification { ClassificationCode = cleanCode, ClassificationName = cleanName };
        _db.Set<AccountClassification>().Add(classification);
        _activity.Record(EventCodes.CreateRecord, nameof(AccountClassification), cleanCode);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(classification.AccountClassificationId);
    }

    public async Task<IReadOnlyList<AccountClassificationView>?> ClassificationsAsync(long accountId, CancellationToken cancellationToken)
    {
        if (!await _db.Set<Account>().AnyAsync(row => row.AccountId == accountId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return await _db.Set<AccountClassificationAssignment>()
            .AsNoTracking()
            .Where(row => row.AccountId == accountId)
            .OrderBy(row => row.Classification!.ClassificationCode)
            .Select(row => new AccountClassificationView(
                row.AssignmentId,
                row.AccountClassificationId,
                row.Classification!.ClassificationCode,
                row.Classification.ClassificationName,
                row.AccountSubClassificationId,
                row.AccountSubClassificationId == null ? null : row.SubClassification!.SubClassificationCode,
                row.AccountSubClassificationId == null ? null : row.SubClassification!.SubClassificationName))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OperationResult<long>> AssignClassificationAsync(long accountId, ClassificationDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var (account, problem) = await ForChangeAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (problem is not null)
        {
            return OperationResult.Fail<long>(problem);
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (draft.AccountClassificationId is not { } classificationId
            || !await _db.Set<AccountClassification>().AnyAsync(row => row.AccountClassificationId == classificationId, cancellationToken).ConfigureAwait(false))
        {
            errors["accountClassificationId"] = ["Choose a classification from the list."];
        }
        else if (draft.AccountSubClassificationId is { } subId
                 && !await _db.Set<AccountSubClassification>()
                     .AnyAsync(row => row.AccountSubClassificationId == subId && row.AccountClassificationId == classificationId, cancellationToken)
                     .ConfigureAwait(false))
        {
            errors["accountSubClassificationId"] = ["Choose a sub-classification of the chosen classification."];
        }

        if (errors.Count > 0)
        {
            return OperationResult.Fail<long>(ProblemCodes.Validation, errors);
        }

        if (await _db.Set<AccountClassificationAssignment>()
                .AnyAsync(row => row.AccountId == accountId && row.AccountClassificationId == draft.AccountClassificationId, cancellationToken)
                .ConfigureAwait(false))
        {
            return OperationResult.Fail<long>(ProblemCodes.AccountClassificationExists);
        }

        var assignment = new AccountClassificationAssignment
        {
            AccountId = accountId,
            AccountClassificationId = draft.AccountClassificationId!.Value,
            AccountSubClassificationId = draft.AccountSubClassificationId
        };
        _db.Set<AccountClassificationAssignment>().Add(assignment);
        _activity.Record(EventCodes.UpdateRecord, nameof(Account), account!.AccountNumber, new Dictionary<string, object?> { ["classification"] = "added" });

        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // The unique index caught a parallel assignment of the same classification.
            _db.ChangeTracker.Clear();
            _activity.Written = false;
            return OperationResult.Fail<long>(ProblemCodes.AccountClassificationExists);
        }

        return OperationResult.Ok(assignment.AssignmentId);
    }

    public async Task<OperationResult<long>> RemoveClassificationAsync(long accountId, long assignmentId, CancellationToken cancellationToken)
    {
        var (account, problem) = await ForChangeAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (problem is not null)
        {
            return OperationResult.Fail<long>(problem);
        }

        var assignment = await _db.Set<AccountClassificationAssignment>()
            .SingleOrDefaultAsync(row => row.AssignmentId == assignmentId && row.AccountId == accountId, cancellationToken)
            .ConfigureAwait(false);
        if (assignment is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        _db.Set<AccountClassificationAssignment>().Remove(assignment);
        _activity.Record(EventCodes.UpdateRecord, nameof(Account), account!.AccountNumber, new Dictionary<string, object?> { ["classification"] = "removed" });
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(assignmentId);
    }

    // ---------------------------------------------------------------- Reminders

    public async Task<IReadOnlyList<AccountReminderView>?> RemindersAsync(long accountId, CancellationToken cancellationToken)
    {
        if (!await _db.Set<Account>().AnyAsync(row => row.AccountId == accountId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return await _db.Set<AccountReminder>()
            .AsNoTracking()
            .Where(row => row.AccountId == accountId)
            .OrderBy(row => row.AccountReminderId)
            .Select(row => new AccountReminderView(row.AccountReminderId, row.ReminderText, row.CreatedOn))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OperationResult<long>> AddReminderAsync(long accountId, string? text, CancellationToken cancellationToken)
    {
        var (account, problem) = await ForChangeAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (problem is not null)
        {
            return OperationResult.Fail<long>(problem);
        }

        var reminderText = text?.Trim() ?? string.Empty;
        if (reminderText.Length is 0 or > ReminderLength)
        {
            return OperationResult.Fail<long>(
                ProblemCodes.Validation,
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["reminderText"] = ["Enter a reminder of up to 200 characters."] });
        }

        var reminder = new AccountReminder { AccountId = accountId, ReminderText = reminderText };
        _db.Set<AccountReminder>().Add(reminder);
        _activity.Record(EventCodes.UpdateRecord, nameof(Account), account!.AccountNumber, new Dictionary<string, object?> { ["reminder"] = "added" });
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(reminder.AccountReminderId);
    }

    public async Task<OperationResult<long>> RemoveReminderAsync(long accountId, long reminderId, CancellationToken cancellationToken)
    {
        var (account, problem) = await ForChangeAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (problem is not null)
        {
            return OperationResult.Fail<long>(problem);
        }

        var reminder = await _db.Set<AccountReminder>()
            .SingleOrDefaultAsync(row => row.AccountReminderId == reminderId && row.AccountId == accountId, cancellationToken)
            .ConfigureAwait(false);
        if (reminder is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        _db.Set<AccountReminder>().Remove(reminder);
        _activity.Record(EventCodes.UpdateRecord, nameof(Account), account!.AccountNumber, new Dictionary<string, object?> { ["reminder"] = "removed" });
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(reminderId);
    }

    /// <summary>Reminders and classifications may be added while an account waits for approval, never once it is closed.</summary>
    private async Task<(Account? Account, string? Problem)> ForChangeAsync(long accountId, CancellationToken cancellationToken)
    {
        var account = await _db.Set<Account>().AsNoTracking().SingleOrDefaultAsync(row => row.AccountId == accountId, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return (null, ProblemCodes.NotFound);
        }

        return (account, AccountGuards.ProblemFor(account, _currentUser.BranchId, requireApproved: false));
    }

    private async Task<OperationResult<long>> SaveAsync(long accountId, CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok(accountId);
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            _activity.Written = false;
            return OperationResult.Fail<long>(ProblemCodes.ConcurrencyConflict);
        }
    }
}
