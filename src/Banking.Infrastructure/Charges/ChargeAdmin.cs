using System.Globalization;
using Banking.Application.Abstractions;
using Banking.Application.Errors;
using Banking.Application.Supervision;
using Banking.Domain.Charges;
using Banking.Domain.Lookups;
using Banking.Domain.Products;
using Banking.Domain.Supervision;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Banking.Infrastructure.Charges;

public sealed record ChargeCodeView(int ChargeId, string Code, string Description, string CategoryCode, bool IsActive);

public sealed record ChargeCodeDraft(string? Code, string? Description, string? CategoryCode, bool? IsActive);

public sealed record ChargeTierView(
    short TierNumber,
    string BasisCode,
    decimal? TransactionAmount,
    decimal AmountOrPercentage,
    decimal? MinimumCharge,
    decimal? MaximumCharge);

public sealed record ChargeTierDraft(string? BasisCode, decimal? TransactionAmount, decimal? AmountOrPercentage, decimal? MinimumCharge, decimal? MaximumCharge);

public sealed record ProductChargeDraft(
    string? ChargeCode,
    string? ConditionCode,
    int? FreeTransactions,
    string? ShortfallPolicyCode,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo,
    IReadOnlyList<ChargeTierDraft>? Tiers);

public sealed record ProductChargeView(
    long ProductChargeId,
    int ProductId,
    string ChargeCode,
    string ConditionCode,
    int? FreeTransactions,
    string? ShortfallPolicyCode,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string State,
    long? SupervisedByUserId,
    DateTime? SupervisedOn,
    IReadOnlyList<ChargeTierView> Tiers);

/// <summary>
/// B0-03: charge codes (reference data) and each product's charges (plan B 2.4). A product charge is supervised: a new
/// one waits for a different supervisor with <c>ACC/CHARGES</c> supervise. Only a pending charge can be edited or
/// rejected; approving one ends an earlier approved charge of the same kind the day before it starts, as product rates
/// do. Posting uses only approved charges (B5-01).
/// </summary>
public sealed class ChargeAdmin
{
    private readonly BankingDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IActivityContext _activity;
    private readonly TimeProvider _timeProvider;
    private readonly SupervisionOptions _supervision;

    public ChargeAdmin(
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

    // ---------------------------------------------------------------- Charge codes

    public async Task<IReadOnlyList<ChargeCodeView>> ListCodesAsync(CancellationToken cancellationToken) =>
        await _db.Set<ChargeCode>()
            .AsNoTracking()
            .OrderBy(row => row.Code)
            .Select(row => new ChargeCodeView(row.ChargeId, row.Code, row.Description, row.ChargeCategory!.Code, row.IsActive))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<OperationResult<int>> CreateCodeAsync(ChargeCodeDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var code = draft.Code?.Trim().ToUpperInvariant();
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(code) || code.Length > 10)
        {
            errors["code"] = ["Enter a code of up to 10 characters."];
        }

        if (string.IsNullOrWhiteSpace(draft.Description) || draft.Description.Trim().Length > 100)
        {
            errors["description"] = ["Describe the charge, in at most 100 characters."];
        }

        var categoryId = await LookupIdAsync<ChargeCategoryType>(draft.CategoryCode, cancellationToken).ConfigureAwait(false);
        if (categoryId is null)
        {
            errors["categoryCode"] = ["Choose a category."];
        }

        if (errors.Count > 0)
        {
            return OperationResult.Fail<int>(ProblemCodes.Validation, errors);
        }

        if (await _db.Set<ChargeCode>().AnyAsync(row => row.Code == code, cancellationToken).ConfigureAwait(false))
        {
            return OperationResult.Fail<int>(ProblemCodes.ReferenceCodeTaken);
        }

        var charge = new ChargeCode
        {
            Code = code!,
            Description = draft.Description!.Trim(),
            ChargeCategoryId = categoryId!.Value,
            IsActive = draft.IsActive ?? true
        };
        _db.Set<ChargeCode>().Add(charge);
        _activity.Record(EventCodes.UpdateRecord, nameof(ChargeCode), charge.Code);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(charge.ChargeId);
    }

    /// <summary>The code itself is fixed once created; the description, category and active flag can change.</summary>
    public async Task<OperationResult<int>> UpdateCodeAsync(int chargeId, ChargeCodeDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var charge = await _db.Set<ChargeCode>().SingleOrDefaultAsync(row => row.ChargeId == chargeId, cancellationToken).ConfigureAwait(false);
        if (charge is null)
        {
            return OperationResult.Fail<int>(ProblemCodes.NotFound);
        }

        if (draft.Code is not null && !string.Equals(draft.Code.Trim(), charge.Code, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Fail<int>(
                ProblemCodes.Validation,
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["code"] = ["A charge code cannot be changed."] });
        }

        if (string.IsNullOrWhiteSpace(draft.Description) || draft.Description.Trim().Length > 100)
        {
            return OperationResult.Fail<int>(
                ProblemCodes.Validation,
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["description"] = ["Describe the charge, in at most 100 characters."] });
        }

        var categoryId = await LookupIdAsync<ChargeCategoryType>(draft.CategoryCode, cancellationToken).ConfigureAwait(false);
        if (categoryId is null)
        {
            return OperationResult.Fail<int>(
                ProblemCodes.Validation,
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["categoryCode"] = ["Choose a category."] });
        }

        charge.Description = draft.Description.Trim();
        charge.ChargeCategoryId = categoryId.Value;
        charge.IsActive = draft.IsActive ?? charge.IsActive;
        _activity.Record(EventCodes.UpdateRecord, nameof(ChargeCode), charge.Code);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(chargeId);
    }

    // ---------------------------------------------------------------- Product charges

    public async Task<IReadOnlyList<ProductChargeView>?> ListProductChargesAsync(int productId, CancellationToken cancellationToken)
    {
        if (!await _db.Set<Product>().AnyAsync(row => row.ProductId == productId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var rows = await _db.Set<ProductCharge>()
            .AsNoTracking()
            .Where(row => row.ProductId == productId)
            .OrderBy(row => row.ChargeCode!.Code)
            .ThenBy(row => row.ChargeCondition!.Code)
            .ThenBy(row => row.EffectiveFrom)
            .Select(row => new
            {
                Row = row,
                ChargeCode = row.ChargeCode!.Code,
                ConditionCode = row.ChargeCondition!.Code,
                PolicyCode = row.ShortfallPolicy == null ? null : row.ShortfallPolicy.Code,
                Tiers = row.Tiers.OrderBy(tier => tier.TierNumber).Select(tier => new
                {
                    tier.TierNumber,
                    BasisCode = tier.ChargeBasis!.Code,
                    tier.TransactionAmount,
                    tier.AmountOrPercentage,
                    tier.MinimumCharge,
                    tier.MaximumCharge
                }).ToList()
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(item => new ProductChargeView(
            item.Row.ProductChargeId,
            item.Row.ProductId,
            item.ChargeCode,
            item.ConditionCode,
            item.Row.FreeTransactions,
            item.PolicyCode,
            item.Row.EffectiveFrom,
            item.Row.EffectiveTo,
            SupervisionRules.StateOf(item.Row) == SupervisionState.Pending ? "PENDING" : "SUPERVISED",
            item.Row.SupervisedByUserId,
            item.Row.SupervisedOn,
            item.Tiers.Select(tier => new ChargeTierView(tier.TierNumber, tier.BasisCode, tier.TransactionAmount, tier.AmountOrPercentage, tier.MinimumCharge, tier.MaximumCharge)).ToList()))
            .ToList();
    }

    public async Task<OperationResult<long>> CreateProductChargeAsync(int productId, ProductChargeDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (!await _db.Set<Product>().AnyAsync(row => row.ProductId == productId, cancellationToken).ConfigureAwait(false))
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        var prepared = await PrepareAsync(draft, cancellationToken).ConfigureAwait(false);
        if (prepared.Errors is not null)
        {
            return OperationResult.Fail<long>(ProblemCodes.Validation, prepared.Errors);
        }

        var charge = new ProductCharge
        {
            ProductId = productId,
            ChargeId = prepared.ChargeId!.Value,
            ChargeConditionId = prepared.ConditionId!.Value,
            FreeTransactions = prepared.FreeTransactions,
            ShortfallPolicyId = prepared.ShortfallPolicyId,
            EffectiveFrom = prepared.EffectiveFrom!.Value,
            EffectiveTo = prepared.EffectiveTo
        };
        AddTiers(charge, prepared.Tiers!);
        SupervisionRules.StartNew(charge, _supervision.Enabled);
        _db.Set<ProductCharge>().Add(charge);
        _activity.Record(EventCodes.UpdateRecord, nameof(ProductCharge), Key(productId));
        if (!await TrySaveAsync(cancellationToken).ConfigureAwait(false))
        {
            return StartsSameDay();
        }

        return OperationResult.Ok(charge.ProductChargeId);
    }

    /// <summary>Changes a charge that is still waiting for approval. An approved charge is replaced by a new one instead.</summary>
    public async Task<OperationResult<long>> UpdatePendingChargeAsync(int productId, long productChargeId, ProductChargeDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var charge = await _db.Set<ProductCharge>()
            .Include(row => row.Tiers)
            .SingleOrDefaultAsync(row => row.ProductChargeId == productChargeId && row.ProductId == productId, cancellationToken)
            .ConfigureAwait(false);
        if (charge is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        if (SupervisionRules.StateOf(charge) != SupervisionState.Pending)
        {
            return OperationResult.Fail<long>(SupervisionProblems.NotPending);
        }

        var prepared = await PrepareAsync(draft, cancellationToken).ConfigureAwait(false);
        if (prepared.Errors is not null)
        {
            return OperationResult.Fail<long>(ProblemCodes.Validation, prepared.Errors);
        }

        // The old bands are deleted and saved before the new ones are added: they share the (charge, tier number) keys.
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        _db.Set<ProductChargeTier>().RemoveRange(charge.Tiers);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        charge.Tiers.Clear();

        charge.ChargeId = prepared.ChargeId!.Value;
        charge.ChargeConditionId = prepared.ConditionId!.Value;
        charge.FreeTransactions = prepared.FreeTransactions;
        charge.ShortfallPolicyId = prepared.ShortfallPolicyId;
        charge.EffectiveFrom = prepared.EffectiveFrom!.Value;
        charge.EffectiveTo = prepared.EffectiveTo;
        AddTiers(charge, prepared.Tiers!);
        _activity.Record(EventCodes.UpdateRecord, nameof(ProductCharge), Key(productId));
        if (!await TrySaveAsync(cancellationToken).ConfigureAwait(false))
        {
            return StartsSameDay();
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(productChargeId);
    }

    /// <summary>
    /// Approves a pending charge (a different person, with supervise). An earlier approved charge of the same charge and
    /// condition that is still open ends the day before this one starts; an approved charge that overlaps it refuses.
    /// </summary>
    public async Task<OperationResult<long>> ApproveAsync(int productId, long productChargeId, CancellationToken cancellationToken)
    {
        var approverId = _currentUser.StaffUserId ?? throw new InvalidOperationException("Approval needs a signed-in user.");
        var charge = await _db.Set<ProductCharge>()
            .SingleOrDefaultAsync(row => row.ProductChargeId == productChargeId && row.ProductId == productId, cancellationToken)
            .ConfigureAwait(false);
        if (charge is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        var check = SupervisionRules.CheckApprove(charge, approverId, hasSupervisePermission: true, _supervision.AllowSelfApproval);
        if (!check.Allowed)
        {
            return OperationResult.Fail<long>(check.ProblemCode!);
        }

        var sameKind = await _db.Set<ProductCharge>()
            .Where(row => row.ProductId == productId
                          && row.ChargeId == charge.ChargeId
                          && row.ChargeConditionId == charge.ChargeConditionId
                          && row.ProductChargeId != charge.ProductChargeId
                          && row.SupervisedByUserId != null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var earlier = sameKind.Where(row => row.EffectiveFrom < charge.EffectiveFrom && (row.EffectiveTo is null || row.EffectiveTo >= charge.EffectiveFrom)).ToList();
        if (sameKind.Any(row => row.EffectiveFrom >= charge.EffectiveFrom && (charge.EffectiveTo is null || row.EffectiveFrom <= charge.EffectiveTo)))
        {
            return OperationResult.Fail<long>(
                ProblemCodes.Validation,
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["effectiveFrom"] = ["An approved charge of this kind already starts within these dates."] });
        }

        foreach (var row in earlier)
        {
            row.EffectiveTo = charge.EffectiveFrom.AddDays(-1);
        }

        SupervisionRules.Approve(charge, approverId, now);
        _activity.Record(EventCodes.SuperviseApprove, nameof(ProductCharge), Key(productId));
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(productChargeId);
    }

    /// <summary>Rejects a pending charge: it was never approved, so it is removed with its bands.</summary>
    public async Task<OperationResult<long>> RejectAsync(int productId, long productChargeId, string? reason, CancellationToken cancellationToken)
    {
        var charge = await _db.Set<ProductCharge>()
            .Include(row => row.Tiers)
            .SingleOrDefaultAsync(row => row.ProductChargeId == productChargeId && row.ProductId == productId, cancellationToken)
            .ConfigureAwait(false);
        if (charge is null)
        {
            return OperationResult.Fail<long>(ProblemCodes.NotFound);
        }

        var check = SupervisionRules.CheckReject(charge, hasRejectPermission: true, wasEverSupervised: false, hasDependents: false);
        if (!check.Allowed)
        {
            return OperationResult.Fail<long>(check.ProblemCode!);
        }

        _db.Set<ProductChargeTier>().RemoveRange(charge.Tiers);
        _db.Set<ProductCharge>().Remove(charge);
        _activity.Record(EventCodes.SuperviseReject, nameof(ProductCharge), Key(productId));
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(productChargeId);
    }

    // ---------------------------------------------------------------- Helpers

    private sealed record Prepared(
        Dictionary<string, string[]>? Errors,
        int? ChargeId,
        int? ConditionId,
        int? ShortfallPolicyId,
        short? FreeTransactions,
        DateOnly? EffectiveFrom,
        DateOnly? EffectiveTo,
        IReadOnlyList<(string BasisCode, ChargeTierDraft Draft)>? Tiers);

    private async Task<Prepared> PrepareAsync(ProductChargeDraft draft, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var chargeCode = draft.ChargeCode?.Trim().ToUpperInvariant();
        var charge = string.IsNullOrEmpty(chargeCode)
            ? null
            : await _db.Set<ChargeCode>().AsNoTracking().Where(row => row.Code == chargeCode && row.IsActive)
                .Select(row => new { row.ChargeId, Category = row.ChargeCategory!.Code })
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (charge is null)
        {
            errors["chargeCode"] = ["Choose an active charge."];
        }

        var conditionCode = draft.ConditionCode?.Trim().ToUpperInvariant();
        var conditionId = await LookupIdAsync<ChargeConditionType>(conditionCode, cancellationToken).ConfigureAwait(false);
        if (conditionId is null)
        {
            errors["conditionCode"] = ["Choose when the charge applies."];
        }
        else if (charge is not null && ChargeConditionRules.CategoryFor(conditionCode!) != charge.Category)
        {
            errors["conditionCode"] = ["This charge does not apply to that condition."];
        }

        if (draft.FreeTransactions is { } free && (free < 0 || free > 1000))
        {
            errors["freeTransactions"] = ["Free transactions per month must be between 0 and 1000."];
        }

        int? policyId = null;
        if (!string.IsNullOrWhiteSpace(draft.ShortfallPolicyCode))
        {
            policyId = await LookupIdAsync<ChargeShortfallPolicyType>(draft.ShortfallPolicyCode.Trim().ToUpperInvariant(), cancellationToken).ConfigureAwait(false);
            if (policyId is null)
            {
                errors["shortfallPolicyCode"] = ["Choose a shortfall policy, or leave it empty to use the institution's."];
            }
        }

        if (draft.EffectiveFrom is null)
        {
            errors["effectiveFrom"] = ["Enter the date the charge starts."];
        }
        else if (draft.EffectiveTo is { } to && to < draft.EffectiveFrom)
        {
            errors["effectiveTo"] = ["The end date cannot be before the start date."];
        }

        var tiers = draft.Tiers ?? [];
        var inputs = tiers.Select(tier => new ChargeTierInput(tier.BasisCode?.Trim().ToUpperInvariant(), tier.TransactionAmount, tier.AmountOrPercentage, tier.MinimumCharge, tier.MaximumCharge)).ToList();
        var tierProblems = ChargeTierRules.Validate(inputs);
        if (tierProblems.Count > 0)
        {
            errors["tiers"] = [.. tierProblems];
        }

        if (errors.Count > 0)
        {
            return new Prepared(errors, null, null, null, null, null, null, null);
        }

        var bands = new List<(string BasisCode, ChargeTierDraft Draft)>(tiers.Count);
        for (var index = 0; index < tiers.Count; index++)
        {
            bands.Add((inputs[index].BasisCode!, tiers[index]));
        }

        return new Prepared(
            null,
            charge!.ChargeId,
            conditionId,
            policyId,
            draft.FreeTransactions is { } freeCount ? (short)freeCount : null,
            draft.EffectiveFrom,
            draft.EffectiveTo,
            bands);
    }

    /// <summary>Adds the bands in order, with the basis resolved to its id.</summary>
    private void AddTiers(ProductCharge charge, IReadOnlyList<(string BasisCode, ChargeTierDraft Draft)> tiers)
    {
        var basisIds = _db.Set<ChargeBasisType>().AsNoTracking().ToDictionary(row => row.Code, row => row.Id, StringComparer.Ordinal);
        for (var index = 0; index < tiers.Count; index++)
        {
            var (basisCode, tier) = tiers[index];
            charge.Tiers.Add(new ProductChargeTier
            {
                TierNumber = (short)(index + 1),
                ChargeBasisId = basisIds[basisCode],
                TransactionAmount = tier.TransactionAmount,
                AmountOrPercentage = tier.AmountOrPercentage!.Value,
                MinimumCharge = tier.MinimumCharge,
                MaximumCharge = tier.MaximumCharge
            });
        }
    }

    private async Task<int?> LookupIdAsync<TLookup>(string? code, CancellationToken cancellationToken)
        where TLookup : Lookup
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var wanted = code.Trim().ToUpperInvariant();
        return await _db.Set<TLookup>()
            .AsNoTracking()
            .Where(row => row.Code == wanted && row.IsActive)
            .Select(row => (int?)row.Id)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Saves; false when the unique key (product, charge, condition, start date) is already taken.</summary>
    private async Task<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }

    private static OperationResult<long> StartsSameDay() =>
        OperationResult.Fail<long>(
            ProblemCodes.Validation,
            new Dictionary<string, string[]>(StringComparer.Ordinal) { ["effectiveFrom"] = ["This product already has this charge for this condition starting on that date."] });

    private static string Key(long id) => id.ToString(CultureInfo.InvariantCulture);
}
