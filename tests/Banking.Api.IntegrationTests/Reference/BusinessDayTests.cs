using System.Net;
using System.Net.Http.Json;
using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Application.Abstractions;
using Banking.Application.Errors;
using Banking.Domain.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Banking.Api.IntegrationTests.Reference;

/// <summary>A3-02: open and close business days, and the working-date provider (plan 6.12).</summary>
[Collection(SqlServerCollection.Name)]
public sealed class BusinessDayTests : IAsyncLifetime
{
    private readonly MsSqlContainerFixture _fixture;
    private ApiTestHost _host = null!;
    private string _admin = null!;

    public BusinessDayTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _host = await ApiTestHost.CreateAsync(_fixture, "businessdays");
        _admin = await _host.AdminTokenAsync();
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Days_open_in_order_skip_weekends_and_holidays_and_close_one_at_a_time()
    {
        var (branchId, client) = await SupervisorInNewBranchAsync("BD1", "SUP1");
        using (client)
        {
            using var admin = _host.Client(_admin);
            (await admin.PostAsJsonAsync("/api/v1/holidays", new { holidayDate = "2030-01-08", remarks = "Test holiday" }))
                .StatusCode.ShouldBe(HttpStatusCode.Created);

            // Monday 2030-01-07 opens.
            (await OpenAsync(client, branchId, "2030-01-07")).StatusCode.ShouldBe(HttpStatusCode.Created);

            using (var current = await client.GetAsync(new Uri("/api/v1/business-day/current", UriKind.Relative)))
            {
                var day = await ApiTestHost.ReadJsonAsync(current);
                day.GetProperty("workingDate").GetString().ShouldBe("2030-01-07");
                day.GetProperty("isOpen").GetBoolean().ShouldBeTrue();
            }

            // The next day waits for the current one to close.
            await ExpectAsync(await OpenAsync(client, branchId, "2030-01-09"), ProblemCodes.BusinessDayPreviousOpen);

            (await CloseAsync(client, branchId, "2030-01-07")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            await ExpectAsync(await CloseAsync(client, branchId, "2030-01-07"), ProblemCodes.BusinessDayNotOpen);

            // Not again, not earlier, not a holiday, not a weekend.
            await ExpectAsync(await OpenAsync(client, branchId, "2030-01-07"), ProblemCodes.BusinessDayDateNotAfter);
            await ExpectAsync(await OpenAsync(client, branchId, "2030-01-03"), ProblemCodes.BusinessDayDateNotAfter);
            await ExpectAsync(await OpenAsync(client, branchId, "2030-01-08"), ProblemCodes.BusinessDayNonWorkingDay);
            await ExpectAsync(await OpenAsync(client, branchId, "2030-01-12"), ProblemCodes.BusinessDayNonWorkingDay);

            // Wednesday after the holiday opens.
            (await OpenAsync(client, branchId, "2030-01-09")).StatusCode.ShouldBe(HttpStatusCode.Created);

            using var history = await client.GetAsync(new Uri($"/api/v1/branches/{branchId}/business-days", UriKind.Relative));
            var items = (await ApiTestHost.ReadJsonAsync(history)).GetProperty("items").EnumerateArray().ToList();
            items.Select(item => item.GetProperty("status").GetString()!).ToArray().ShouldBe(new[] { "OPEN", "CLOSED" });
        }
    }

    [Fact]
    public async Task The_working_date_provider_refuses_when_no_day_is_open()
    {
        var (branchId, client) = await SupervisorInNewBranchAsync("BD2", "SUP2");
        using (client)
        {
            using (var scope = _host.Factory.Services.CreateScope())
            {
                var provider = scope.ServiceProvider.GetRequiredService<IWorkingDateProvider>();
                var refused = await Should.ThrowAsync<BusinessRuleException>(() => provider.GetAsync(branchId, CancellationToken.None));
                refused.ProblemCode.ShouldBe(ProblemCodes.BusinessDayNotOpen);
            }

            using (var current = await client.GetAsync(new Uri("/api/v1/business-day/current", UriKind.Relative)))
            {
                var day = await ApiTestHost.ReadJsonAsync(current);
                day.GetProperty("isOpen").GetBoolean().ShouldBeFalse();
                day.GetProperty("workingDate").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
            }

            (await OpenAsync(client, branchId, "2031-03-03")).StatusCode.ShouldBe(HttpStatusCode.Created);

            using (var scope = _host.Factory.Services.CreateScope())
            {
                var provider = scope.ServiceProvider.GetRequiredService<IWorkingDateProvider>();
                (await provider.GetAsync(branchId, CancellationToken.None)).ShouldBe(new DateOnly(2031, 3, 3));
            }
        }
    }

    [Fact]
    public async Task A_worked_date_cannot_become_a_holiday_and_other_branches_are_out_of_reach()
    {
        var (branchId, client) = await SupervisorInNewBranchAsync("BD3", "SUP3");
        using (client)
        {
            (await OpenAsync(client, branchId, "2032-02-02")).StatusCode.ShouldBe(HttpStatusCode.Created);

            using var admin = _host.Client(_admin);
            using var holiday = await admin.PostAsJsonAsync("/api/v1/holidays", new { holidayDate = "2032-02-02" });
            await ExpectAsync(holiday, ProblemCodes.HolidayBusinessDayExists);

            // The admin works in the head office branch and cannot open or close BD3's day.
            await ExpectAsync(await OpenAsync(admin, branchId, "2032-02-03"), ProblemCodes.RecordOtherBranch);
            await ExpectAsync(await CloseAsync(admin, branchId, "2032-02-02"), ProblemCodes.RecordOtherBranch);
        }
    }

    [Fact]
    public async Task A_teller_sees_the_working_date_but_cannot_open_or_close_days()
    {
        await _host.CreateStaffAsync("TELLER41", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.Teller));
        using var teller = _host.Client(await _host.TokenAsync("TELLER41"));

        (await teller.GetAsync(new Uri("/api/v1/business-day/current", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await OpenAsync(teller, _host.HomeBranchId, "2033-05-02")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CloseAsync(teller, _host.HomeBranchId, "2033-05-02")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private async Task<(int BranchId, HttpClient Client)> SupervisorInNewBranchAsync(string branchCode, string staffCode)
    {
        var branchId = await _host.CreateBranchAsync(branchCode);
        await _host.CreateStaffAsync(staffCode, canSwitchBranch: true, (branchId, RoleGroupCatalog.Supervisor));
        return (branchId, _host.Client(await _host.TokenAsync(staffCode, branchId: branchId)));
    }

    private static Task<HttpResponseMessage> OpenAsync(HttpClient client, int branchId, string date) =>
        client.PostAsJsonAsync($"/api/v1/branches/{branchId}/business-days/open", new { workingDate = date });

    private static Task<HttpResponseMessage> CloseAsync(HttpClient client, int branchId, string date) =>
        client.PostAsync(new Uri($"/api/v1/branches/{branchId}/business-days/{date}/close", UriKind.Relative), content: null);

    private static async Task ExpectAsync(HttpResponseMessage response, string problemCode)
    {
        using (response)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await ApiTestHost.ProblemTypeAsync(response)).ShouldBe(problemCode);
        }
    }
}
