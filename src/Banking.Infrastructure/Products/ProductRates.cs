using System.Globalization;
using Banking.Application.Abstractions;
using Banking.Application.Errors;
using Banking.Domain.Lookups;
using Banking.Domain.Products;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Banking.Infrastructure.Products;

public static class RateStatuses
{
    public const string Scheduled = "SCHEDULED";
    public const string Current = "CURRENT";
    public const string Ended = "ENDED";
}

public sealed record ProductRateView(
    long ProductInterestRateId,
    int RateTypeId,
    string RateTypeCode,
    decimal? MinimumAmount,
    decimal? MaximumAmount,
    decimal InterestRate,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string Status,
    bool IsStarted);

public sealed record ProductRateDraft(int? RateTypeId, decimal? MinimumAmount, decimal? MaximumAmount, decimal? InterestRate, DateOnly? EffectiveFrom, DateOnly? EffectiveTo);

/// <summary>What adding a rate did: the new row, and the open-ended row it closed the day before, if any.</summary>
public sealed record ProductRateAdded(long ProductInterestRateId, long? ClosedRateId, DateOnly? ClosedOn);

/// <summary>
/// A5-02: effective-dated interest rates (plan 6.8). Rows of one product, rate type and overlapping amount band may not
/// overlap in time. A row that has started (<c>EffectiveFrom</c> on or before today) is never edited in place: it can
/// only be closed. Adding a rate closes the open-ended row of the same type and band the day before the new one starts.
/// Changes take the product row lock so two administrators cannot create an overlap together (ADR 0002, A5-b).
/// </summary>
public sealed class ProductRates
{
    private readonly BankingDbContext _db;
    private readonly IActivityContext _activity;
    private readonly TimeProvider _timeProvider;

    public ProductRates(BankingDbContext db, IActivityContext activity, TimeProvider timeProvider)
    {
        _db = db;
        _activity = activity;
        _timeProvider = timeProvider;
    }

    private DateOnly Today => DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

    public async Task<IReadOnlyList<ProductRateView>?> ListAsync(int productId, CancellationToken cancellationToken)
    {
        if (!await _db.Set<Product>().AnyAsync(row => row.ProductId == productId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var today = Today;
        var rows = await _db.Set<ProductInterestRate>()
            .AsNoTracking()
            .Where(row => row.ProductId == productId)
            .OrderBy(row => row.RateType!.Code)
            .ThenBy(row => row.MinimumAmount)
            .ThenBy(row => row.EffectiveFrom)
            .Select(row => new { Row = row, RateTypeCode = row.RateType!.Code })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(item => View(item.Row, item.RateTypeCode, today)).ToList();
    }

    public async Task<OperationResult<ProductRateAdded>> AddAsync(int productId, ProductRateDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var errors = await ValidateAsync(draft, cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return OperationResult.Fail<ProductRateAdded>(ProblemCodes.Validation, errors);
        }

        return await InProductLockAsync(productId, async product =>
        {
            var rate = new ProductInterestRate
            {
                ProductId = productId,
                RateTypeId = draft.RateTypeId,
                MinimumAmount = draft.MinimumAmount,
                MaximumAmount = draft.MaximumAmount,
                InterestRate = draft.InterestRate!.Value,
                EffectiveFrom = draft.EffectiveFrom!.Value,
                EffectiveTo = draft.EffectiveTo
            };

            var others = await RatesOfTypeAsync(productId, draft.RateTypeId!.Value, cancellationToken).ConfigureAwait(false);

            // Plan 6.8: to change a rate, the old row ends the day before the new one starts.
            var previous = others
                .Where(row => row.EffectiveTo is null
                              && row.EffectiveFrom < rate.EffectiveFrom
                              && row.MinimumAmount == rate.MinimumAmount
                              && row.MaximumAmount == rate.MaximumAmount)
                .MaxBy(row => row.EffectiveFrom);
            if (previous is not null)
            {
                previous.EffectiveTo = rate.EffectiveFrom.AddDays(-1);
            }

            if (others.Any(row => Overlaps(row, rate)))
            {
                return OperationResult.Fail<ProductRateAdded>(ProblemCodes.ProductRateOverlap);
            }

            _db.Set<ProductInterestRate>().Add(rate);
            _activity.Record(EventCodes.UpdateRecord, nameof(Product), product.ProductCode, new Dictionary<string, object?> { ["interestRate"] = "added" });
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok(new ProductRateAdded(rate.ProductInterestRateId, previous?.ProductInterestRateId, previous?.EffectiveTo));
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Changes a rate that has not started yet.</summary>
    public async Task<OperationResult<long>> UpdateAsync(int productId, long rateId, ProductRateDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var errors = await ValidateAsync(draft, cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return OperationResult.Fail<long>(ProblemCodes.Validation, errors);
        }

        return await InProductLockAsync(productId, async product =>
        {
            var rate = await _db.Set<ProductInterestRate>()
                .SingleOrDefaultAsync(row => row.ProductInterestRateId == rateId && row.ProductId == productId, cancellationToken)
                .ConfigureAwait(false);
            if (rate is null)
            {
                return OperationResult.Fail<long>(ProblemCodes.NotFound);
            }

            if (rate.EffectiveFrom <= Today)
            {
                return OperationResult.Fail<long>(ProblemCodes.ProductRateStarted);
            }

            rate.RateTypeId = draft.RateTypeId;
            rate.MinimumAmount = draft.MinimumAmount;
            rate.MaximumAmount = draft.MaximumAmount;
            rate.InterestRate = draft.InterestRate!.Value;
            rate.EffectiveFrom = draft.EffectiveFrom!.Value;
            rate.EffectiveTo = draft.EffectiveTo;

            var others = (await RatesOfTypeAsync(productId, draft.RateTypeId!.Value, cancellationToken).ConfigureAwait(false))
                .Where(row => row.ProductInterestRateId != rateId);
            if (others.Any(row => Overlaps(row, rate)))
            {
                return OperationResult.Fail<long>(ProblemCodes.ProductRateOverlap);
            }

            _activity.Record(EventCodes.UpdateRecord, nameof(Product), product.ProductCode, new Dictionary<string, object?> { ["interestRate"] = "changed" });
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok(rateId);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Ends a rate on <paramref name="effectiveTo"/>. Only brings the end forward, never earlier than yesterday or
    /// before the rate starts.
    /// </summary>
    public async Task<OperationResult<long>> CloseAsync(int productId, long rateId, DateOnly? effectiveTo, CancellationToken cancellationToken) =>
        await InProductLockAsync(productId, async product =>
        {
            var rate = await _db.Set<ProductInterestRate>()
                .SingleOrDefaultAsync(row => row.ProductInterestRateId == rateId && row.ProductId == productId, cancellationToken)
                .ConfigureAwait(false);
            if (rate is null)
            {
                return OperationResult.Fail<long>(ProblemCodes.NotFound);
            }

            var earliest = Today.AddDays(-1) > rate.EffectiveFrom ? Today.AddDays(-1) : rate.EffectiveFrom;
            if (effectiveTo is not { } end || end < earliest || (rate.EffectiveTo is { } current && end > current))
            {
                var message = rate.EffectiveTo is { } existing
                    ? string.Create(CultureInfo.InvariantCulture, $"Choose a date from {earliest:yyyy-MM-dd} to {existing:yyyy-MM-dd}.")
                    : string.Create(CultureInfo.InvariantCulture, $"Choose a date on or after {earliest:yyyy-MM-dd}.");
                return OperationResult.Fail<long>(
                    ProblemCodes.Validation,
                    new Dictionary<string, string[]>(StringComparer.Ordinal) { ["effectiveTo"] = [message] });
            }

            rate.EffectiveTo = end;
            _activity.Record(EventCodes.UpdateRecord, nameof(Product), product.ProductCode, new Dictionary<string, object?> { ["interestRate"] = "closed" });
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok(rateId);
        }, cancellationToken).ConfigureAwait(false);

    /// <summary>Removes a rate that has not started yet.</summary>
    public async Task<OperationResult<long>> DeleteAsync(int productId, long rateId, CancellationToken cancellationToken) =>
        await InProductLockAsync(productId, async product =>
        {
            var rate = await _db.Set<ProductInterestRate>()
                .SingleOrDefaultAsync(row => row.ProductInterestRateId == rateId && row.ProductId == productId, cancellationToken)
                .ConfigureAwait(false);
            if (rate is null)
            {
                return OperationResult.Fail<long>(ProblemCodes.NotFound);
            }

            if (rate.EffectiveFrom <= Today)
            {
                return OperationResult.Fail<long>(ProblemCodes.ProductRateStarted);
            }

            _db.Set<ProductInterestRate>().Remove(rate);
            _activity.Record(EventCodes.UpdateRecord, nameof(Product), product.ProductCode, new Dictionary<string, object?> { ["interestRate"] = "removed" });
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok(rateId);
        }, cancellationToken).ConfigureAwait(false);

    /// <summary>Two rows overlap when their amount bands and their date ranges both overlap (open ends are unbounded).</summary>
    public static bool Overlaps(ProductInterestRate first, ProductInterestRate second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        var bandsOverlap = (first.MinimumAmount ?? decimal.MinValue) <= (second.MaximumAmount ?? decimal.MaxValue)
                           && (second.MinimumAmount ?? decimal.MinValue) <= (first.MaximumAmount ?? decimal.MaxValue);
        var datesOverlap = first.EffectiveFrom <= (second.EffectiveTo ?? DateOnly.MaxValue)
                           && second.EffectiveFrom <= (first.EffectiveTo ?? DateOnly.MaxValue);
        return bandsOverlap && datesOverlap;
    }

    private async Task<OperationResult<T>> InProductLockAsync<T>(int productId, Func<Product, Task<OperationResult<T>>> work, CancellationToken cancellationToken)
    {
        var transaction = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            await _db.Database
                .ExecuteSqlInterpolatedAsync($"SELECT [ProductId] FROM [Product] WITH (UPDLOCK, HOLDLOCK) WHERE [ProductId] = {productId}", cancellationToken)
                .ConfigureAwait(false);

            var product = await _db.Set<Product>().AsNoTracking().SingleOrDefaultAsync(row => row.ProductId == productId, cancellationToken).ConfigureAwait(false);
            if (product is null)
            {
                return OperationResult.Fail<T>(ProblemCodes.NotFound);
            }

            var result = await work(product).ConfigureAwait(false);
            if (result.Succeeded)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _db.ChangeTracker.Clear();
            }

            return result;
        }
    }

    private Task<List<ProductInterestRate>> RatesOfTypeAsync(int productId, int rateTypeId, CancellationToken cancellationToken) =>
        _db.Set<ProductInterestRate>()
            .Where(row => row.ProductId == productId && row.RateTypeId == rateTypeId)
            .ToListAsync(cancellationToken);

    private async Task<Dictionary<string, string[]>> ValidateAsync(ProductRateDraft draft, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (draft.RateTypeId is not { } rateTypeId
            || !await _db.Set<RateType>().AnyAsync(row => row.Id == rateTypeId && row.IsActive, cancellationToken).ConfigureAwait(false))
        {
            errors["rateTypeId"] = ["Choose credit or debit."];
        }

        if (draft.InterestRate is not (>= 0m and <= 100m))
        {
            errors["interestRate"] = ["Enter a rate from 0 to 100."];
        }

        if (draft.MinimumAmount is < 0m)
        {
            errors["minimumAmount"] = ["The amount cannot be negative."];
        }

        if (draft.MaximumAmount is < 0m)
        {
            errors["maximumAmount"] = ["The amount cannot be negative."];
        }
        else if (draft is { MinimumAmount: { } minimum, MaximumAmount: { } maximum } && minimum > maximum)
        {
            errors["maximumAmount"] = ["The upper amount must not be below the lower amount."];
        }

        if (draft.EffectiveFrom is not { } from)
        {
            errors["effectiveFrom"] = ["Enter the date the rate starts."];
        }
        else if (from < Today)
        {
            errors["effectiveFrom"] = ["A new rate cannot start in the past."];
        }
        else if (draft.EffectiveTo is { } to && to < from)
        {
            errors["effectiveTo"] = ["The end date must not be before the start date."];
        }

        return errors;
    }

    private static ProductRateView View(ProductInterestRate row, string rateTypeCode, DateOnly today)
    {
        var status = row.EffectiveFrom > today
            ? RateStatuses.Scheduled
            : row.EffectiveTo is { } end && end < today ? RateStatuses.Ended : RateStatuses.Current;
        return new ProductRateView(
            row.ProductInterestRateId,
            row.RateTypeId ?? 0,
            rateTypeCode,
            row.MinimumAmount,
            row.MaximumAmount,
            row.InterestRate,
            row.EffectiveFrom,
            row.EffectiveTo,
            status,
            row.EffectiveFrom <= today);
    }
}
