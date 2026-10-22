using System.Globalization;
using Banking.Application.Errors;
using Banking.Application.Transactions;
using Banking.Domain.Accounts;
using Banking.Domain.Ledger;
using Banking.Domain.Lookups;
using Banking.Domain.Parties;
using Banking.Domain.Security;
using Banking.Domain.Transactions;
using Banking.Infrastructure.Parties;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Banking.Infrastructure.Transactions;

/// <summary>Who brought or takes the cash when it is not the holder (plan B 6.1, Q3).</summary>
public sealed record CashPerson(string? Name, string? Mobile, int? IdentificationTypeId, string? IdentificationNumber);

public sealed record CashDepositDraft(long? AccountId, decimal? Amount, string? Narration, bool? ByHolder, CashPerson? Depositor);

public sealed record CashWithdrawalDraft(
    long? AccountId,
    decimal? Amount,
    string? Narration,
    IReadOnlyList<long>? ConfirmedSignerPartyIds,
    bool? ToHolder,
    CashPerson? Recipient);

/// <summary>One person who signs on the account, for the withdrawal screen (plan B 6.3).</summary>
public sealed record AccountSignerView(
    long PartyId,
    string PartyNumber,
    string DisplayName,
    string RoleCode,
    string EffectiveKycStatusCode,
    bool HasEffectiveSignature);

public sealed record AccountSignersView(long AccountId, string AccountNumber, string? OperatingModeCode, IReadOnlyList<AccountSignerView> Signers);

/// <summary>
/// B2-02 and B2-03: cash deposits and withdrawals at the counter (plan B 6.1 to 6.3). The cash moves through the
/// teller's open till; the account may be in any branch (Q4), in which case each branch's <c>INTER_BRANCH</c> account
/// carries the other side. A withdrawal needs the mandate confirmed, never takes the till below zero, and is refused
/// while a signer's KYC has lapsed.
/// </summary>
public sealed class CashPostings
{
    private const int NameLength = 100;
    private const int MobileLength = 20;
    private const int IdNumberLength = 30;

    private readonly BankingDbContext _db;
    private readonly PostingEngine _engine;
    private readonly Tills _tills;
    private readonly KycService _kyc;
    private readonly PartySignatures _signatures;
    private readonly TransactionOptions _options;

    public CashPostings(
        BankingDbContext db,
        PostingEngine engine,
        Tills tills,
        KycService kyc,
        PartySignatures signatures,
        IOptions<TransactionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _db = db;
        _engine = engine;
        _tills = tills;
        _kyc = kyc;
        _signatures = signatures;
        _options = options.Value;
    }

    public async Task<OperationResult<PostingOutcome>> DepositAsync(CashDepositDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var errors = Common(draft.AccountId, draft.Amount, draft.Narration);
        PersonErrors(errors, "depositor", draft.ByHolder, draft.Depositor, draft.Amount);
        if (errors.Count > 0)
        {
            return OperationResult.Fail<PostingOutcome>(ProblemCodes.Validation, errors);
        }

        var actor = _tills.Actor();
        var (session, problem) = await _tills.OpenSessionAsync(actor, null, cancellationToken).ConfigureAwait(false);
        if (problem is not null)
        {
            return OperationResult.Fail<PostingOutcome>(problem);
        }

        var account = await AccountAsync(draft.AccountId!.Value, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return OperationResult.Fail<PostingOutcome>(ProblemCodes.NotFound);
        }

        var amount = draft.Amount!.Value;
        var lines = new List<LedgerLine> { new(session!.TellerLedgerAccountId, actor.BranchId, EntrySides.Debit, amount) };
        var crossing = await CrossBranchAsync(actor.BranchId, account.BranchId, amount, tillSide: EntrySides.Debit, cancellationToken).ConfigureAwait(false);
        if (crossing.Problem is not null)
        {
            return crossing.Problem;
        }

        lines.AddRange(crossing.Lines);
        return await _engine.SubmitAsync(
                actor,
                new PostingDraft(
                    TransactionCodes.CashDeposit,
                    TransactionLimitCodes.CashDeposit,
                    amount,
                    Narration(draft.Narration),
                    [new AccountLine(account.AccountId, EntrySides.Credit, amount)],
                    lines,
                    CheckSignerKyc: false,
                    Details(account.AccountNumber, session.TillSessionId, "depositor", draft.ByHolder, draft.Depositor)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OperationResult<PostingOutcome>> WithdrawAsync(CashWithdrawalDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var errors = Common(draft.AccountId, draft.Amount, draft.Narration);
        PersonErrors(errors, "recipient", draft.ToHolder, draft.Recipient, draft.Amount);
        var confirmed = (draft.ConfirmedSignerPartyIds ?? []).Distinct().ToList();
        if (confirmed.Count == 0)
        {
            errors["confirmedSignerPartyIds"] = ["Confirm who signed for this withdrawal."];
        }

        if (errors.Count > 0)
        {
            return OperationResult.Fail<PostingOutcome>(ProblemCodes.Validation, errors);
        }

        var actor = _tills.Actor();
        var (session, problem) = await _tills.OpenSessionAsync(actor, null, cancellationToken).ConfigureAwait(false);
        if (problem is not null)
        {
            return OperationResult.Fail<PostingOutcome>(problem);
        }

        var account = await AccountAsync(draft.AccountId!.Value, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return OperationResult.Fail<PostingOutcome>(ProblemCodes.NotFound);
        }

        var signers = MandateRules.Signers(account.Holders);
        if (confirmed.Exists(id => !signers.Contains(id)))
        {
            return OperationResult.Fail<PostingOutcome>(
                ProblemCodes.Validation,
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["confirmedSignerPartyIds"] = ["Only people who sign on this account can authorise it."] });
        }

        if (!MandateRules.IsSatisfied(account.Mode, signers, confirmed))
        {
            return OperationResult.Fail<PostingOutcome>(ProblemCodes.TransactionMandateNotConfirmed);
        }

        var amount = draft.Amount!.Value;
        var till = session!.TellerLedgerAccountId;
        var lines = new List<LedgerLine> { new(till, actor.BranchId, EntrySides.Credit, amount) };
        var crossing = await CrossBranchAsync(actor.BranchId, account.BranchId, amount, tillSide: EntrySides.Credit, cancellationToken).ConfigureAwait(false);
        if (crossing.Problem is not null)
        {
            return crossing.Problem;
        }

        lines.AddRange(crossing.Lines);
        var details = Details(account.AccountNumber, session.TillSessionId, "recipient", draft.ToHolder, draft.Recipient);
        details["signers"] = string.Join(",", confirmed.Order().Select(static id => id.ToString(CultureInfo.InvariantCulture)));
        return await _engine.SubmitAsync(
                actor,
                new PostingDraft(
                    TransactionCodes.CashWithdrawal,
                    TransactionLimitCodes.CashWithdrawal,
                    amount,
                    Narration(draft.Narration),
                    [new AccountLine(account.AccountId, EntrySides.Debit, amount)],
                    lines,
                    CheckSignerKyc: true,
                    details,
                    [new LedgerFloor(till, actor.BranchId, ProblemCodes.TillInsufficientCash)]),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>The people who sign on an account, with their KYC and specimen state, for the withdrawal screen.</summary>
    public async Task<AccountSignersView?> SignersAsync(long accountId, CancellationToken cancellationToken)
    {
        var account = await AccountAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return null;
        }

        var signerIds = MandateRules.Signers(account.Holders).ToList();
        var parties = await _db.Set<Party>()
            .AsNoTracking()
            .Where(row => signerIds.Contains(row.PartyId))
            .Select(row => new { row.PartyId, row.PartyNumber, row.DisplayName })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var statuses = await _kyc.EffectiveStatusesAsync(signerIds, cancellationToken).ConfigureAwait(false);
        var signed = await _signatures.WithEffectiveSignatureAsync(signerIds, cancellationToken).ConfigureAwait(false);

        var signers = parties
            .OrderBy(row => row.DisplayName, StringComparer.Ordinal)
            .Select(row => new AccountSignerView(
                row.PartyId,
                row.PartyNumber,
                row.DisplayName,
                account.Holders.First(holder => holder.PartyId == row.PartyId).RoleCode,
                statuses.GetValueOrDefault(row.PartyId) ?? LookupCodes.KycStatuses.Pending,
                signed.Contains(row.PartyId)))
            .ToList();
        return new AccountSignersView(account.AccountId, account.AccountNumber, account.Mode, signers);
    }

    // ---------------------------------------------------------------- Helpers

    private sealed record AccountRow(long AccountId, string AccountNumber, int BranchId, string? Mode, IReadOnlyList<MandateHolder> Holders);

    private readonly record struct Crossing(IReadOnlyList<LedgerLine> Lines, OperationResult<PostingOutcome>? Problem);

    private async Task<AccountRow?> AccountAsync(long accountId, CancellationToken cancellationToken)
    {
        var row = await _db.Set<Account>()
            .AsNoTracking()
            .Where(item => item.AccountId == accountId)
            .Select(item => new
            {
                item.AccountId,
                item.AccountNumber,
                item.BranchId,
                Mode = item.OperatingModeId == null ? null : item.OperatingMode!.Code,
                Holders = item.Holders.Select(holder => new MandateHolder(holder.PartyId, holder.Role!.Code, holder.Party!.PartyType!.Code)).ToList()
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : new AccountRow(row.AccountId, row.AccountNumber, row.BranchId, row.Mode, row.Holders);
    }

    /// <summary>
    /// When the account is in another branch, the till's branch and the account's branch each balance through their
    /// inter-branch account (plan B 6.1, 6.6): the inter-branch line in the till's branch takes the opposite side of the
    /// till, and the one in the account's branch the till's side.
    /// </summary>
    private async Task<Crossing> CrossBranchAsync(int tillBranchId, int accountBranchId, decimal amount, string tillSide, CancellationToken cancellationToken)
    {
        if (tillBranchId == accountBranchId)
        {
            return new Crossing([], null);
        }

        var settings = await _db.Set<SystemLedgerSetting>()
            .AsNoTracking()
            .Where(row => (row.BranchId == tillBranchId || row.BranchId == accountBranchId) && row.SettingCode == LedgerSettingCodes.InterBranch)
            .ToDictionaryAsync(row => row.BranchId, row => row.LedgerAccountId, cancellationToken)
            .ConfigureAwait(false);
        foreach (var branchId in new[] { tillBranchId, accountBranchId })
        {
            if (!settings.ContainsKey(branchId))
            {
                return new Crossing([], OperationResult.FailWith<PostingOutcome>(
                    ProblemCodes.LedgerSettingMissing,
                    new Dictionary<string, object?>(StringComparer.Ordinal) { ["settingCode"] = LedgerSettingCodes.InterBranch, ["branchId"] = branchId }));
            }
        }

        return new Crossing(
            [
                new LedgerLine(settings[tillBranchId], tillBranchId, PostingRules.Opposite(tillSide), amount),
                new LedgerLine(settings[accountBranchId], accountBranchId, tillSide, amount)
            ],
            null);
    }

    private static Dictionary<string, string[]> Common(long? accountId, decimal? amount, string? narration)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (accountId is null)
        {
            errors["accountId"] = ["Choose the account."];
        }

        if (amount is not { } value || !PostingRules.IsValidAmount(value))
        {
            errors["amount"] = ["Enter an amount above zero with at most 2 decimal places."];
        }

        if (narration is { Length: > 200 })
        {
            errors["narration"] = ["Keep the narration to 200 characters."];
        }

        return errors;
    }

    /// <summary>
    /// Someone other than the holder gives a name and mobile; at or above the cash report threshold, also an ID type
    /// and number (Q3).
    /// </summary>
    private void PersonErrors(Dictionary<string, string[]> errors, string field, bool? isHolder, CashPerson? person, decimal? amount)
    {
        if (isHolder is null)
        {
            errors[field] = ["Say whether the account holder is at the counter."];
            return;
        }

        if (isHolder.Value)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(person?.Name) || person.Name.Trim().Length > NameLength)
        {
            errors[field + ".name"] = ["Enter the person's full name."];
        }

        if (string.IsNullOrWhiteSpace(person?.Mobile) || person.Mobile.Trim().Length > MobileLength)
        {
            errors[field + ".mobile"] = ["Enter the person's mobile number."];
        }

        if (_options.CashTransactionReportThreshold is { } threshold && amount is { } value && value >= threshold)
        {
            if (person?.IdentificationTypeId is null)
            {
                errors[field + ".identificationTypeId"] = ["For this amount, record the person's ID type."];
            }

            if (string.IsNullOrWhiteSpace(person?.IdentificationNumber) || person.IdentificationNumber.Trim().Length > IdNumberLength)
            {
                errors[field + ".identificationNumber"] = ["For this amount, record the person's ID number."];
            }
        }
    }

    private static Dictionary<string, string?> Details(string accountNumber, long tillSessionId, string role, bool? isHolder, CashPerson? person)
    {
        var details = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["accountNumber"] = accountNumber,
            ["tillSessionId"] = tillSessionId.ToString(CultureInfo.InvariantCulture),
            [role + "IsHolder"] = isHolder == true ? "true" : "false"
        };
        if (isHolder != true && person is not null)
        {
            details[role + "Name"] = person.Name?.Trim();
            details[role + "Mobile"] = person.Mobile?.Trim();
            if (person.IdentificationTypeId is { } typeId)
            {
                details[role + "IdentificationTypeId"] = typeId.ToString(CultureInfo.InvariantCulture);
                details[role + "IdentificationNumber"] = person.IdentificationNumber is null ? null : PartyRules.NormalizeIdentificationNumber(person.IdentificationNumber);
            }
        }

        return details;
    }

    private static string? Narration(string? narration) => string.IsNullOrWhiteSpace(narration) ? null : narration.Trim();
}
