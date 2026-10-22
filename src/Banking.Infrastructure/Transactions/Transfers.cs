using System.Globalization;
using Banking.Application.Abstractions;
using Banking.Application.Errors;
using Banking.Domain.Accounts;
using Banking.Domain.Ledger;
using Banking.Domain.Lookups;
using Banking.Domain.Security;
using Banking.Domain.Transactions;
using Banking.Infrastructure.Charges;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Banking.Infrastructure.Transactions;

/// <summary>A transfer as the teller enters it. The signers are those who authorised it at the counter.</summary>
public sealed record TransferDraft(
    long? FromAccountId,
    long? ToAccountId,
    decimal? Amount,
    string? Narration,
    IReadOnlyList<long>? ConfirmedSignerPartyIds);

/// <summary>
/// B3-01 and B3-02: account-to-account transfers (plan B 6.5, 6.6). The source account is in the teller's working
/// branch; the destination may be in any branch, in which case the lines run through each branch's
/// <c>INTER_BRANCH</c> account. The signers confirmed must satisfy the source account's mandate (6.3).
/// </summary>
public sealed class Transfers
{
    private readonly BankingDbContext _db;
    private readonly PostingEngine _engine;
    private readonly ICurrentUser _currentUser;
    private readonly ChargeQuoter _quoter;

    public Transfers(BankingDbContext db, PostingEngine engine, ICurrentUser currentUser, ChargeQuoter quoter)
    {
        _db = db;
        _engine = engine;
        _currentUser = currentUser;
        _quoter = quoter;
    }

    public async Task<OperationResult<PostingOutcome>> TransferAsync(TransferDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var actor = new PostingActor(
            _currentUser.StaffUserId ?? throw new InvalidOperationException("A transfer needs a signed-in user."),
            _currentUser.BranchId ?? throw new InvalidOperationException("A transfer needs a working branch."));

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (draft.FromAccountId is null)
        {
            errors["fromAccountId"] = ["Choose the account to pay from."];
        }

        if (draft.ToAccountId is null)
        {
            errors["toAccountId"] = ["Choose the account to pay into."];
        }
        else if (draft.ToAccountId == draft.FromAccountId)
        {
            errors["toAccountId"] = ["Choose a different account to pay into."];
        }

        if (draft.Amount is not { } amount || !PostingRules.IsValidAmount(amount))
        {
            errors["amount"] = ["Enter an amount above zero with at most 2 decimal places."];
        }

        if (draft.Narration is { Length: > 200 })
        {
            errors["narration"] = ["Keep the narration to 200 characters."];
        }

        var confirmed = (draft.ConfirmedSignerPartyIds ?? []).Distinct().ToList();
        if (confirmed.Count == 0)
        {
            errors["confirmedSignerPartyIds"] = ["Confirm who signed for this transfer."];
        }

        if (errors.Count > 0)
        {
            return OperationResult.Fail<PostingOutcome>(ProblemCodes.Validation, errors);
        }

        var ids = new[] { draft.FromAccountId!.Value, draft.ToAccountId!.Value };
        var accounts = await _db.Set<Account>()
            .AsNoTracking()
            .Where(row => ids.Contains(row.AccountId))
            .Select(row => new
            {
                row.AccountId,
                row.AccountNumber,
                row.BranchId,
                Mode = row.OperatingModeId == null ? null : row.OperatingMode!.Code,
                Holders = row.Holders.Select(holder => new MandateHolder(holder.PartyId, holder.Role!.Code, holder.Party!.PartyType!.Code)).ToList()
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var from = accounts.Find(row => row.AccountId == ids[0]);
        var to = accounts.Find(row => row.AccountId == ids[1]);
        if (from is null || to is null)
        {
            return OperationResult.Fail<PostingOutcome>(ProblemCodes.NotFound);
        }

        if (from.BranchId != actor.BranchId)
        {
            return OperationResult.Fail<PostingOutcome>(ProblemCodes.RecordOtherBranch);
        }

        var signers = MandateRules.Signers(from.Holders);
        if (confirmed.Exists(id => !signers.Contains(id)))
        {
            return OperationResult.Fail<PostingOutcome>(
                ProblemCodes.Validation,
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["confirmedSignerPartyIds"] = ["Only people who sign on this account can authorise it."] });
        }

        if (!MandateRules.IsSatisfied(from.Mode, signers, confirmed))
        {
            return OperationResult.Fail<PostingOutcome>(ProblemCodes.TransactionMandateNotConfirmed);
        }

        var value = draft.Amount!.Value;
        var ledger = new List<LedgerLine>();
        if (from.BranchId != to.BranchId)
        {
            var settings = await _db.Set<SystemLedgerSetting>()
                .AsNoTracking()
                .Where(row => (row.BranchId == from.BranchId || row.BranchId == to.BranchId) && row.SettingCode == LedgerSettingCodes.InterBranch)
                .ToDictionaryAsync(row => row.BranchId, row => row.LedgerAccountId, cancellationToken)
                .ConfigureAwait(false);
            foreach (var branchId in new[] { from.BranchId, to.BranchId })
            {
                if (!settings.ContainsKey(branchId))
                {
                    return OperationResult.FailWith<PostingOutcome>(
                        ProblemCodes.LedgerSettingMissing,
                        new Dictionary<string, object?>(StringComparer.Ordinal) { ["settingCode"] = LedgerSettingCodes.InterBranch, ["branchId"] = branchId });
                }
            }

            ledger.Add(new LedgerLine(settings[from.BranchId], from.BranchId, EntrySides.Credit, value));
            ledger.Add(new LedgerLine(settings[to.BranchId], to.BranchId, EntrySides.Debit, value));
        }

        // The source account pays any per-transfer charge on top of the amount moved (B5-01).
        var charge = await _quoter.QuoteAsync(from.AccountId, LookupCodes.ChargeConditions.PerTransferOut, value, cancellationToken).ConfigureAwait(false);
        if (charge.ProblemCode is not null)
        {
            return OperationResult.Fail<PostingOutcome>(charge.ProblemCode);
        }

        var accountLines = new List<AccountLine> { new(from.AccountId, EntrySides.Debit, value), new(to.AccountId, EntrySides.Credit, value) };
        if (charge.IncomeLine is { } income)
        {
            accountLines.Add(new AccountLine(from.AccountId, EntrySides.Debit, charge.Amount));
            ledger.Add(income);
        }

        var posting = new PostingDraft(
            from.BranchId == to.BranchId ? TransactionCodes.Transfer : TransactionCodes.TransferAcrossBranches,
            TransactionLimitCodes.Transfer,
            value,
            string.IsNullOrWhiteSpace(draft.Narration) ? null : draft.Narration.Trim(),
            accountLines,
            ledger,
            CheckSignerKyc: true,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["fromAccountNumber"] = from.AccountNumber,
                ["toAccountNumber"] = to.AccountNumber,
                ["signers"] = string.Join(",", confirmed.Order().Select(static id => id.ToString(CultureInfo.InvariantCulture)))
            });

        return await _engine.SubmitAsync(actor, posting, cancellationToken).ConfigureAwait(false);
    }
}
