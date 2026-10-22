using System.Globalization;
using System.Text.Json;
using Banking.Application.Abstractions;
using Banking.Application.Errors;
using Banking.Application.Supervision;
using Banking.Domain.Institution;
using Banking.Domain.Ledger;
using Banking.Domain.Lookups;
using Banking.Domain.Products;
using Banking.Domain.Supervision;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Banking.Infrastructure.Ledger;

/// <summary>One requested setting.</summary>
public sealed record LedgerSettingValue(string SettingCode, long LedgerAccountId);

/// <summary>One setting as shown: what it is for, which account types it accepts, and the account set now.</summary>
public sealed record LedgerSettingView(
    string SettingCode,
    string Description,
    IReadOnlyList<string> AllowedTypes,
    long? LedgerAccountId,
    string? LedgerAccountCode,
    string? AccountName);

public sealed record PendingLedgerSettings(
    long ChangeRequestId,
    IReadOnlyList<LedgerSettingValue> Proposed,
    long RequestedByUserId,
    DateTime RequestedOn);

public sealed record LedgerSettingsView(IReadOnlyList<LedgerSettingView> Settings, PendingLedgerSettings? Pending);

/// <summary>
/// B0-02: the ledger accounts a branch (vault, inter-branch, cash short and over, withholding tax, suspense) and a
/// product (interest expense and payable, charge income) post to (plan B 2.2). A change replaces the whole set and
/// always waits for a different approver with <c>LED/SETTINGS</c> supervise, whatever <c>Supervision:Enabled</c> says,
/// because these accounts decide where money goes.
/// </summary>
public sealed class LedgerSettingsAdmin
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly BankingDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IActivityContext _activity;
    private readonly TimeProvider _timeProvider;
    private readonly SupervisionOptions _supervision;

    public LedgerSettingsAdmin(
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

    // ---------------------------------------------------------------- Reads

    public async Task<LedgerSettingsView?> BranchAsync(int branchId, CancellationToken cancellationToken)
    {
        if (!await _db.Set<Branch>().AnyAsync(row => row.BranchId == branchId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var current = await _db.Set<SystemLedgerSetting>()
            .AsNoTracking()
            .Where(row => row.BranchId == branchId)
            .Select(row => new Current(row.SettingCode, row.LedgerAccountId, row.LedgerAccount!.LedgerAccountCode, row.LedgerAccount.AccountName))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return await ViewAsync(LedgerSettingCodes.Branch, current, ChangeRequestEntities.BranchLedgerSettings, Key(branchId), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<LedgerSettingsView?> ProductAsync(int productId, CancellationToken cancellationToken)
    {
        if (!await _db.Set<Product>().AnyAsync(row => row.ProductId == productId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var current = await _db.Set<ProductLedgerSetting>()
            .AsNoTracking()
            .Where(row => row.ProductId == productId)
            .Select(row => new Current(row.SettingCode, row.LedgerAccountId, row.LedgerAccount!.LedgerAccountCode, row.LedgerAccount.AccountName))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return await ViewAsync(LedgerSettingCodes.Product, current, ChangeRequestEntities.ProductLedgerSettings, Key(productId), cancellationToken)
            .ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- Requests

    public async Task<OperationResult<long>> RequestBranchAsync(int branchId, IReadOnlyList<LedgerSettingValue> values, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (!await _db.Set<Branch>().AnyAsync(row => row.BranchId == branchId, cancellationToken).ConfigureAwait(false))
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        var currencyId = await _db.Set<Currency>()
            .Where(row => row.IsLocalCurrency)
            .Select(row => (int?)row.CurrencyId)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var normalized = Normalize(values);
        var errors = await ValidateAsync(LedgerSettingCodes.Branch, normalized, requiredBranchId: branchId, currencyId, cancellationToken)
            .ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return OperationResult.Fail<long>(ProblemCodes.Validation, errors);
        }

        var before = await _db.Set<SystemLedgerSetting>()
            .AsNoTracking()
            .Where(row => row.BranchId == branchId)
            .OrderBy(row => row.SettingCode)
            .Select(row => new LedgerSettingValue(row.SettingCode, row.LedgerAccountId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return await RequestAsync(
                ChangeRequestEntities.BranchLedgerSettings,
                Key(branchId),
                ChangeRequestTypes.BranchLedgerSettingsReplace,
                before,
                normalized,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OperationResult<long>> RequestProductAsync(int productId, IReadOnlyList<LedgerSettingValue> values, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(values);
        var product = await _db.Set<Product>()
            .AsNoTracking()
            .Where(row => row.ProductId == productId)
            .Select(row => new { row.CurrencyId })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (product is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        var normalized = Normalize(values);
        var errors = await ValidateAsync(LedgerSettingCodes.Product, normalized, requiredBranchId: null, product.CurrencyId, cancellationToken)
            .ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return OperationResult.Fail<long>(ProblemCodes.Validation, errors);
        }

        var before = await _db.Set<ProductLedgerSetting>()
            .AsNoTracking()
            .Where(row => row.ProductId == productId)
            .OrderBy(row => row.SettingCode)
            .Select(row => new LedgerSettingValue(row.SettingCode, row.LedgerAccountId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return await RequestAsync(
                ChangeRequestEntities.ProductLedgerSettings,
                Key(productId),
                ChangeRequestTypes.ProductLedgerSettingsReplace,
                before,
                normalized,
                cancellationToken)
            .ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- Decisions

    public Task<OperationResult<long>> ApproveBranchAsync(int branchId, CancellationToken cancellationToken) =>
        ApproveAsync(ChangeRequestEntities.BranchLedgerSettings, branchId, cancellationToken);

    public Task<OperationResult<long>> ApproveProductAsync(int productId, CancellationToken cancellationToken) =>
        ApproveAsync(ChangeRequestEntities.ProductLedgerSettings, productId, cancellationToken);

    public Task<OperationResult<long>> RejectBranchAsync(int branchId, string? reason, CancellationToken cancellationToken) =>
        RejectAsync(ChangeRequestEntities.BranchLedgerSettings, branchId, reason, cancellationToken);

    public Task<OperationResult<long>> RejectProductAsync(int productId, string? reason, CancellationToken cancellationToken) =>
        RejectAsync(ChangeRequestEntities.ProductLedgerSettings, productId, reason, cancellationToken);

    private async Task<OperationResult<long>> ApproveAsync(string entityType, int id, CancellationToken cancellationToken)
    {
        var approverId = _currentUser.StaffUserId ?? throw new InvalidOperationException("Approval needs a signed-in user.");
        var request = await PendingAsync(entityType, id, cancellationToken).ConfigureAwait(false);
        if (request is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.ChangeNotPending);
        }

        if (request.RequestedByUserId == approverId && !_supervision.AllowSelfApproval)
        {
            return OperationResult.Fail<long>(SupervisionProblems.SelfApproval);
        }

        var values = JsonSerializer.Deserialize<List<LedgerSettingValue>>(request.PayloadJson, Json)!;
        if (entityType == ChangeRequestEntities.BranchLedgerSettings)
        {
            var existing = await _db.Set<SystemLedgerSetting>().Where(row => row.BranchId == id).ToListAsync(cancellationToken).ConfigureAwait(false);
            _db.Set<SystemLedgerSetting>().RemoveRange(existing.Where(row => values.TrueForAll(value => value.SettingCode != row.SettingCode)));
            foreach (var value in values)
            {
                var row = existing.Find(item => item.SettingCode == value.SettingCode);
                if (row is null)
                {
                    _db.Set<SystemLedgerSetting>().Add(new SystemLedgerSetting { BranchId = id, SettingCode = value.SettingCode, LedgerAccountId = value.LedgerAccountId });
                }
                else
                {
                    row.LedgerAccountId = value.LedgerAccountId;
                }
            }
        }
        else
        {
            var existing = await _db.Set<ProductLedgerSetting>().Where(row => row.ProductId == id).ToListAsync(cancellationToken).ConfigureAwait(false);
            _db.Set<ProductLedgerSetting>().RemoveRange(existing.Where(row => values.TrueForAll(value => value.SettingCode != row.SettingCode)));
            foreach (var value in values)
            {
                var row = existing.Find(item => item.SettingCode == value.SettingCode);
                if (row is null)
                {
                    _db.Set<ProductLedgerSetting>().Add(new ProductLedgerSetting { ProductId = id, SettingCode = value.SettingCode, LedgerAccountId = value.LedgerAccountId });
                }
                else
                {
                    row.LedgerAccountId = value.LedgerAccountId;
                }
            }
        }

        request.Status = ChangeRequestStatuses.Approved;
        request.DecidedByUserId = approverId;
        request.DecidedOn = _timeProvider.GetUtcNow().UtcDateTime;
        _activity.Record(EventCodes.SuperviseApprove, entityType, Key(id));
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(request.ChangeRequestId);
    }

    private async Task<OperationResult<long>> RejectAsync(string entityType, int id, string? reason, CancellationToken cancellationToken)
    {
        var request = await PendingAsync(entityType, id, cancellationToken).ConfigureAwait(false);
        if (request is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.ChangeNotPending);
        }

        request.Status = ChangeRequestStatuses.Rejected;
        request.DecidedByUserId = _currentUser.StaffUserId;
        request.DecidedOn = _timeProvider.GetUtcNow().UtcDateTime;
        request.DecisionNote = reason is { Length: > 300 } ? reason[..300] : reason;
        _activity.Record(EventCodes.SuperviseReject, entityType, Key(id));
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(request.ChangeRequestId);
    }

    // ---------------------------------------------------------------- Helpers

    private sealed record Current(string SettingCode, long LedgerAccountId, string LedgerAccountCode, string AccountName);

    private async Task<LedgerSettingsView> ViewAsync(
        IReadOnlyList<LedgerSettingDefinition> definitions,
        IReadOnlyList<Current> current,
        string entityType,
        string entityId,
        CancellationToken cancellationToken)
    {
        var settings = definitions
            .Select(definition =>
            {
                var row = current.FirstOrDefault(item => item.SettingCode == definition.Code);
                return new LedgerSettingView(definition.Code, definition.Description, definition.AllowedTypes, row?.LedgerAccountId, row?.LedgerAccountCode, row?.AccountName);
            })
            .ToList();

        var pending = await _db.Set<ChangeRequest>()
            .AsNoTracking()
            .Where(row => row.EntityType == entityType && row.EntityId == entityId && row.Status == ChangeRequestStatuses.Pending)
            .Select(row => new { row.ChangeRequestId, row.PayloadJson, row.RequestedByUserId, row.RequestedOn })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return new LedgerSettingsView(
            settings,
            pending is null
                ? null
                : new PendingLedgerSettings(
                    pending.ChangeRequestId,
                    JsonSerializer.Deserialize<List<LedgerSettingValue>>(pending.PayloadJson, Json)!,
                    pending.RequestedByUserId,
                    pending.RequestedOn));
    }

    private static List<LedgerSettingValue> Normalize(IReadOnlyList<LedgerSettingValue> values) =>
        values
            .Select(value => value with { SettingCode = (value.SettingCode ?? string.Empty).Trim().ToUpperInvariant() })
            .OrderBy(value => value.SettingCode, StringComparer.Ordinal)
            .ToList();

    /// <summary>Known codes, once each; approved accounts of an allowed type, in the right branch and currency.</summary>
    private async Task<Dictionary<string, string[]>> ValidateAsync(
        IReadOnlyList<LedgerSettingDefinition> definitions,
        List<LedgerSettingValue> values,
        int? requiredBranchId,
        int? currencyId,
        CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        if (values.Select(value => value.SettingCode).Distinct(StringComparer.Ordinal).Count() != values.Count)
        {
            problems.Add("Each setting can appear only once.");
        }

        var ids = values.Select(value => value.LedgerAccountId).Distinct().ToList();
        var accounts = await _db.Set<LedgerAccount>()
            .AsNoTracking()
            .Where(row => ids.Contains(row.LedgerAccountId))
            .Select(row => new
            {
                Account = row,
                TypeCode = row.LedgerAccountTypeId == null ? null : row.LedgerAccountType!.Code
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var value in values)
        {
            var definition = definitions.FirstOrDefault(item => item.Code == value.SettingCode);
            if (definition is null)
            {
                problems.Add($"Unknown setting {value.SettingCode}.");
                continue;
            }

            var found = accounts.Find(row => row.Account.LedgerAccountId == value.LedgerAccountId);
            if (found is null)
            {
                problems.Add($"{value.SettingCode}: ledger account {value.LedgerAccountId} does not exist.");
                continue;
            }

            if (!SupervisionRules.CheckUsable(found.Account).Allowed)
            {
                problems.Add($"{value.SettingCode}: {found.Account.LedgerAccountCode} is still waiting for approval.");
            }

            if (found.TypeCode is null || !definition.AllowedTypes.Contains(found.TypeCode))
            {
                problems.Add($"{value.SettingCode}: use a ledger account of type {string.Join(" or ", definition.AllowedTypes)}.");
            }

            if (requiredBranchId is { } branchId && found.Account.BranchId != branchId)
            {
                problems.Add($"{value.SettingCode}: {found.Account.LedgerAccountCode} belongs to another branch.");
            }

            if (found.Account.CurrencyId is { } accountCurrency && currencyId is { } wanted && accountCurrency != wanted)
            {
                problems.Add($"{value.SettingCode}: {found.Account.LedgerAccountCode} is in another currency.");
            }
        }

        return problems.Count == 0
            ? new Dictionary<string, string[]>(StringComparer.Ordinal)
            : new Dictionary<string, string[]>(StringComparer.Ordinal) { ["settings"] = [.. problems] };
    }

    private async Task<OperationResult<long>> RequestAsync(
        string entityType,
        string entityId,
        string changeType,
        object before,
        object proposed,
        CancellationToken cancellationToken)
    {
        var pending = await _db.Set<ChangeRequest>()
            .AnyAsync(row => row.EntityType == entityType && row.EntityId == entityId && row.Status == ChangeRequestStatuses.Pending, cancellationToken)
            .ConfigureAwait(false);
        if (pending)
        {
            return OperationResult.Fail<long>(ProblemCodes.ChangePendingExists);
        }

        var request = new ChangeRequest
        {
            EntityType = entityType,
            EntityId = entityId,
            ChangeType = changeType,
            Status = ChangeRequestStatuses.Pending,
            BeforeJson = JsonSerializer.Serialize(before, Json),
            PayloadJson = JsonSerializer.Serialize(proposed, Json),
            RequestedByUserId = _currentUser.StaffUserId ?? throw new InvalidOperationException("A change request needs a signed-in user."),
            RequestedOn = _timeProvider.GetUtcNow().UtcDateTime
        };
        _db.Set<ChangeRequest>().Add(request);
        _activity.Record(EventCodes.UpdateRecord, entityType, entityId);

        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // The filtered unique index caught a concurrent request for the same record.
            _db.Entry(request).State = EntityState.Detached;
            return OperationResult.Fail<long>(ProblemCodes.ChangePendingExists);
        }

        return OperationResult.Ok(request.ChangeRequestId);
    }

    private Task<ChangeRequest?> PendingAsync(string entityType, int id, CancellationToken cancellationToken)
    {
        var entityId = Key(id);
        return _db.Set<ChangeRequest>()
            .SingleOrDefaultAsync(row => row.EntityType == entityType && row.EntityId == entityId && row.Status == ChangeRequestStatuses.Pending, cancellationToken);
    }

    private static string Key(int id) => id.ToString(CultureInfo.InvariantCulture);
}
