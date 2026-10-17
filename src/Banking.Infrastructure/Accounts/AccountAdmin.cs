using Banking.Application.Abstractions;
using Banking.Application.Errors;
using Banking.Application.Supervision;
using Banking.Domain.Accounts;
using Banking.Domain.Lookups;
using Banking.Domain.Supervision;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Banking.Infrastructure.Accounts;

public sealed record AccountHolderView(long PartyId, string PartyNumber, string DisplayName, string PartyTypeCode, string RoleCode);

public sealed record AccountBalanceView(
    decimal OpeningBalance,
    decimal ClearBalance,
    decimal FreezedAmount,
    decimal UnsupervisedCredit,
    decimal UnsupervisedDebit,
    decimal OverdrawLimit,
    decimal AvailableBalance,
    decimal TotalBalance,
    decimal? MinimumBalance,
    string CurrencyCode,
    DateTime? LastCreditTransactionDate,
    DateTime? LastDebitTransactionDate);

public sealed record AccountConditionView(
    bool IsFreezed,
    bool DoNotAllowCredit,
    bool DoNotAllowDebit,
    decimal? CreditLimit,
    decimal? DebitLimit,
    bool SuperviseAllTransactions,
    bool DoNotApplyTransactionCharges);

public sealed record AccountReminderView(long AccountReminderId, string ReminderText, DateTime CreatedOn);

public sealed record AccountView(
    long AccountId,
    string AccountNumber,
    string AccountName,
    int BranchId,
    string BranchCode,
    int ProductId,
    string ProductCode,
    string ProductName,
    string ProductTypeCode,
    string CurrencyCode,
    DateOnly DateOpened,
    string? OperatingModeCode,
    string Status,
    string SupervisionState,
    bool IsDormant,
    DateTime? ClosedOn,
    string? CloseReasonCode,
    long? OpenedByUserId,
    DateTime CreatedOn,
    long? ModifiedByUserId,
    long? SupervisedByUserId,
    DateTime? SupervisedOn,
    IReadOnlyList<AccountHolderView> Holders,
    AccountBalanceView Balance,
    AccountConditionView Condition,
    IReadOnlyList<AccountReminderView> Reminders);

public static class AccountSupervisionStates
{
    public const string Pending = "PENDING";
    public const string Approved = "APPROVED";
    public const string NotRequired = "NOT_REQUIRED";
}

/// <summary>
/// A5-04 and A5-05: account detail, the balance read (plan 5.8, computed with <see cref="AccountBalanceCalculator"/>),
/// and approval (R2, four-eyes). Rejecting removes a Pending account that was never approved (R5); its number is not
/// reused. Approval and rejection need the effective branch to be the account's branch (D2).
/// </summary>
public sealed class AccountAdmin
{
    private readonly BankingDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IActivityContext _activity;
    private readonly TimeProvider _timeProvider;
    private readonly SupervisionOptions _supervision;

    public AccountAdmin(
        BankingDbContext db,
        ICurrentUser currentUser,
        IActivityContext activity,
        TimeProvider timeProvider,
        IOptions<SupervisionOptions> supervision)
    {
        ArgumentNullException.ThrowIfNull(supervision);
        _db = db;
        _currentUser = currentUser;
        _activity = activity;
        _timeProvider = timeProvider;
        _supervision = supervision.Value;
    }

    public async Task<AccountView?> GetAsync(long accountId, CancellationToken cancellationToken)
    {
        var row = await _db.Set<Account>()
            .AsNoTracking()
            .Where(account => account.AccountId == accountId)
            .Select(account => new
            {
                account.AccountId,
                account.AccountNumber,
                account.AccountName,
                account.BranchId,
                account.Branch!.BranchCode,
                account.ProductId,
                account.Product!.ProductCode,
                account.Product!.ProductName,
                ProductTypeCode = account.Product!.ProductType!.Code,
                account.Product!.Currency!.CurrencyCode,
                account.Product!.MinimumBalance,
                account.DateOpened,
                OperatingModeCode = account.OperatingModeId == null ? null : account.OperatingMode!.Code,
                account.IsSupervisionRequired,
                account.SupervisedByUserId,
                account.SupervisedOn,
                account.IsDormant,
                account.ClosedOn,
                CloseReasonCode = account.CloseReasonId == null ? null : account.CloseReason!.Code,
                account.OpenedByUserId,
                account.CreatedOn,
                account.ModifiedByUserId,
                Balance = account.Balance,
                Condition = account.Condition
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        var holders = await _db.Set<AccountHolder>()
            .AsNoTracking()
            .Where(holder => holder.AccountId == accountId)
            .OrderBy(holder => holder.AccountHolderId)
            .Select(holder => new AccountHolderView(
                holder.PartyId,
                holder.Party!.PartyNumber,
                holder.Party!.DisplayName,
                holder.Party!.PartyType!.Code,
                holder.Role!.Code))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var reminders = await _db.Set<AccountReminder>()
            .AsNoTracking()
            .Where(reminder => reminder.AccountId == accountId)
            .OrderBy(reminder => reminder.AccountReminderId)
            .Select(reminder => new AccountReminderView(reminder.AccountReminderId, reminder.ReminderText, reminder.CreatedOn))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var pending = row.IsSupervisionRequired && row.SupervisedByUserId == null;
        var condition = row.Condition ?? new AccountCondition();
        _activity.Record(EventCodes.ViewRecord, nameof(Account), row.AccountNumber);

        return new AccountView(
            row.AccountId,
            row.AccountNumber,
            row.AccountName,
            row.BranchId,
            row.BranchCode,
            row.ProductId,
            row.ProductCode,
            row.ProductName,
            row.ProductTypeCode,
            row.CurrencyCode,
            row.DateOpened,
            row.OperatingModeCode,
            AccountStatusRules.Derive(pending, row.ClosedOn is not null, condition.IsFreezed, row.IsDormant),
            !row.IsSupervisionRequired ? AccountSupervisionStates.NotRequired
            : pending ? AccountSupervisionStates.Pending : AccountSupervisionStates.Approved,
            row.IsDormant,
            row.ClosedOn,
            row.CloseReasonCode,
            row.OpenedByUserId,
            row.CreatedOn,
            row.ModifiedByUserId,
            row.SupervisedByUserId,
            row.SupervisedOn,
            holders,
            BalanceView(row.Balance ?? new AccountBalance(), row.MinimumBalance, row.CurrencyCode),
            new AccountConditionView(
                condition.IsFreezed,
                condition.DoNotAllowCredit,
                condition.DoNotAllowDebit,
                condition.CreditLimit,
                condition.DebitLimit,
                condition.SuperviseAllTransactions,
                condition.DoNotApplyTransactionCharges),
            reminders);
    }

    public async Task<AccountBalanceView?> GetBalanceAsync(long accountId, CancellationToken cancellationToken)
    {
        var row = await _db.Set<Account>()
            .AsNoTracking()
            .Where(account => account.AccountId == accountId)
            .Select(account => new
            {
                account.AccountNumber,
                account.Balance,
                account.Product!.MinimumBalance,
                account.Product!.Currency!.CurrencyCode
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        _activity.Record(EventCodes.ViewRecord, nameof(AccountBalance), row.AccountNumber);
        return BalanceView(row.Balance ?? new AccountBalance(), row.MinimumBalance, row.CurrencyCode);
    }

    public async Task<OperationResult<long>> ApproveAsync(long accountId, CancellationToken cancellationToken)
    {
        var approverId = _currentUser.StaffUserId ?? throw new InvalidOperationException("Approval needs a signed-in user.");

        var account = await _db.Set<Account>().SingleOrDefaultAsync(row => row.AccountId == accountId, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        if (account.BranchId != _currentUser.BranchId)
        {
            return OperationResult.Fail<long>(ProblemCodes.RecordOtherBranch);
        }

        var check = SupervisionRules.CheckApprove(account, approverId, hasSupervisePermission: true, _supervision.AllowSelfApproval);
        if (!check.Allowed)
        {
            return OperationResult.Fail<long>(check.ProblemCode!);
        }

        SupervisionRules.Approve(account, approverId, _timeProvider.GetUtcNow().UtcDateTime);
        _activity.Record(EventCodes.SuperviseApprove, nameof(Account), account.AccountNumber);
        return await SaveAsync(accountId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationResult<long>> RejectAsync(long accountId, string? reason, CancellationToken cancellationToken)
    {
        var account = await _db.Set<Account>().SingleOrDefaultAsync(row => row.AccountId == accountId, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        if (account.BranchId != _currentUser.BranchId)
        {
            return OperationResult.Fail<long>(ProblemCodes.RecordOtherBranch);
        }

        // The hold and freeze history is never deleted, so an account that has any cannot be rejected.
        var hasDependents = await _db.Set<FreezeReleaseAccountBalance>().AnyAsync(row => row.AccountId == accountId, cancellationToken).ConfigureAwait(false);
        var check = SupervisionRules.CheckReject(account, hasRejectPermission: true, wasEverSupervised: false, hasDependents);
        if (!check.Allowed)
        {
            return OperationResult.Fail<long>(check.ProblemCode!);
        }

        _db.Set<AccountHolder>().RemoveRange(await _db.Set<AccountHolder>().Where(row => row.AccountId == accountId).ToListAsync(cancellationToken).ConfigureAwait(false));
        _db.Set<AccountReminder>().RemoveRange(await _db.Set<AccountReminder>().Where(row => row.AccountId == accountId).ToListAsync(cancellationToken).ConfigureAwait(false));
        _db.Set<AccountClassificationAssignment>().RemoveRange(
            await _db.Set<AccountClassificationAssignment>().Where(row => row.AccountId == accountId).ToListAsync(cancellationToken).ConfigureAwait(false));
        _db.Set<AccountBalance>().RemoveRange(await _db.Set<AccountBalance>().Where(row => row.AccountId == accountId).ToListAsync(cancellationToken).ConfigureAwait(false));
        _db.Set<AccountCondition>().RemoveRange(await _db.Set<AccountCondition>().Where(row => row.AccountId == accountId).ToListAsync(cancellationToken).ConfigureAwait(false));
        _db.Set<Account>().Remove(account);

        var details = string.IsNullOrWhiteSpace(reason)
            ? null
            : new Dictionary<string, object?> { ["reason"] = reason.Length > 300 ? reason[..300] : reason };
        _activity.Record(EventCodes.SuperviseReject, nameof(Account), account.AccountNumber, details);
        return await SaveAsync(accountId, cancellationToken).ConfigureAwait(false);
    }

    internal static AccountBalanceView BalanceView(AccountBalance balance, decimal? minimumBalance, string currencyCode) =>
        new(
            balance.OpeningBalance,
            balance.ClearBalance,
            balance.FreezedAmount,
            balance.UnsupervisedCredit,
            balance.UnsupervisedDebit,
            balance.OverdrawLimit,
            AccountBalanceCalculator.Available(balance),
            AccountBalanceCalculator.Total(balance),
            minimumBalance,
            currencyCode,
            balance.LastCreditTransactionDate,
            balance.LastDebitTransactionDate);

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
