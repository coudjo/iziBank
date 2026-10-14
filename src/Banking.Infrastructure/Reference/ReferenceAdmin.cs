using System.Globalization;
using Banking.Application.Abstractions;
using Banking.Application.Errors;
using Banking.Domain.Institution;
using Banking.Domain.Lookups;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Banking.Infrastructure.Reference;

public sealed record InstitutionProfileView(
    string InstitutionName,
    string? LicenseNumber,
    int? RegulatoryCategoryId,
    string? RegulatoryCategoryCode,
    DateOnly? IncorporationDate,
    string? HeadOfficeAddress,
    bool IsComplete);

public sealed record InstitutionProfileChanges(
    string InstitutionName,
    string? LicenseNumber,
    int? RegulatoryCategoryId,
    DateOnly? IncorporationDate,
    string? HeadOfficeAddress);

public sealed record BranchView(
    int BranchId,
    string BranchCode,
    string BranchName,
    string? OperatingLicenseNo,
    short? ClearingDays,
    bool IsActive);

public sealed record BranchDraft(string? BranchCode, string BranchName, string? OperatingLicenseNo, short? ClearingDays, bool IsActive);

public sealed record CurrencyView(int CurrencyId, string CurrencyCode, string CurrencyName, decimal? ExchangeRate, bool IsLocalCurrency);

public sealed record CurrencyDraft(string? CurrencyCode, string CurrencyName, decimal? ExchangeRate, bool IsLocalCurrency);

public sealed record HolidayView(int HolidayId, DateOnly HolidayDate, string? Remarks);

/// <summary>
/// A3-01: institution profile (a singleton that is never created or deleted through the API), branches (code
/// immutable), currencies (exactly one local currency at all times) and defined holidays.
/// </summary>
public sealed class ReferenceAdmin
{
    private const int InstitutionProfileId = 1;

    private readonly BankingDbContext _db;
    private readonly IActivityContext _activity;
    private readonly TimeProvider _timeProvider;

    public ReferenceAdmin(BankingDbContext db, IActivityContext activity, TimeProvider timeProvider)
    {
        _db = db;
        _activity = activity;
        _timeProvider = timeProvider;
    }

    // ---------------------------------------------------------------- Institution profile

    public async Task<InstitutionProfileView?> GetProfileAsync(CancellationToken cancellationToken) =>
        await _db.Set<InstitutionProfile>()
            .AsNoTracking()
            .Where(row => row.InstitutionProfileId == InstitutionProfileId)
            .Select(row => new InstitutionProfileView(
                row.InstitutionName,
                row.LicenseNumber,
                row.RegulatoryCategoryId,
                row.RegulatoryCategoryId == null ? null : row.RegulatoryCategory!.Code,
                row.IncorporationDate,
                row.HeadOfficeAddress,
                row.InstitutionName != string.Empty && row.LicenseNumber != null && row.RegulatoryCategoryId != null))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<OperationResult<int>> UpdateProfileAsync(InstitutionProfileChanges changes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var profile = await _db.Set<InstitutionProfile>()
            .SingleOrDefaultAsync(row => row.InstitutionProfileId == InstitutionProfileId, cancellationToken)
            .ConfigureAwait(false);
        if (profile is null)
        {
            // The Migrator creates the row; the API never does (A3-01).
            return OperationResult.Fail<int>(ProblemCodes.NotFound);
        }

        var name = changes.InstitutionName.Trim();
        var license = Blank(changes.LicenseNumber);
        var address = Blank(changes.HeadOfficeAddress);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (name.Length is 0 or > 150)
        {
            errors["institutionName"] = ["Enter a name of up to 150 characters."];
        }

        if (license is { Length: > 30 })
        {
            errors["licenseNumber"] = ["Keep the licence number to 30 characters."];
        }

        if (address is { Length: > 300 })
        {
            errors["headOfficeAddress"] = ["Keep the address to 300 characters."];
        }

        if (changes.IncorporationDate is { } incorporated && incorporated > Today())
        {
            errors["incorporationDate"] = ["The incorporation date cannot be in the future."];
        }

        if (changes.RegulatoryCategoryId is { } categoryId
            && !await _db.Set<RegulatoryCategoryType>().AnyAsync(row => row.Id == categoryId && row.IsActive, cancellationToken).ConfigureAwait(false))
        {
            errors["regulatoryCategoryId"] = ["Choose a regulatory category from the list."];
        }

        if (errors.Count > 0)
        {
            return OperationResult.Fail<int>(ProblemCodes.Validation, errors);
        }

        profile.InstitutionName = name;
        profile.LicenseNumber = license;
        profile.RegulatoryCategoryId = changes.RegulatoryCategoryId;
        profile.IncorporationDate = changes.IncorporationDate;
        profile.HeadOfficeAddress = address;

        _activity.Record(EventCodes.UpdateRecord, nameof(InstitutionProfile), "1");
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(InstitutionProfileId);
    }

    // ---------------------------------------------------------------- Branches

    public async Task<IReadOnlyList<BranchView>> ListBranchesAsync(bool includeInactive, CancellationToken cancellationToken) =>
        await _db.Set<Branch>()
            .AsNoTracking()
            .Where(row => includeInactive || row.IsActive)
            .OrderBy(row => row.BranchCode)
            .Select(row => new BranchView(row.BranchId, row.BranchCode, row.BranchName, row.OperatingLicenseNo, row.ClearingDays, row.IsActive))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<BranchView?> GetBranchAsync(int branchId, CancellationToken cancellationToken) =>
        await _db.Set<Branch>()
            .AsNoTracking()
            .Where(row => row.BranchId == branchId)
            .Select(row => new BranchView(row.BranchId, row.BranchCode, row.BranchName, row.OperatingLicenseNo, row.ClearingDays, row.IsActive))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<OperationResult<int>> CreateBranchAsync(BranchDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var code = draft.BranchCode?.Trim().ToUpperInvariant() ?? string.Empty;
        var errors = ValidateBranch(draft);
        if (code.Length is 0 or > 10 || !code.All(static character => char.IsAsciiLetterUpper(character) || char.IsAsciiDigit(character)))
        {
            errors["branchCode"] = ["Use 1 to 10 capital letters or digits."];
        }

        if (errors.Count > 0)
        {
            return OperationResult.Fail<int>(ProblemCodes.Validation, errors);
        }

        if (await _db.Set<Branch>().AnyAsync(row => row.BranchCode == code, cancellationToken).ConfigureAwait(false))
        {
            return OperationResult.Fail<int>(ProblemCodes.ReferenceCodeTaken);
        }

        var branch = new Branch
        {
            BranchCode = code,
            BranchName = draft.BranchName.Trim(),
            OperatingLicenseNo = Blank(draft.OperatingLicenseNo),
            ClearingDays = draft.ClearingDays,
            IsActive = draft.IsActive
        };
        _db.Set<Branch>().Add(branch);
        _activity.Record(EventCodes.CreateRecord, nameof(Branch), code);

        return await SaveUniqueAsync(branch, () => branch.BranchId, ProblemCodes.ReferenceCodeTaken, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationResult<int>> UpdateBranchAsync(int branchId, BranchDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var branch = await _db.Set<Branch>().SingleOrDefaultAsync(row => row.BranchId == branchId, cancellationToken).ConfigureAwait(false);
        if (branch is null)
        {
            return OperationResult.Fail<int>(ProblemCodes.NotFound);
        }

        if (draft.BranchCode is not null && !string.Equals(draft.BranchCode.Trim(), branch.BranchCode, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Fail<int>(ProblemCodes.ReferenceCodeImmutable);
        }

        var errors = ValidateBranch(draft);
        if (errors.Count > 0)
        {
            return OperationResult.Fail<int>(ProblemCodes.Validation, errors);
        }

        branch.BranchName = draft.BranchName.Trim();
        branch.OperatingLicenseNo = Blank(draft.OperatingLicenseNo);
        branch.ClearingDays = draft.ClearingDays;
        branch.IsActive = draft.IsActive;

        _activity.Record(EventCodes.UpdateRecord, nameof(Branch), branch.BranchCode);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(branchId);
    }

    // ---------------------------------------------------------------- Currencies

    public async Task<IReadOnlyList<CurrencyView>> ListCurrenciesAsync(CancellationToken cancellationToken) =>
        await _db.Set<Currency>()
            .AsNoTracking()
            .OrderByDescending(row => row.IsLocalCurrency)
            .ThenBy(row => row.CurrencyCode)
            .Select(row => new CurrencyView(row.CurrencyId, row.CurrencyCode, row.CurrencyName, row.ExchangeRate, row.IsLocalCurrency))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<OperationResult<int>> CreateCurrencyAsync(CurrencyDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var code = draft.CurrencyCode?.Trim().ToUpperInvariant() ?? string.Empty;
        var errors = ValidateCurrency(draft);
        if (code.Length != 3 || !code.All(char.IsAsciiLetterUpper))
        {
            errors["currencyCode"] = ["Use the three-letter ISO code, for example USD."];
        }

        if (errors.Count > 0)
        {
            return OperationResult.Fail<int>(ProblemCodes.Validation, errors);
        }

        if (await _db.Set<Currency>().AnyAsync(row => row.CurrencyCode == code, cancellationToken).ConfigureAwait(false))
        {
            return OperationResult.Fail<int>(ProblemCodes.ReferenceCodeTaken);
        }

        var currency = new Currency
        {
            CurrencyCode = code,
            CurrencyName = draft.CurrencyName.Trim(),
            ExchangeRate = draft.IsLocalCurrency ? 1m : draft.ExchangeRate,
            IsLocalCurrency = false
        };
        _activity.Record(EventCodes.CreateRecord, nameof(Currency), code);

        var transaction = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            _db.Set<Currency>().Add(currency);
            var saved = await SaveUniqueAsync(currency, () => currency.CurrencyId, ProblemCodes.ReferenceCodeTaken, cancellationToken).ConfigureAwait(false);
            if (!saved.Succeeded)
            {
                return saved;
            }

            if (draft.IsLocalCurrency)
            {
                await MakeLocalAsync(currency, cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        return OperationResult.Ok(currency.CurrencyId);
    }

    /// <summary>
    /// The code never changes. Making a currency local moves the flag from the current local currency in one
    /// transaction; the local currency cannot simply be switched off.
    /// </summary>
    public async Task<OperationResult<int>> UpdateCurrencyAsync(int currencyId, CurrencyDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var currency = await _db.Set<Currency>().SingleOrDefaultAsync(row => row.CurrencyId == currencyId, cancellationToken).ConfigureAwait(false);
        if (currency is null)
        {
            return OperationResult.Fail<int>(ProblemCodes.NotFound);
        }

        if (draft.CurrencyCode is not null && !string.Equals(draft.CurrencyCode.Trim(), currency.CurrencyCode, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Fail<int>(ProblemCodes.ReferenceCodeImmutable);
        }

        var errors = ValidateCurrency(draft);
        if (errors.Count > 0)
        {
            return OperationResult.Fail<int>(ProblemCodes.Validation, errors);
        }

        if (currency.IsLocalCurrency && !draft.IsLocalCurrency)
        {
            return OperationResult.Fail<int>(ProblemCodes.LocalCurrencyRequired);
        }

        currency.CurrencyName = draft.CurrencyName.Trim();
        currency.ExchangeRate = draft.IsLocalCurrency ? 1m : draft.ExchangeRate;
        _activity.Record(EventCodes.UpdateRecord, nameof(Currency), currency.CurrencyCode);

        var transaction = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (draft.IsLocalCurrency && !currency.IsLocalCurrency)
            {
                await MakeLocalAsync(currency, cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        return OperationResult.Ok(currencyId);
    }

    // ---------------------------------------------------------------- Holidays

    public async Task<IReadOnlyList<HolidayView>> ListHolidaysAsync(int? year, CancellationToken cancellationToken)
    {
        var query = _db.Set<DefinedHoliday>().AsNoTracking();
        if (year is { } y and >= 1 and <= 9999)
        {
            var from = new DateOnly(y, 1, 1);
            var to = new DateOnly(y, 12, 31);
            query = query.Where(row => row.HolidayDate >= from && row.HolidayDate <= to);
        }

        return await query
            .OrderBy(row => row.HolidayDate)
            .Select(row => new HolidayView(row.HolidayId, row.HolidayDate, row.Remarks))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>A holiday cannot be added on a date any branch has already opened as a business day.</summary>
    public async Task<OperationResult<int>> CreateHolidayAsync(DateOnly? date, string? remarks, CancellationToken cancellationToken)
    {
        var note = Blank(remarks);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (date is null)
        {
            errors["holidayDate"] = ["Enter the date of the holiday."];
        }

        if (note is { Length: > 100 })
        {
            errors["remarks"] = ["Keep the remarks to 100 characters."];
        }

        if (errors.Count > 0)
        {
            return OperationResult.Fail<int>(ProblemCodes.Validation, errors);
        }

        var day = date!.Value;
        if (await _db.Set<DefinedHoliday>().AnyAsync(row => row.HolidayDate == day, cancellationToken).ConfigureAwait(false))
        {
            return OperationResult.Fail<int>(ProblemCodes.HolidayExists);
        }

        if (await _db.Set<BusinessDayStatus>().AnyAsync(row => row.WorkingDate == day, cancellationToken).ConfigureAwait(false))
        {
            return OperationResult.Fail<int>(ProblemCodes.HolidayBusinessDayExists);
        }

        var holiday = new DefinedHoliday { HolidayDate = day, Remarks = note };
        _db.Set<DefinedHoliday>().Add(holiday);
        _activity.Record(EventCodes.CreateRecord, nameof(DefinedHoliday), day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        return await SaveUniqueAsync(holiday, () => holiday.HolidayId, ProblemCodes.HolidayExists, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A holiday on a date that any branch has already worked stays, so the calendar keeps its history.</summary>
    public async Task<OperationResult<int>> DeleteHolidayAsync(int holidayId, CancellationToken cancellationToken)
    {
        var holiday = await _db.Set<DefinedHoliday>().SingleOrDefaultAsync(row => row.HolidayId == holidayId, cancellationToken).ConfigureAwait(false);
        if (holiday is null)
        {
            return OperationResult.Fail<int>(ProblemCodes.NotFound);
        }

        if (await _db.Set<BusinessDayStatus>().AnyAsync(row => row.WorkingDate == holiday.HolidayDate, cancellationToken).ConfigureAwait(false))
        {
            return OperationResult.Fail<int>(ProblemCodes.HolidayBusinessDayExists);
        }

        _db.Set<DefinedHoliday>().Remove(holiday);
        _activity.Record(EventCodes.DeleteRecord, nameof(DefinedHoliday), holiday.HolidayDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok(holidayId);
    }

    // ---------------------------------------------------------------- Helpers

    private async Task MakeLocalAsync(Currency currency, CancellationToken cancellationToken)
    {
        // Two saves: the filtered unique index allows only one local row at any moment.
        var current = await _db.Set<Currency>()
            .Where(row => row.IsLocalCurrency && row.CurrencyId != currency.CurrencyId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var row in current)
        {
            row.IsLocalCurrency = false;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        currency.IsLocalCurrency = true;
        currency.ExchangeRate = 1m;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<OperationResult<int>> SaveUniqueAsync(object entity, Func<int> id, string duplicateCode, CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok(id());
        }
        catch (DbUpdateException)
        {
            // A concurrent insert won the unique index.
            _db.Entry(entity).State = EntityState.Detached;
            _activity.Written = false;
            return OperationResult.Fail<int>(duplicateCode);
        }
    }

    private static Dictionary<string, string[]> ValidateBranch(BranchDraft draft)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var name = draft.BranchName?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 100)
        {
            errors["branchName"] = ["Enter a name of up to 100 characters."];
        }

        if (Blank(draft.OperatingLicenseNo) is { Length: > 30 })
        {
            errors["operatingLicenseNo"] = ["Keep the licence number to 30 characters."];
        }

        if (draft.ClearingDays is < 0 or > 30)
        {
            errors["clearingDays"] = ["Enter a number of days from 0 to 30."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateCurrency(CurrencyDraft draft)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var name = draft.CurrencyName?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 50)
        {
            errors["currencyName"] = ["Enter a name of up to 50 characters."];
        }

        if (!draft.IsLocalCurrency && draft.ExchangeRate is <= 0m or > 999_999_999_999m)
        {
            errors["exchangeRate"] = ["Enter a rate greater than zero."];
        }

        return errors;
    }

    private DateOnly Today() => DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
