using Banking.Api.Problems;
using Banking.Api.Security;
using Banking.Application.Abstractions;
using Banking.Application.Errors;
using Banking.Domain.Lookups;
using Banking.Domain.Security;
using Banking.Infrastructure.Reference;
using Banking.Infrastructure.Security;

namespace Banking.Api.Endpoints;

internal sealed record UpdateInstitutionProfileRequest(
    string? InstitutionName,
    string? LicenseNumber,
    int? RegulatoryCategoryId,
    DateOnly? IncorporationDate,
    string? HeadOfficeAddress);

internal sealed record BranchRequest(string? BranchCode, string? BranchName, string? OperatingLicenseNo, short? ClearingDays, bool IsActive = true);

internal sealed record CurrencyRequest(string? CurrencyCode, string? CurrencyName, decimal? ExchangeRate, bool IsLocalCurrency = false);

internal sealed record HolidayRequest(DateOnly? HolidayDate, string? Remarks);

internal sealed record OpenBusinessDayRequest(DateOnly? WorkingDate);

/// <summary>A3-01 institution, branches, currencies, holidays; A3-02 business days.</summary>
internal static class ReferenceEndpoints
{
    private const string Institution = "INSTITUTION";
    private const string Branches = "BRANCHES";
    private const string Currencies = "CURRENCIES";
    private const string Calendar = "CALENDAR";
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    public static RouteGroupBuilder MapReferenceEndpoints(this RouteGroupBuilder api)
    {
        // Reference reads are open to anyone signed in: the portal needs them on every screen.
        var profile = api.MapGroup("/institution-profile").WithTags("Institution");

        profile.MapGet("/", GetProfileAsync)
            .AuthenticatedOnly()
            .Produces<InstitutionProfileView>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        profile.MapPut("/", UpdateProfileAsync)
            .RequirePermission(ModuleCatalog.ReferenceData, Institution, PermissionAction.Edit)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

        var branches = api.MapGroup("/branches").WithTags("Branches");

        branches.MapGet("/", ListBranchesAsync)
            .AuthenticatedOnly()
            .Produces<IReadOnlyList<BranchView>>();

        branches.MapGet("/{branchId:int}", GetBranchAsync)
            .AuthenticatedOnly()
            .Produces<BranchView>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        branches.MapPost("/", CreateBranchAsync)
            .RequirePermission(ModuleCatalog.ReferenceData, Branches, PermissionAction.Add)
            .Produces<CreatedId>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        branches.MapPut("/{branchId:int}", UpdateBranchAsync)
            .RequirePermission(ModuleCatalog.ReferenceData, Branches, PermissionAction.Edit)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        branches.MapGet("/{branchId:int}/business-days", ListBusinessDaysAsync)
            .RequirePermission(ModuleCatalog.ReferenceData, Calendar, PermissionAction.View)
            .WithTags("Business days")
            .Produces<PagedResult<BusinessDayView>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        branches.MapPost("/{branchId:int}/business-days/open", OpenBusinessDayAsync)
            .RequirePermission(ModuleCatalog.ReferenceData, Calendar, PermissionAction.Edit)
            .WithTags("Business days")
            .Produces<CreatedId>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        branches.MapPost("/{branchId:int}/business-days/{workingDate}/close", CloseBusinessDayAsync)
            .RequirePermission(ModuleCatalog.ReferenceData, Calendar, PermissionAction.Supervise)
            .WithTags("Business days")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        api.MapGet("/business-day/current", CurrentBusinessDayAsync)
            .AuthenticatedOnly()
            .WithTags("Business days")
            .Produces<CurrentBusinessDay>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        var currencies = api.MapGroup("/currencies").WithTags("Currencies");

        currencies.MapGet("/", ListCurrenciesAsync)
            .AuthenticatedOnly()
            .Produces<IReadOnlyList<CurrencyView>>();

        currencies.MapPost("/", CreateCurrencyAsync)
            .RequirePermission(ModuleCatalog.ReferenceData, Currencies, PermissionAction.Add)
            .Produces<CreatedId>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        currencies.MapPut("/{currencyId:int}", UpdateCurrencyAsync)
            .RequirePermission(ModuleCatalog.ReferenceData, Currencies, PermissionAction.Edit)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        var holidays = api.MapGroup("/holidays").WithTags("Holidays");

        holidays.MapGet("/", ListHolidaysAsync)
            .AuthenticatedOnly()
            .Produces<IReadOnlyList<HolidayView>>();

        holidays.MapPost("/", CreateHolidayAsync)
            .RequirePermission(ModuleCatalog.ReferenceData, Calendar, PermissionAction.Add)
            .Produces<CreatedId>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        holidays.MapDelete("/{holidayId:int}", DeleteHolidayAsync)
            .RequirePermission(ModuleCatalog.ReferenceData, Calendar, PermissionAction.Delete)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return api;
    }

    private static async Task<IResult> GetProfileAsync(ReferenceAdmin admin, CancellationToken cancellationToken)
    {
        var profile = await admin.GetProfileAsync(cancellationToken).ConfigureAwait(false);
        return profile is null ? NotFound() : TypedResults.Ok(profile);
    }

    private static async Task<IResult> UpdateProfileAsync(UpdateInstitutionProfileRequest request, ReferenceAdmin admin, CancellationToken cancellationToken)
    {
        var result = await admin.UpdateProfileAsync(
                new InstitutionProfileChanges(
                    request.InstitutionName ?? string.Empty,
                    request.LicenseNumber,
                    request.RegulatoryCategoryId,
                    request.IncorporationDate,
                    request.HeadOfficeAddress),
                cancellationToken)
            .ConfigureAwait(false);
        return NoContentOr(result);
    }

    private static async Task<IResult> ListBranchesAsync(ReferenceAdmin admin, bool includeInactive = false, CancellationToken cancellationToken = default) =>
        TypedResults.Ok(await admin.ListBranchesAsync(includeInactive, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> GetBranchAsync(int branchId, ReferenceAdmin admin, CancellationToken cancellationToken)
    {
        var branch = await admin.GetBranchAsync(branchId, cancellationToken).ConfigureAwait(false);
        return branch is null ? NotFound() : TypedResults.Ok(branch);
    }

    private static async Task<IResult> CreateBranchAsync(BranchRequest request, ReferenceAdmin admin, CancellationToken cancellationToken)
    {
        var result = await admin.CreateBranchAsync(ToDraft(request), cancellationToken).ConfigureAwait(false);
        return CreatedOr(result);
    }

    private static async Task<IResult> UpdateBranchAsync(int branchId, BranchRequest request, ReferenceAdmin admin, CancellationToken cancellationToken)
    {
        var result = await admin.UpdateBranchAsync(branchId, ToDraft(request), cancellationToken).ConfigureAwait(false);
        return NoContentOr(result);
    }

    private static async Task<IResult> ListCurrenciesAsync(ReferenceAdmin admin, CancellationToken cancellationToken) =>
        TypedResults.Ok(await admin.ListCurrenciesAsync(cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> CreateCurrencyAsync(CurrencyRequest request, ReferenceAdmin admin, CancellationToken cancellationToken)
    {
        var result = await admin.CreateCurrencyAsync(ToDraft(request), cancellationToken).ConfigureAwait(false);
        return CreatedOr(result);
    }

    private static async Task<IResult> UpdateCurrencyAsync(int currencyId, CurrencyRequest request, ReferenceAdmin admin, CancellationToken cancellationToken)
    {
        var result = await admin.UpdateCurrencyAsync(currencyId, ToDraft(request), cancellationToken).ConfigureAwait(false);
        return NoContentOr(result);
    }

    private static async Task<IResult> ListHolidaysAsync(ReferenceAdmin admin, int? year = null, CancellationToken cancellationToken = default) =>
        TypedResults.Ok(await admin.ListHolidaysAsync(year, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> CreateHolidayAsync(HolidayRequest request, ReferenceAdmin admin, CancellationToken cancellationToken)
    {
        var result = await admin.CreateHolidayAsync(request.HolidayDate, request.Remarks, cancellationToken).ConfigureAwait(false);
        return CreatedOr(result);
    }

    private static async Task<IResult> DeleteHolidayAsync(int holidayId, ReferenceAdmin admin, CancellationToken cancellationToken)
    {
        var result = await admin.DeleteHolidayAsync(holidayId, cancellationToken).ConfigureAwait(false);
        return NoContentOr(result);
    }

    private static async Task<IResult> CurrentBusinessDayAsync(BusinessDays days, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var current = currentUser.BranchId is { } branchId
            ? await days.CurrentAsync(branchId, cancellationToken).ConfigureAwait(false)
            : null;
        return current is null ? NotFound() : TypedResults.Ok(current);
    }

    private static async Task<IResult> ListBusinessDaysAsync(
        int branchId,
        BusinessDays days,
        int page = 1,
        int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await days.ListAsync(branchId, Math.Max(1, page), Math.Clamp(pageSize, 1, MaxPageSize), cancellationToken).ConfigureAwait(false);
        return result is null ? NotFound() : TypedResults.Ok(result);
    }

    private static async Task<IResult> OpenBusinessDayAsync(int branchId, OpenBusinessDayRequest request, BusinessDays days, CancellationToken cancellationToken)
    {
        var result = await days.OpenAsync(branchId, request.WorkingDate, cancellationToken).ConfigureAwait(false);
        return result.Succeeded
            ? TypedResults.Created((string?)null, new CreatedId(result.Value))
            : ResultProblems.From(result.ProblemCode!, result.Errors);
    }

    private static async Task<IResult> CloseBusinessDayAsync(int branchId, DateOnly workingDate, BusinessDays days, CancellationToken cancellationToken)
    {
        var result = await days.CloseAsync(branchId, workingDate, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? TypedResults.NoContent() : ResultProblems.From(result.ProblemCode!, result.Errors);
    }

    private static BranchDraft ToDraft(BranchRequest request) =>
        new(request.BranchCode, request.BranchName ?? string.Empty, request.OperatingLicenseNo, request.ClearingDays, request.IsActive);

    private static CurrencyDraft ToDraft(CurrencyRequest request) =>
        new(request.CurrencyCode, request.CurrencyName ?? string.Empty, request.ExchangeRate, request.IsLocalCurrency);

    private static IResult CreatedOr(OperationResult<int> result) =>
        result.Succeeded
            ? TypedResults.Created((string?)null, new CreatedId(result.Value))
            : ResultProblems.From(result.ProblemCode!, result.Errors);

    private static IResult NoContentOr(OperationResult<int> result) =>
        result.Succeeded ? TypedResults.NoContent() : ResultProblems.From(result.ProblemCode!, result.Errors);

    private static IResult NotFound() => ApiProblems.Result(StatusCodes.Status404NotFound, ProblemCodes.NotFound);
}
