using System.Globalization;
using Banking.Application.Abstractions;
using Banking.Application.Errors;
using Banking.Domain.Ledger;
using Banking.Domain.Lookups;
using Banking.Domain.Security;
using Banking.Domain.Supervision;
using Banking.Domain.Transactions;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Banking.Infrastructure.Transactions;

public sealed record TillSessionView(
    long TillSessionId,
    long StaffUserId,
    string StaffCode,
    string StaffName,
    int BranchId,
    DateOnly WorkingDate,
    long TellerLedgerAccountId,
    string TellerLedgerAccountCode,
    string Status,
    decimal Balance,
    DateTime OpenedOn,
    DateTime? ClosedOn,
    decimal? CountedAmount,
    decimal? ExpectedAmount,
    long? ClosePostingRequestId);

/// <summary>The signed-in teller's till: whether they have a till account here, and today's session if any.</summary>
public sealed record MyTillView(bool HasTillAccount, string? TellerLedgerAccountCode, TillSessionView? Session);

/// <summary>A branch's tills and vault for the supervisor (plan B 6.4).</summary>
public sealed record BranchTillsView(int BranchId, long? VaultLedgerAccountId, decimal? VaultBalance, IReadOnlyList<TillSessionView> Sessions);

/// <summary>The close: the session, and the posting that empties the till (null when the till was already empty).</summary>
public sealed record TillCloseOutcome(TillSessionView Session, PostingOutcome? Posting);

/// <summary>
/// B2-01 and B2-04: till sessions, cash bought from and sold to the vault, and the count at close (plan B 6.4). A
/// teller opens one session at a time, on their till ledger account in the working branch, starting from zero. Vault
/// movements always wait for a <c>TXN/VAULT</c> supervisor; the close posting for a <c>TXN/TILL</c> supervisor, and its
/// approval closes the session.
/// </summary>
public sealed class Tills
{
    private static readonly string[] TillCodes =
    [
        TransactionCodes.CashDeposit,
        TransactionCodes.CashWithdrawal,
        TransactionCodes.TillBuy,
        TransactionCodes.TillSell
    ];

    private readonly BankingDbContext _db;
    private readonly PostingEngine _engine;
    private readonly ICurrentUser _currentUser;
    private readonly IWorkingDateProvider _workingDates;
    private readonly IActivityContext _activity;
    private readonly TimeProvider _timeProvider;

    public Tills(
        BankingDbContext db,
        PostingEngine engine,
        ICurrentUser currentUser,
        IWorkingDateProvider workingDates,
        IActivityContext activity,
        TimeProvider timeProvider)
    {
        _db = db;
        _engine = engine;
        _currentUser = currentUser;
        _workingDates = workingDates;
        _activity = activity;
        _timeProvider = timeProvider;
    }

    // ---------------------------------------------------------------- Reads

    public async Task<MyTillView> MineAsync(CancellationToken cancellationToken)
    {
        var actor = Actor();
        var account = await TillAccountAsync(actor, cancellationToken).ConfigureAwait(false);
        var session = await _db.Set<TillSession>()
            .AsNoTracking()
            .Where(row => row.StaffUserId == actor.StaffUserId && row.Status != TillSessionStatuses.Closed)
            .Select(row => (long?)row.TillSessionId)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return new MyTillView(
            account is not null,
            account?.Code,
            session is { } id ? await ViewAsync(id, cancellationToken).ConfigureAwait(false) : null);
    }

    public async Task<BranchTillsView> BranchAsync(int branchId, CancellationToken cancellationToken)
    {
        var ids = await _db.Set<TillSession>()
            .AsNoTracking()
            .Where(row => row.BranchId == branchId && row.Status != TillSessionStatuses.Closed)
            .OrderBy(row => row.TillSessionId)
            .Select(row => row.TillSessionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var sessions = new List<TillSessionView>(ids.Count);
        foreach (var id in ids)
        {
            sessions.Add((await ViewAsync(id, cancellationToken).ConfigureAwait(false))!);
        }

        var vault = await SettingAsync(branchId, LedgerSettingCodes.Vault, cancellationToken).ConfigureAwait(false);
        return new BranchTillsView(
            branchId,
            vault,
            vault is { } vaultId ? await BalanceAsync(vaultId, branchId, cancellationToken).ConfigureAwait(false) : null,
            sessions);
    }

    // ---------------------------------------------------------------- Open, buy, sell

    public async Task<OperationResult<TillSessionView>> OpenAsync(CancellationToken cancellationToken)
    {
        var actor = Actor();
        var workingDate = await _workingDates.GetAsync(actor.BranchId, cancellationToken).ConfigureAwait(false);
        var account = await TillAccountAsync(actor, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return OperationResult.Fail<TillSessionView>(ProblemCodes.TillNotAssigned);
        }

        if (await _db.Set<TillSession>().AnyAsync(row => row.StaffUserId == actor.StaffUserId && row.Status != TillSessionStatuses.Closed, cancellationToken).ConfigureAwait(false))
        {
            return OperationResult.Fail<TillSessionView>(ProblemCodes.TillAlreadyOpen);
        }

        if (await BalanceAsync(account.Id, actor.BranchId, cancellationToken).ConfigureAwait(false) != 0m)
        {
            return OperationResult.Fail<TillSessionView>(ProblemCodes.TillNotEmpty);
        }

        var session = new TillSession
        {
            StaffUserId = actor.StaffUserId,
            BranchId = actor.BranchId,
            WorkingDate = workingDate,
            TellerLedgerAccountId = account.Id,
            Status = TillSessionStatuses.Open,
            OpenedOn = _timeProvider.GetUtcNow().UtcDateTime
        };
        _db.Set<TillSession>().Add(session);
        _activity.Record(EventCodes.TillOpen, nameof(TillSession), null);

        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // The filtered unique index caught a second open at the same moment.
            _db.Entry(session).State = EntityState.Detached;
            return OperationResult.Fail<TillSessionView>(ProblemCodes.TillAlreadyOpen);
        }

        return OperationResult.Ok((await ViewAsync(session.TillSessionId, cancellationToken).ConfigureAwait(false))!);
    }

    /// <summary>Cash from the vault into the till: Dr till, Cr vault. Waits for a vault supervisor.</summary>
    public Task<OperationResult<PostingOutcome>> BuyAsync(long tillSessionId, decimal? amount, CancellationToken cancellationToken) =>
        MoveAsync(tillSessionId, amount, buy: true, cancellationToken);

    /// <summary>Cash from the till back to the vault: Dr vault, Cr till. Waits for a vault supervisor.</summary>
    public Task<OperationResult<PostingOutcome>> SellAsync(long tillSessionId, decimal? amount, CancellationToken cancellationToken) =>
        MoveAsync(tillSessionId, amount, buy: false, cancellationToken);

    private async Task<OperationResult<PostingOutcome>> MoveAsync(long tillSessionId, decimal? amount, bool buy, CancellationToken cancellationToken)
    {
        if (amount is not { } value || !PostingRules.IsValidAmount(value))
        {
            return OperationResult.Fail<PostingOutcome>(ProblemCodes.Validation, AmountError("amount"));
        }

        var actor = Actor();
        var (session, problem) = await OpenSessionAsync(actor, tillSessionId, cancellationToken).ConfigureAwait(false);
        if (problem is not null)
        {
            return OperationResult.Fail<PostingOutcome>(problem);
        }

        var vault = await SettingAsync(actor.BranchId, LedgerSettingCodes.Vault, cancellationToken).ConfigureAwait(false);
        if (vault is not { } vaultId)
        {
            return Missing<PostingOutcome>(LedgerSettingCodes.Vault, actor.BranchId);
        }

        var till = session!.TellerLedgerAccountId;
        LedgerLine[] lines = buy
            ? [new(till, actor.BranchId, EntrySides.Debit, value), new(vaultId, actor.BranchId, EntrySides.Credit, value)]
            : [new(vaultId, actor.BranchId, EntrySides.Debit, value), new(till, actor.BranchId, EntrySides.Credit, value)];
        var floor = buy
            ? new LedgerFloor(vaultId, actor.BranchId, ProblemCodes.VaultInsufficientCash)
            : new LedgerFloor(till, actor.BranchId, ProblemCodes.TillInsufficientCash);

        return await _engine.SubmitAsync(
                actor,
                new PostingDraft(
                    buy ? TransactionCodes.TillBuy : TransactionCodes.TillSell,
                    LimitCode: null,
                    value,
                    buy ? "Cash from the vault" : "Cash to the vault",
                    [],
                    lines,
                    CheckSignerKyc: false,
                    new Dictionary<string, string?>(StringComparer.Ordinal) { ["tillSessionId"] = Key(tillSessionId) },
                    [floor]),
                cancellationToken)
            .ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- Close

    /// <summary>
    /// The teller's count (plan B 6.4). An empty till that counts zero closes at once. Otherwise the session goes to
    /// <c>CLOSING</c> and one posting waits for a supervisor: the counted cash to the vault, any shortage to cash short
    /// and any overage to cash over. Approval closes the session; rejection or cancellation opens it again.
    /// </summary>
    public async Task<OperationResult<TillCloseOutcome>> CloseAsync(long tillSessionId, decimal? countedAmount, CancellationToken cancellationToken)
    {
        if (countedAmount is not { } counted || counted < 0m || decimal.Round(counted, 2) != counted || counted > PostingRules.MaxAmount)
        {
            return OperationResult.Fail<TillCloseOutcome>(ProblemCodes.Validation, AmountError("countedAmount"));
        }

        var actor = Actor();
        var (session, problem) = await OpenSessionAsync(actor, tillSessionId, cancellationToken).ConfigureAwait(false);
        if (problem is not null)
        {
            return OperationResult.Fail<TillCloseOutcome>(problem);
        }

        var pending = await _db.Set<PostingRequest>()
            .AnyAsync(
                row => row.RequestedByUserId == actor.StaffUserId
                       && row.BranchId == actor.BranchId
                       && row.Status == PostingRequestStatuses.Pending
                       && TillCodes.Contains(row.TransactionCode!.Code),
                cancellationToken)
            .ConfigureAwait(false);
        if (pending)
        {
            return OperationResult.Fail<TillCloseOutcome>(ProblemCodes.TillPendingRequests);
        }

        var expected = await BalanceAsync(session!.TellerLedgerAccountId, actor.BranchId, cancellationToken).ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        session.CountedAmount = counted;
        session.ExpectedAmount = expected;

        if (expected == 0m && counted == 0m)
        {
            session.Status = TillSessionStatuses.Closed;
            session.ClosedOn = now;
            _activity.Record(EventCodes.TillClose, nameof(TillSession), Key(tillSessionId));
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok(new TillCloseOutcome((await ViewAsync(tillSessionId, cancellationToken).ConfigureAwait(false))!, null));
        }

        var vault = await SettingAsync(actor.BranchId, LedgerSettingCodes.Vault, cancellationToken).ConfigureAwait(false);
        var cashShort = await SettingAsync(actor.BranchId, LedgerSettingCodes.CashShort, cancellationToken).ConfigureAwait(false);
        var cashOver = await SettingAsync(actor.BranchId, LedgerSettingCodes.CashOver, cancellationToken).ConfigureAwait(false);
        if (vault is null && counted > 0m)
        {
            return Missing<TillCloseOutcome>(LedgerSettingCodes.Vault, actor.BranchId);
        }

        if (cashShort is null && counted < expected)
        {
            return Missing<TillCloseOutcome>(LedgerSettingCodes.CashShort, actor.BranchId);
        }

        if (cashOver is null && counted > expected)
        {
            return Missing<TillCloseOutcome>(LedgerSettingCodes.CashOver, actor.BranchId);
        }

        var plan = TillCloseRules.Plan(expected, counted, actor.BranchId, session.TellerLedgerAccountId, vault ?? 0, cashShort, cashOver)!.Value;

        // Mark the session closing first, so no cash posting can start on it while the close waits.
        session.Status = TillSessionStatuses.Closing;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var posting = await _engine.SubmitAsync(
                actor,
                new PostingDraft(
                    plan.TransactionCode,
                    LimitCode: null,
                    plan.Amount,
                    "Till close",
                    [],
                    plan.Lines,
                    CheckSignerKyc: false,
                    new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        ["tillSessionId"] = Key(tillSessionId),
                        ["expected"] = expected.ToString(CultureInfo.InvariantCulture),
                        ["counted"] = counted.ToString(CultureInfo.InvariantCulture)
                    },
                    ClosesTillSessionId: tillSessionId),
                cancellationToken)
            .ConfigureAwait(false);

        var stored = await _db.Set<TillSession>().SingleAsync(row => row.TillSessionId == tillSessionId, cancellationToken).ConfigureAwait(false);
        if (!posting.Succeeded)
        {
            stored.Status = TillSessionStatuses.Open;
            stored.CountedAmount = null;
            stored.ExpectedAmount = null;
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new OperationResult<TillCloseOutcome>(default, posting.ProblemCode, posting.Errors, posting.Extensions);
        }

        stored.ClosePostingRequestId = posting.Value!.PostingRequestId;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(new TillCloseOutcome((await ViewAsync(tillSessionId, cancellationToken).ConfigureAwait(false))!, posting.Value));
    }

    // ---------------------------------------------------------------- Shared

    /// <summary>The actor's open session for today, or the problem that stops a cash action on it.</summary>
    internal async Task<(TillSession? Session, string? Problem)> OpenSessionAsync(PostingActor actor, long? tillSessionId, CancellationToken cancellationToken)
    {
        var workingDate = await _workingDates.GetAsync(actor.BranchId, cancellationToken).ConfigureAwait(false);
        var session = await _db.Set<TillSession>()
            .Where(row => row.StaffUserId == actor.StaffUserId && row.Status != TillSessionStatuses.Closed)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (session is null
            || (tillSessionId is { } wanted && session.TillSessionId != wanted)
            || session.Status != TillSessionStatuses.Open
            || session.BranchId != actor.BranchId
            || session.WorkingDate != workingDate)
        {
            return (null, ProblemCodes.TillNotOpen);
        }

        return (session, null);
    }

    /// <summary>A ledger account's balance in one branch (debits minus credits): the cash an asset account holds.</summary>
    internal async Task<decimal> BalanceAsync(long ledgerAccountId, int branchId, CancellationToken cancellationToken) =>
        await _db.Set<LedgerEntry>()
            .AsNoTracking()
            .Where(row => row.LedgerAccountId == ledgerAccountId && row.BranchId == branchId)
            .SumAsync(row => row.CrDb == EntrySides.Debit ? row.Amount : -row.Amount, cancellationToken)
            .ConfigureAwait(false);

    internal async Task<long?> SettingAsync(int branchId, string settingCode, CancellationToken cancellationToken) =>
        await _db.Set<SystemLedgerSetting>()
            .AsNoTracking()
            .Where(row => row.BranchId == branchId && row.SettingCode == settingCode)
            .Select(row => (long?)row.LedgerAccountId)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    internal PostingActor Actor() =>
        new(
            _currentUser.StaffUserId ?? throw new InvalidOperationException("Till work needs a signed-in user."),
            _currentUser.BranchId ?? throw new InvalidOperationException("Till work needs a working branch."));

    private sealed record TillAccount(long Id, string Code);

    /// <summary>The teller's approved till ledger account, when it is in the working branch.</summary>
    private async Task<TillAccount?> TillAccountAsync(PostingActor actor, CancellationToken cancellationToken)
    {
        var row = await _db.Set<StaffUser>()
            .AsNoTracking()
            .Where(staff => staff.StaffUserId == actor.StaffUserId && staff.IsTeller && staff.TellerGLAccountId != null)
            .Select(staff => staff.TellerGLAccount)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return row is not null && row.BranchId == actor.BranchId && SupervisionRules.CheckUsable(row).Allowed
            ? new TillAccount(row.LedgerAccountId, row.LedgerAccountCode)
            : null;
    }

    private async Task<TillSessionView?> ViewAsync(long tillSessionId, CancellationToken cancellationToken)
    {
        var row = await _db.Set<TillSession>()
            .AsNoTracking()
            .Where(item => item.TillSessionId == tillSessionId)
            .Select(item => new
            {
                Session = item,
                item.StaffUser!.StaffCode,
                item.StaffUser.FullName,
                AccountCode = item.TellerLedgerAccount!.LedgerAccountCode
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        var session = row.Session;
        return new TillSessionView(
            session.TillSessionId,
            session.StaffUserId,
            row.StaffCode,
            row.FullName,
            session.BranchId,
            session.WorkingDate,
            session.TellerLedgerAccountId,
            row.AccountCode,
            session.Status,
            await BalanceAsync(session.TellerLedgerAccountId, session.BranchId, cancellationToken).ConfigureAwait(false),
            session.OpenedOn,
            session.ClosedOn,
            session.CountedAmount,
            session.ExpectedAmount,
            session.ClosePostingRequestId);
    }

    private static OperationResult<T> Missing<T>(string settingCode, int branchId) =>
        OperationResult.FailWith<T>(
            ProblemCodes.LedgerSettingMissing,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["settingCode"] = settingCode, ["branchId"] = branchId });

    private static Dictionary<string, string[]> AmountError(string field) =>
        new(StringComparer.Ordinal) { [field] = ["Enter an amount with at most 2 decimal places."] };

    private static string Key(long id) => id.ToString(CultureInfo.InvariantCulture);
}
