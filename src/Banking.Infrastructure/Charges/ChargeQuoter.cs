using Banking.Application.Abstractions;
using Banking.Application.Charges;
using Banking.Application.Errors;
using Banking.Domain.Accounts;
using Banking.Domain.Charges;
using Banking.Domain.Ledger;
using Banking.Domain.Lookups;
using Banking.Domain.Supervision;
using Banking.Domain.Transactions;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Banking.Infrastructure.Charges;

/// <summary>
/// What a per-transaction charge comes to on one account, and the lines that book it.
/// </summary>
/// <param name="ProductChargeId">The approved product charge that applies, or null when none does.</param>
/// <param name="ChargeCode">The charge code of that product charge.</param>
/// <param name="Amount">The amount to debit to the account now; zero when waived, written off or none applies.</param>
/// <param name="Arrear">The part the balance could not cover that is kept to be collected later (DEFER).</param>
/// <param name="AllowShortfall">True when the charge may take the balance below zero (CHARGE_FULL).</param>
/// <param name="Waived">True when the free-transaction allowance for the month covers this transaction.</param>
/// <param name="IncomeLine">The credit to the product's charge income account in the account's branch, when Amount is above zero.</param>
/// <param name="ProblemCode">Set when the charge cannot be booked, for example when no charge income account is set up.</param>
public sealed record ChargeQuote(
    long? ProductChargeId,
    string? ChargeCode,
    decimal Amount,
    decimal Arrear,
    bool AllowShortfall,
    bool Waived,
    LedgerLine? IncomeLine,
    string? ProblemCode)
{
    /// <summary>The arrear to record on the posting, for the account it is charged to.</summary>
    public IReadOnlyList<ArrearRaise>? ArrearRaises(long accountId) =>
        Arrear > 0m && ProductChargeId is { } id ? [new ArrearRaise(accountId, id, Arrear)] : null;
}

/// <summary>Plan B 6.8, B5-01 and B5-02: works out a per-transaction charge and the lines that book it. Read-only.</summary>
public sealed class ChargeQuoter
{
    private readonly BankingDbContext _db;
    private readonly IWorkingDateProvider _workingDates;
    private readonly IOptions<ChargeOptions> _options;

    public ChargeQuoter(BankingDbContext db, IWorkingDateProvider workingDates, IOptions<ChargeOptions> options)
    {
        _db = db;
        _workingDates = workingDates;
        _options = options;
    }

    /// <summary>
    /// The charge for one transaction of the given condition. Exempt accounts (B-16) and products without an approved
    /// charge on the working date pay nothing. The shortfall policy decides what the charge takes when the balance,
    /// after the principal, does not cover it (B-17).
    /// </summary>
    public async Task<ChargeQuote> QuoteAsync(long accountId, string conditionCode, decimal amount, CancellationToken cancellationToken)
    {
        var account = await _db.Set<Account>()
            .AsNoTracking()
            .Where(row => row.AccountId == accountId)
            .Select(row => new { row.BranchId, row.ProductId })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (account is null)
        {
            return Nothing(ProblemCodes.NotFound);
        }

        if (amount <= 0m || await ExemptAsync(accountId, cancellationToken).ConfigureAwait(false))
        {
            return Nothing(null);
        }

        var working = await _workingDates.GetAsync(account.BranchId, cancellationToken).ConfigureAwait(false);
        var charges = await _db.Set<ProductCharge>()
            .AsNoTracking()
            .Include(row => row.ChargeCode)
            .Include(row => row.ChargeCondition)
            .Include(row => row.ShortfallPolicy)
            .Include(row => row.Tiers)
            .ThenInclude(row => row.ChargeBasis)
            .Where(row => row.ProductId == account.ProductId
                && row.ChargeCondition!.Code == conditionCode
                && row.EffectiveFrom <= working
                && (row.EffectiveTo == null || row.EffectiveTo >= working))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var charge = charges.FirstOrDefault(row => SupervisionRules.StateOf(row) != SupervisionState.Pending);
        if (charge is null)
        {
            return Nothing(null);
        }

        var bands = charge.Tiers
            .OrderBy(row => row.TierNumber)
            .Select(row => new ChargeBand(row.ChargeBasis!.Code, row.TransactionAmount, row.AmountOrPercentage, row.MinimumCharge, row.MaximumCharge))
            .ToList();
        var fee = ChargeCalculator.Calculate(bands, amount);
        var earlier = charge.FreeTransactions is > 0 ? await CountThisMonthAsync(accountId, conditionCode, working, cancellationToken).ConfigureAwait(false) : 0;
        var waived = ChargeCalculator.IsWaived(charge.FreeTransactions, earlier);
        if (waived || fee <= 0m)
        {
            return new ChargeQuote(charge.ProductChargeId, charge.ChargeCode!.Code, 0m, 0m, false, waived, null, null);
        }

        // What the balance can give the charge is what is left after the principal (B-17).
        var available = await _db.Set<AccountBalance>()
            .AsNoTracking()
            .Where(row => row.AccountId == accountId)
            .Select(row => row.AvailableBalance)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var policy = ChargeShortfallRules.Resolve(charge.ShortfallPolicy?.Code, _options.Value.ShortfallPolicy);
        var outcome = ChargeShortfallRules.Apply(policy, fee, available - amount);
        var code = charge.ChargeCode!.Code;
        if (outcome.Charged <= 0m)
        {
            return new ChargeQuote(charge.ProductChargeId, code, 0m, outcome.Arrear, false, false, null, null);
        }

        var incomeAccountId = await IncomeAccountAsync(account.ProductId, cancellationToken).ConfigureAwait(false);
        if (incomeAccountId is null)
        {
            return new ChargeQuote(charge.ProductChargeId, code, 0m, 0m, false, false, null, ProblemCodes.LedgerSettingMissing);
        }

        return new ChargeQuote(
            charge.ProductChargeId,
            code,
            outcome.Charged,
            outcome.Arrear,
            policy == LookupCodes.ChargeShortfallPolicies.ChargeFull,
            false,
            new LedgerLine(incomeAccountId.Value, account.BranchId, EntrySides.Credit, outcome.Charged),
            null);
    }

    private static ChargeQuote Nothing(string? problemCode) => new(null, null, 0m, 0m, false, false, null, problemCode);

    private async Task<bool> ExemptAsync(long accountId, CancellationToken cancellationToken)
    {
        var exempt = await _db.Set<AccountCondition>()
            .AsNoTracking()
            .Where(row => row.AccountId == accountId)
            .Select(row => (bool?)row.DoNotApplyTransactionCharges)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return exempt == true;
    }

    private async Task<long?> IncomeAccountAsync(int productId, CancellationToken cancellationToken) =>
        await _db.Set<ProductLedgerSetting>()
            .AsNoTracking()
            .Where(row => row.ProductId == productId && row.SettingCode == LedgerSettingCodes.ChargeIncome)
            .Select(row => (long?)row.LedgerAccountId)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Counts the account's debits of the same kind earlier this month, one per posted transaction. A transaction in
    /// which the account is also credited (an arrear collected from a transfer in) is not a withdrawal and is not counted.
    /// </summary>
    private async Task<int> CountThisMonthAsync(long accountId, string conditionCode, DateOnly working, CancellationToken cancellationToken)
    {
        string[] codes = conditionCode == LookupCodes.ChargeConditions.PerTransferOut
            ? [TransactionCodes.Transfer, TransactionCodes.TransferAcrossBranches]
            : [TransactionCodes.CashWithdrawal];
        var codeIds = await _db.Set<TransactionCode>()
            .AsNoTracking()
            .Where(row => codes.Contains(row.Code))
            .Select(row => row.TransactionCodeId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var monthStart = new DateOnly(working.Year, working.Month, 1);
        return await _db.Set<AccountLedgerEntry>()
            .AsNoTracking()
            .Where(row => row.AccountId == accountId
                && row.CrDb == EntrySides.Debit
                && row.EntryDate >= monthStart
                && row.EntryDate <= working
                && codeIds.Contains(row.TransactionCodeId)
                && !_db.Set<AccountLedgerEntry>().Any(credit => credit.PostedTransactionId == row.PostedTransactionId
                    && credit.AccountId == accountId
                    && credit.CrDb == EntrySides.Credit))
            .Select(row => row.PostedTransactionId)
            .Distinct()
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
