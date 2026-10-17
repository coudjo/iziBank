using System.Globalization;
using System.Text.Json;
using Banking.Application.Abstractions;
using Banking.Application.Accounts;
using Banking.Application.Errors;
using Banking.Application.Supervision;
using Banking.Domain.Accounts;
using Banking.Domain.Institution;
using Banking.Domain.Ledger;
using Banking.Domain.Lookups;
using Banking.Domain.Products;
using Banking.Domain.Supervision;
using Banking.Infrastructure.Ledger;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Banking.Infrastructure.Products;

/// <summary>The fields of an approved product that can still change, as stored in a change request.</summary>
public sealed record ProductChange(
    string ProductName,
    long? LedgerControlAccountId,
    decimal? CreditInterestRate,
    decimal? DebitInterestRate,
    decimal? MinimumBalance,
    short? AccountsPerCustomer,
    bool IsChequeBookAllowed,
    bool IsFreezed,
    bool IsActive);

public sealed record PendingProductChange(long ChangeRequestId, ProductChange Proposed, long RequestedByUserId, DateTime RequestedOn);

public sealed record ProductView(
    int ProductId,
    int BranchId,
    string BranchCode,
    string ProductCode,
    string ProductName,
    int ProductTypeId,
    string ProductTypeCode,
    string AccountPrefix,
    int CurrencyId,
    string CurrencyCode,
    long? LedgerControlAccountId,
    string? LedgerControlAccountCode,
    string? LedgerControlAccountName,
    decimal? CreditInterestRate,
    decimal? DebitInterestRate,
    decimal? MinimumBalance,
    short? AccountsPerCustomer,
    bool IsChequeBookAllowed,
    bool IsFreezed,
    bool IsActive,
    string SupervisionState,
    long? CreatedByUserId,
    DateTime CreatedOn,
    long? ModifiedByUserId,
    long? SupervisedByUserId,
    DateTime? SupervisedOn,
    PendingProductChange? PendingChange);

/// <summary>
/// A product as sent by the portal. On an update, null flags keep their current value; on a create they default to
/// cheque book not allowed, not frozen and active.
/// </summary>
public sealed record ProductDraft(
    string? ProductCode,
    string ProductName,
    int? ProductTypeId,
    string? AccountPrefix,
    int? CurrencyId,
    long? LedgerControlAccountId,
    decimal? CreditInterestRate,
    decimal? DebitInterestRate,
    decimal? MinimumBalance,
    short? AccountsPerCustomer,
    bool? IsChequeBookAllowed,
    bool? IsFreezed,
    bool? IsActive);

public sealed record ProductFilter(int? ProductTypeId, string? Search, string? SupervisionState, bool? IsActive, bool OpenableOnly);

public sealed record ProductEditOutcome(bool Applied, long? ChangeRequestId);

/// <summary>
/// A5-01: products (plan 6.8) with maker-checker. Products are institution-wide (D4): any branch's administrator can
/// manage them, and <c>BranchId</c> only records where the product was created. A new product is Pending until a
/// different supervisor approves it. After approval the code, type, prefix and currency never change; other changes
/// wait for approval as a change request, except freezing or deactivating, which apply at once (ADR 0002, A5-a).
/// </summary>
public sealed class ProductAdmin
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly BankingDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IActivityContext _activity;
    private readonly TimeProvider _timeProvider;
    private readonly SupervisionOptions _supervision;
    private readonly AccountNumberOptions _accountNumbers;
    private readonly AccountOptions _accounts;

    public ProductAdmin(
        BankingDbContext db,
        ICurrentUser currentUser,
        IActivityContext activity,
        TimeProvider timeProvider,
        IOptions<SupervisionOptions> supervision,
        IOptions<AccountNumberOptions> accountNumbers,
        IOptions<AccountOptions> accounts)
    {
        ArgumentNullException.ThrowIfNull(supervision);
        ArgumentNullException.ThrowIfNull(accountNumbers);
        ArgumentNullException.ThrowIfNull(accounts);
        _db = db;
        _currentUser = currentUser;
        _activity = activity;
        _timeProvider = timeProvider;
        _supervision = supervision.Value;
        _accountNumbers = accountNumbers.Value;
        _accounts = accounts.Value;
    }

    public async Task<PagedResult<ProductView>> ListAsync(ProductFilter filter, int page, int pageSize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = _db.Set<Product>().AsNoTracking();
        if (filter.ProductTypeId is { } typeId)
        {
            query = query.Where(row => row.ProductTypeId == typeId);
        }

        if (filter.IsActive is { } isActive)
        {
            query = query.Where(row => row.IsActive == isActive);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = "%" + filter.Search.Trim()
                .Replace("[", "[[]", StringComparison.Ordinal)
                .Replace("%", "[%]", StringComparison.Ordinal)
                .Replace("_", "[_]", StringComparison.Ordinal) + "%";
            query = query.Where(row => EF.Functions.Like(row.ProductCode, pattern) || EF.Functions.Like(row.ProductName, pattern));
        }

        switch (filter.SupervisionState?.Trim().ToUpperInvariant())
        {
            case LedgerSupervisionStates.Pending:
                query = query.Where(row => row.IsSupervisionRequired && row.SupervisedByUserId == null);
                break;
            case LedgerSupervisionStates.Approved:
                query = query.Where(row => row.IsSupervisionRequired && row.SupervisedByUserId != null);
                break;
            case LedgerSupervisionStates.NotRequired:
                query = query.Where(row => !row.IsSupervisionRequired);
                break;
        }

        if (filter.OpenableOnly)
        {
            // UX 6.7 step 2: the product picker on account opening (plan 6.9 rule 2).
            var openable = _accounts.DirectOpeningProductTypes.ToList();
            query = query.Where(row => row.IsActive
                                       && row.IsFreezed != true
                                       && (!row.IsSupervisionRequired || row.SupervisedByUserId != null)
                                       && openable.Contains(row.ProductType!.Code));
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await Project(query.OrderBy(row => row.ProductCode).Skip((page - 1) * pageSize).Take(pageSize))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<ProductView>(await WithPendingAsync(rows, cancellationToken).ConfigureAwait(false), page, pageSize, total);
    }

    public async Task<ProductView?> GetAsync(int productId, CancellationToken cancellationToken)
    {
        var rows = await Project(_db.Set<Product>().AsNoTracking().Where(row => row.ProductId == productId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return (await WithPendingAsync(rows, cancellationToken).ConfigureAwait(false)).SingleOrDefault();
    }

    public async Task<OperationResult<int>> CreateAsync(ProductDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var branchId = _currentUser.BranchId ?? throw new InvalidOperationException("Creating a product needs a branch.");
        var code = draft.ProductCode?.Trim().ToUpperInvariant() ?? string.Empty;
        var prefix = draft.AccountPrefix?.Trim() ?? string.Empty;

        var errors = await ValidateAsync(draft, draft.ProductTypeId, draft.CurrencyId, cancellationToken).ConfigureAwait(false);
        if (code.Length is 0 or > 10 || !code.All(static character => char.IsAsciiLetterUpper(character) || char.IsAsciiDigit(character) || character is '-'))
        {
            errors["productCode"] = ["Use 1 to 10 capital letters, digits or dashes."];
        }

        var maxPrefix = _accountNumbers.MaxPrefixLength;
        if (prefix.Length == 0 || prefix.Length > maxPrefix || !prefix.All(char.IsAsciiDigit))
        {
            errors["accountPrefix"] = [string.Create(CultureInfo.InvariantCulture, $"Use 1 to {maxPrefix} digits.")];
        }

        if (errors.Count > 0)
        {
            return OperationResult.Fail<int>(ProblemCodes.Validation, errors);
        }

        if (await _db.Set<Product>().AnyAsync(row => row.ProductCode == code, cancellationToken).ConfigureAwait(false))
        {
            return OperationResult.Fail<int>(ProblemCodes.ProductCodeTaken);
        }

        var product = new Product
        {
            BranchId = branchId,
            ProductCode = code,
            ProductName = draft.ProductName.Trim(),
            ProductTypeId = draft.ProductTypeId!.Value,
            AccountPrefix = prefix,
            CurrencyId = draft.CurrencyId!.Value,
            LedgerControlAccountId = draft.LedgerControlAccountId,
            CreditInterestRate = draft.CreditInterestRate,
            DebitInterestRate = draft.DebitInterestRate,
            MinimumBalance = draft.MinimumBalance,
            AccountsPerCustomer = draft.AccountsPerCustomer,
            IsChequeBookAllowed = draft.IsChequeBookAllowed ?? false,
            IsFreezed = draft.IsFreezed ?? false,
            IsActive = draft.IsActive ?? true
        };
        SupervisionRules.StartNew(product, _supervision.Enabled);
        _db.Set<Product>().Add(product);
        _activity.Record(EventCodes.CreateRecord, nameof(Product), code);

        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            _db.Entry(product).State = EntityState.Detached;
            _activity.Written = false;
            return OperationResult.Fail<int>(ProblemCodes.ProductCodeTaken);
        }

        return OperationResult.Ok(product.ProductId);
    }

    /// <summary>
    /// A Pending product is edited in place. An approved product keeps its code, type, prefix and currency; freezing
    /// or deactivating applies at once, anything else becomes a change request when supervision is on.
    /// </summary>
    public async Task<OperationResult<ProductEditOutcome>> UpdateAsync(int productId, ProductDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var product = await _db.Set<Product>().SingleOrDefaultAsync(row => row.ProductId == productId, cancellationToken).ConfigureAwait(false);
        if (product is null)
        {
            return OperationResult.Fail<ProductEditOutcome>(ProblemCodes.NotFound);
        }

        if (draft.ProductCode is not null && !string.Equals(draft.ProductCode.Trim(), product.ProductCode, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Fail<ProductEditOutcome>(ProblemCodes.ReferenceCodeImmutable);
        }

        var pending = SupervisionRules.StateOf(product) == SupervisionState.Pending;
        var typeId = draft.ProductTypeId ?? product.ProductTypeId;
        var currencyId = draft.CurrencyId ?? product.CurrencyId;
        var prefix = draft.AccountPrefix?.Trim() ?? product.AccountPrefix;

        if (!pending
            && (typeId != product.ProductTypeId || currencyId != product.CurrencyId || !string.Equals(prefix, product.AccountPrefix, StringComparison.Ordinal)))
        {
            return OperationResult.Fail<ProductEditOutcome>(ProblemCodes.ProductFieldLocked);
        }

        var errors = await ValidateAsync(draft, typeId, currencyId, cancellationToken).ConfigureAwait(false);
        var maxPrefix = _accountNumbers.MaxPrefixLength;
        if (prefix.Length == 0 || prefix.Length > maxPrefix || !prefix.All(char.IsAsciiDigit))
        {
            errors["accountPrefix"] = [string.Create(CultureInfo.InvariantCulture, $"Use 1 to {maxPrefix} digits.")];
        }

        if (errors.Count > 0)
        {
            return OperationResult.Fail<ProductEditOutcome>(ProblemCodes.Validation, errors);
        }

        var current = ChangeOf(product);
        var proposed = new ProductChange(
            draft.ProductName.Trim(),
            draft.LedgerControlAccountId,
            draft.CreditInterestRate,
            draft.DebitInterestRate,
            draft.MinimumBalance,
            draft.AccountsPerCustomer,
            draft.IsChequeBookAllowed ?? current.IsChequeBookAllowed,
            draft.IsFreezed ?? current.IsFreezed,
            draft.IsActive ?? current.IsActive);

        if (pending)
        {
            product.ProductTypeId = typeId;
            product.CurrencyId = currencyId;
            product.AccountPrefix = prefix;
            Apply(product, proposed);
            _activity.Record(EventCodes.UpdateRecord, nameof(Product), product.ProductCode);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok(new ProductEditOutcome(Applied: true, ChangeRequestId: null));
        }

        if (proposed == current)
        {
            return OperationResult.Ok(new ProductEditOutcome(Applied: true, ChangeRequestId: null));
        }

        // Freezing or deactivating only ever stops new accounts, so it does not wait for a second person.
        var onlyRestricts = proposed with { IsFreezed = current.IsFreezed, IsActive = current.IsActive } == current
                            && (proposed.IsFreezed || !current.IsFreezed)
                            && (!proposed.IsActive || current.IsActive);

        if (!_supervision.Enabled || !product.IsSupervisionRequired || onlyRestricts)
        {
            Apply(product, proposed);
            _activity.Record(EventCodes.UpdateRecord, nameof(Product), product.ProductCode);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok(new ProductEditOutcome(Applied: true, ChangeRequestId: null));
        }

        if (await PendingChangeQuery(productId).AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return OperationResult.Fail<ProductEditOutcome>(ProblemCodes.ChangePendingExists);
        }

        var request = new ChangeRequest
        {
            EntityType = ChangeRequestEntities.Product,
            EntityId = Key(productId),
            ChangeType = ChangeRequestTypes.ProductUpdate,
            Status = ChangeRequestStatuses.Pending,
            BeforeJson = JsonSerializer.Serialize(current, Json),
            PayloadJson = JsonSerializer.Serialize(proposed, Json),
            RequestedByUserId = _currentUser.StaffUserId ?? throw new InvalidOperationException("A change request needs a signed-in user."),
            RequestedOn = _timeProvider.GetUtcNow().UtcDateTime
        };

        // Only the change request is saved; the live product is untouched until approval.
        _db.Set<ChangeRequest>().Add(request);
        _activity.Record(EventCodes.UpdateRecord, nameof(Product), product.ProductCode, new Dictionary<string, object?> { ["changeRequest"] = true });

        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            _db.Entry(request).State = EntityState.Detached;
            _activity.Written = false;
            return OperationResult.Fail<ProductEditOutcome>(ProblemCodes.ChangePendingExists);
        }

        return OperationResult.Ok(new ProductEditOutcome(Applied: false, request.ChangeRequestId));
    }

    /// <summary>Approves a Pending product (R2), or the pending change of an approved one (four-eyes).</summary>
    public async Task<OperationResult<int>> ApproveAsync(int productId, CancellationToken cancellationToken)
    {
        var approverId = _currentUser.StaffUserId ?? throw new InvalidOperationException("Approval needs a signed-in user.");

        var product = await _db.Set<Product>().SingleOrDefaultAsync(row => row.ProductId == productId, cancellationToken).ConfigureAwait(false);
        if (product is null)
        {
            return OperationResult.Fail<int>(ProblemCodes.NotFound);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (SupervisionRules.StateOf(product) == SupervisionState.Pending)
        {
            var check = SupervisionRules.CheckApprove(product, approverId, hasSupervisePermission: true, _supervision.AllowSelfApproval);
            if (!check.Allowed)
            {
                return OperationResult.Fail<int>(check.ProblemCode!);
            }

            SupervisionRules.Approve(product, approverId, now);
            _activity.Record(EventCodes.SuperviseApprove, nameof(Product), product.ProductCode);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok(productId);
        }

        var request = await PendingChangeQuery(productId).AsTracking().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (request is null)
        {
            return OperationResult.Fail<int>(SupervisionProblems.NotPending);
        }

        if (request.RequestedByUserId == approverId && !_supervision.AllowSelfApproval)
        {
            return OperationResult.Fail<int>(SupervisionProblems.SelfApproval);
        }

        Apply(product, JsonSerializer.Deserialize<ProductChange>(request.PayloadJson, Json)!);
        product.SupervisedByUserId = approverId;
        product.SupervisedOn = now;
        request.Status = ChangeRequestStatuses.Approved;
        request.DecidedByUserId = approverId;
        request.DecidedOn = now;
        _activity.Record(EventCodes.SuperviseApprove, nameof(Product), product.ProductCode, new Dictionary<string, object?> { ["changeRequestId"] = request.ChangeRequestId });
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(productId);
    }

    /// <summary>
    /// Rejects a Pending product (R5: deleted with its rates, provided no account uses it), or the pending change of
    /// an approved product (the change request is closed and the product is untouched).
    /// </summary>
    public async Task<OperationResult<int>> RejectAsync(int productId, string? reason, CancellationToken cancellationToken)
    {
        var deciderId = _currentUser.StaffUserId ?? throw new InvalidOperationException("Rejection needs a signed-in user.");

        var product = await _db.Set<Product>().SingleOrDefaultAsync(row => row.ProductId == productId, cancellationToken).ConfigureAwait(false);
        if (product is null)
        {
            return OperationResult.Fail<int>(ProblemCodes.NotFound);
        }

        if (SupervisionRules.StateOf(product) == SupervisionState.Pending)
        {
            var hasDependents = await _db.Set<Account>().AnyAsync(row => row.ProductId == productId, cancellationToken).ConfigureAwait(false);

            // A Pending product was never approved: an approved product never returns to Pending (A5-a).
            var check = SupervisionRules.CheckReject(product, hasRejectPermission: true, wasEverSupervised: false, hasDependents);
            if (!check.Allowed)
            {
                return OperationResult.Fail<int>(check.ProblemCode!);
            }

            var rates = await _db.Set<ProductInterestRate>().Where(row => row.ProductId == productId).ToListAsync(cancellationToken).ConfigureAwait(false);
            _db.Set<ProductInterestRate>().RemoveRange(rates);
            _db.Set<Product>().Remove(product);
            _activity.Record(EventCodes.SuperviseReject, nameof(Product), product.ProductCode, Note(reason));
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok(productId);
        }

        var request = await PendingChangeQuery(productId).AsTracking().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (request is null)
        {
            return OperationResult.Fail<int>(SupervisionProblems.NotPending);
        }

        request.Status = ChangeRequestStatuses.Rejected;
        request.DecidedByUserId = deciderId;
        request.DecidedOn = _timeProvider.GetUtcNow().UtcDateTime;
        request.DecisionNote = reason is { Length: > 300 } ? reason[..300] : reason;
        _activity.Record(EventCodes.SuperviseReject, nameof(Product), product.ProductCode, new Dictionary<string, object?> { ["changeRequestId"] = request.ChangeRequestId });
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(productId);
    }

    /// <summary>The ledger account type a product's control account must have (plan 6.8).</summary>
    public static string ControlAccountTypeFor(string productTypeCode) =>
        productTypeCode == LookupCodes.ProductTypes.Loan ? LookupCodes.LedgerAccountTypes.Asset : LookupCodes.LedgerAccountTypes.Liability;

    private async Task<Dictionary<string, string[]>> ValidateAsync(ProductDraft draft, int? typeId, int? currencyId, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var name = draft.ProductName?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 100)
        {
            errors["productName"] = ["Enter a name of up to 100 characters."];
        }

        string? typeCode = null;
        if (typeId is { } productTypeId)
        {
            typeCode = await _db.Set<ProductType>()
                .Where(row => row.Id == productTypeId && row.IsActive)
                .Select(row => row.Code)
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (typeCode is null)
        {
            errors["productTypeId"] = ["Choose a product type from the list."];
        }

        var currencyKnown = currencyId is { } id
                            && await _db.Set<Currency>().AnyAsync(row => row.CurrencyId == id, cancellationToken).ConfigureAwait(false);
        if (!currencyKnown)
        {
            errors["currencyId"] = ["Choose a currency from the list."];
        }

        if (draft.LedgerControlAccountId is not { } ledgerId)
        {
            errors["ledgerControlAccountId"] = ["Choose the control account in the chart of accounts."];
        }
        else if (typeCode is not null && currencyKnown)
        {
            var ledger = await _db.Set<LedgerAccount>()
                .AsNoTracking()
                .Where(row => row.LedgerAccountId == ledgerId)
                .Select(row => new
                {
                    TypeCode = row.LedgerAccountTypeId == null ? null : row.LedgerAccountType!.Code,
                    row.CurrencyId,
                    Approved = !row.IsSupervisionRequired || row.SupervisedByUserId != null
                })
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            var expectedType = ControlAccountTypeFor(typeCode);
            if (ledger is null)
            {
                errors["ledgerControlAccountId"] = ["Choose the control account in the chart of accounts."];
            }
            else if (!string.Equals(ledger.TypeCode, expectedType, StringComparison.Ordinal))
            {
                errors["ledgerControlAccountId"] = [expectedType == LookupCodes.LedgerAccountTypes.Asset
                    ? "A loan product needs an asset control account."
                    : "A deposit product needs a liability control account."];
            }
            else if (ledger.CurrencyId != currencyId)
            {
                errors["ledgerControlAccountId"] = ["The control account must be in the product's currency."];
            }
            else if (!ledger.Approved)
            {
                errors["ledgerControlAccountId"] = ["The control account is waiting for approval and cannot be used yet."];
            }
        }

        if (draft.CreditInterestRate is < 0m or > 100m)
        {
            errors["creditInterestRate"] = ["Enter a rate from 0 to 100."];
        }

        if (draft.DebitInterestRate is < 0m or > 100m)
        {
            errors["debitInterestRate"] = ["Enter a rate from 0 to 100."];
        }

        if (draft.MinimumBalance is < 0m)
        {
            errors["minimumBalance"] = ["The minimum balance cannot be negative."];
        }

        if (draft.AccountsPerCustomer is < 0)
        {
            errors["accountsPerCustomer"] = ["Enter 0 for no limit, or the number of accounts allowed."];
        }

        return errors;
    }

    private static ProductChange ChangeOf(Product product) =>
        new(
            product.ProductName,
            product.LedgerControlAccountId,
            product.CreditInterestRate,
            product.DebitInterestRate,
            product.MinimumBalance,
            product.AccountsPerCustomer,
            product.IsChequeBookAllowed ?? false,
            product.IsFreezed ?? false,
            product.IsActive);

    private static void Apply(Product product, ProductChange change)
    {
        product.ProductName = change.ProductName;
        product.LedgerControlAccountId = change.LedgerControlAccountId;
        product.CreditInterestRate = change.CreditInterestRate;
        product.DebitInterestRate = change.DebitInterestRate;
        product.MinimumBalance = change.MinimumBalance;
        product.AccountsPerCustomer = change.AccountsPerCustomer;
        product.IsChequeBookAllowed = change.IsChequeBookAllowed;
        product.IsFreezed = change.IsFreezed;
        product.IsActive = change.IsActive;
    }

    private IQueryable<ChangeRequest> PendingChangeQuery(int productId)
    {
        var key = Key(productId);
        return _db.Set<ChangeRequest>()
            .AsNoTracking()
            .Where(row => row.EntityType == ChangeRequestEntities.Product
                          && row.EntityId == key
                          && row.Status == ChangeRequestStatuses.Pending);
    }

    private async Task<List<ProductView>> WithPendingAsync(List<ProductView> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return rows;
        }

        var keys = rows.Select(row => Key(row.ProductId)).ToList();
        var pending = await _db.Set<ChangeRequest>()
            .AsNoTracking()
            .Where(row => row.EntityType == ChangeRequestEntities.Product
                          && row.Status == ChangeRequestStatuses.Pending
                          && keys.Contains(row.EntityId))
            .Select(row => new { row.EntityId, row.ChangeRequestId, row.PayloadJson, row.RequestedByUserId, row.RequestedOn })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var byKey = pending.ToDictionary(
            row => row.EntityId,
            row => new PendingProductChange(
                row.ChangeRequestId,
                JsonSerializer.Deserialize<ProductChange>(row.PayloadJson, Json)!,
                row.RequestedByUserId,
                row.RequestedOn),
            StringComparer.Ordinal);

        return rows
            .Select(row => byKey.TryGetValue(Key(row.ProductId), out var change) ? row with { PendingChange = change } : row)
            .ToList();
    }

    private static IQueryable<ProductView> Project(IQueryable<Product> query) =>
        query.Select(row => new ProductView(
            row.ProductId,
            row.BranchId,
            row.Branch!.BranchCode,
            row.ProductCode,
            row.ProductName,
            row.ProductTypeId,
            row.ProductType!.Code,
            row.AccountPrefix,
            row.CurrencyId,
            row.Currency!.CurrencyCode,
            row.LedgerControlAccountId,
            row.LedgerControlAccountId == null ? null : row.LedgerControlAccount!.LedgerAccountCode,
            row.LedgerControlAccountId == null ? null : row.LedgerControlAccount!.AccountName,
            row.CreditInterestRate,
            row.DebitInterestRate,
            row.MinimumBalance,
            row.AccountsPerCustomer,
            row.IsChequeBookAllowed == true,
            row.IsFreezed == true,
            row.IsActive,
            !row.IsSupervisionRequired
                ? LedgerSupervisionStates.NotRequired
                : row.SupervisedByUserId == null ? LedgerSupervisionStates.Pending : LedgerSupervisionStates.Approved,
            row.CreatedByUserId,
            row.CreatedOn,
            row.ModifiedByUserId,
            row.SupervisedByUserId,
            row.SupervisedOn,
            null));

    private static Dictionary<string, object?>? Note(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? null : new Dictionary<string, object?> { ["reason"] = reason.Length > 300 ? reason[..300] : reason };

    private static string Key(int productId) => productId.ToString(CultureInfo.InvariantCulture);
}
