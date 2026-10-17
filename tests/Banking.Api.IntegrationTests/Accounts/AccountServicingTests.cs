using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Application.Errors;
using Banking.Domain.Accounts;
using Banking.Domain.Lookups;
using Banking.Domain.Security;
using Banking.Domain.Supervision;
using Banking.Infrastructure.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Banking.Api.IntegrationTests.Accounts;

/// <summary>A5-06 holds and freezes, A5-07 close, dormancy, classifications, reminders, A5-08 search (plan 6.10).</summary>
[Collection(SqlServerCollection.Name)]
public sealed class AccountServicingTests : IAsyncLifetime
{
    private readonly MsSqlContainerFixture _fixture;
    private ApiTestHost _host = null!;
    private AccountSeed _seed = null!;
    private HttpClient _teller = null!;
    private HttpClient _supervisor = null!;
    private int _product;
    private int _closeReasonId;

    public AccountServicingTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _host = await ApiTestHost.CreateAsync(_fixture, "servicing");
        _seed = new AccountSeed(_host);
        await _host.AdminTokenAsync();
        await _host.CreateStaffAsync("TELLER1", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.Teller));
        await _host.CreateStaffAsync("SUPER1", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.Supervisor));
        _teller = _host.Client(await _host.TokenAsync("TELLER1"));
        _supervisor = _host.Client(await _host.TokenAsync("SUPER1"));
        _product = await _seed.ProductAsync("SAV01");

        await using var context = _host.NewContext();
        _closeReasonId = await context.Set<CloseReasonType>().Where(row => row.Code == LookupCodes.CloseReasons.CustomerRequest).Select(row => row.Id).SingleAsync();
    }

    public async Task DisposeAsync()
    {
        _teller.Dispose();
        _supervisor.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task Twenty_parallel_holds_on_one_account_add_up_with_no_lost_updates()
    {
        var account = await _seed.AccountAsync(_product, await _seed.CustomerAsync("Ama Hold"), clearBalance: 1_000m);

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => FreezeAsync(account, "HOLD_AMOUNT", 12.5m)));
        try
        {
            responses.ShouldAllBe(response => response.StatusCode == HttpStatusCode.Created);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        await using var context = _host.NewContext();
        var balance = await context.Set<AccountBalance>().AsNoTracking().SingleAsync(row => row.AccountId == account);
        balance.FreezedAmount.ShouldBe(250m);
        balance.AvailableBalance.ShouldBe(750m);
        balance.TotalBalance.ShouldBe(1_000m);
        (await context.Set<FreezeReleaseAccountBalance>().CountAsync(row => row.AccountId == account)).ShouldBe(20);
    }

    [Fact]
    public async Task Holds_releases_and_freezes_follow_the_rules_and_keep_a_history()
    {
        var account = await _seed.AccountAsync(_product, await _seed.CustomerAsync("Kofi Freeze"), clearBalance: 500m);

        using (var hold = await FreezeAsync(account, "HOLD_AMOUNT", 100m))
        {
            hold.StatusCode.ShouldBe(HttpStatusCode.Created);
            (await ApiTestHost.ReadJsonAsync(hold)).GetProperty("balance").GetProperty("availableBalance").GetDecimal().ShouldBe(400m);
        }

        await ExpectFieldAsync(await FreezeAsync(account, "RELEASE_AMOUNT", 150m), "amount");
        await ExpectFieldAsync(await FreezeAsync(account, "HOLD_AMOUNT", 0m), "amount");
        await ExpectFieldAsync(await FreezeAsync(account, "HOLD_AMOUNT", 1.005m), "amount");
        await ExpectFieldAsync(await FreezeAsync(account, "FREEZE_ACCOUNT", 5m), "amount");
        await ExpectFieldAsync(await FreezeAsync(account, "SHRED_ACCOUNT", null), "action");
        (await FreezeAsync(account, "RELEASE_AMOUNT", 40m)).StatusCode.ShouldBe(HttpStatusCode.Created);

        using (var freeze = await FreezeAsync(account, "FREEZE_ACCOUNT", null))
        {
            freeze.StatusCode.ShouldBe(HttpStatusCode.Created);
            (await ApiTestHost.ReadJsonAsync(freeze)).GetProperty("isFreezed").GetBoolean().ShouldBeTrue();
        }

        (await DetailAsync(account)).GetProperty("status").GetString().ShouldBe(AccountStatuses.Frozen);
        await ExpectAsync(await FreezeAsync(account, "FREEZE_ACCOUNT", null), ProblemCodes.AccountAlreadyFrozen);
        (await FreezeAsync(account, "UNFREEZE_ACCOUNT", null)).StatusCode.ShouldBe(HttpStatusCode.Created);
        await ExpectAsync(await FreezeAsync(account, "UNFREEZE_ACCOUNT", null), ProblemCodes.AccountNotFrozen);

        using var history = await _supervisor.GetAsync(new Uri($"/api/v1/accounts/{account}/freeze-actions", UriKind.Relative));
        var rows = (await ApiTestHost.ReadJsonAsync(history)).EnumerateArray().Select(row => row.GetProperty("action").GetString()!).ToArray();
        rows.ShouldBe(new[] { "UNFREEZE_ACCOUNT", "FREEZE_ACCOUNT", "RELEASE_AMOUNT", "HOLD_AMOUNT" });

        // Tellers see the account but do not hold or freeze.
        using var teller = await _teller.PostAsJsonAsync($"/api/v1/accounts/{account}/freeze-actions", new { action = "HOLD_AMOUNT", amount = 1m });
        teller.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_pending_account_cannot_be_held_and_a_retried_hold_counts_once()
    {
        var pending = await _seed.AccountAsync(_product, await _seed.CustomerAsync("Esi Pending"), approved: false);
        await ExpectAsync(await FreezeAsync(pending, "HOLD_AMOUNT", 5m), SupervisionProblems.Pending);

        var account = await _seed.AccountAsync(_product, await _seed.CustomerAsync("Yaw Retry"), clearBalance: 100m);
        var key = Guid.NewGuid().ToString("N");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/api/v1/accounts/{account}/freeze-actions", UriKind.Relative))
            {
                Content = JsonContent.Create(new { action = "HOLD_AMOUNT", amount = 30m, remarks = "Court order" })
            };
            request.Headers.Add("Idempotency-Key", key);
            using var response = await _supervisor.SendAsync(request);
            response.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        (await BalanceAsync(account)).GetProperty("freezedAmount").GetDecimal().ShouldBe(30m);
    }

    [Fact]
    public async Task Close_is_blocked_by_a_balance_or_a_hold_and_a_closed_account_rejects_every_change()
    {
        var account = await _seed.AccountAsync(_product, await _seed.CustomerAsync("Akua Close"), clearBalance: 20m);
        (await FreezeAsync(account, "HOLD_AMOUNT", 5m)).StatusCode.ShouldBe(HttpStatusCode.Created);

        using (var check = await _supervisor.GetAsync(new Uri($"/api/v1/accounts/{account}/close-check", UriKind.Relative)))
        {
            var body = await ApiTestHost.ReadJsonAsync(check);
            body.GetProperty("canClose").GetBoolean().ShouldBeFalse();
            body.GetProperty("checks").EnumerateArray().Where(row => !row.GetProperty("passed").GetBoolean())
                .Select(row => row.GetProperty("code").GetString()!).Order(StringComparer.Ordinal).ToArray()
                .ShouldBe(new[] { AccountCloseChecks.NoHolds, AccountCloseChecks.ZeroBalance }.Order(StringComparer.Ordinal).ToArray());
        }

        using (var blocked = await CloseAsync(_supervisor, account))
        {
            blocked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            var problem = await ApiTestHost.ReadJsonAsync(blocked);
            problem.GetProperty("type").GetString().ShouldBe(ProblemCodes.AccountCloseBlocked);
            problem.GetProperty("failedChecks").GetArrayLength().ShouldBe(2);
        }

        (await FreezeAsync(account, "RELEASE_AMOUNT", 5m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        await _seed.SetBalanceAsync(account, 0m);

        using (var noReason = await _supervisor.PostAsJsonAsync($"/api/v1/accounts/{account}/close", new { closeReasonId = (int?)null }))
        {
            noReason.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        (await CloseAsync(_teller, account)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CloseAsync(_supervisor, account)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var closed = await DetailAsync(account);
        closed.GetProperty("status").GetString().ShouldBe(AccountStatuses.Closed);
        closed.GetProperty("closeReasonCode").GetString().ShouldBe(LookupCodes.CloseReasons.CustomerRequest);

        await ExpectAsync(await CloseAsync(_supervisor, account), ProblemCodes.AccountClosed);
        await ExpectAsync(await FreezeAsync(account, "HOLD_AMOUNT", 1m), ProblemCodes.AccountClosed);
        await ExpectAsync(await FreezeAsync(account, "FREEZE_ACCOUNT", null), ProblemCodes.AccountClosed);
        await ExpectAsync(await PostAsync(_supervisor, $"/api/v1/accounts/{account}/dormancy/mark"), ProblemCodes.AccountClosed);
        await ExpectAsync(await _teller.PostAsJsonAsync($"/api/v1/accounts/{account}/reminders", new { reminderText = "Call the customer" }), ProblemCodes.AccountClosed);
    }

    [Fact]
    public async Task Dormancy_is_marked_and_reactivation_is_recorded()
    {
        var account = await _seed.AccountAsync(_product, await _seed.CustomerAsync("Kwame Dormant"));

        (await PostAsync(_supervisor, $"/api/v1/accounts/{account}/dormancy/mark")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await DetailAsync(account)).GetProperty("status").GetString().ShouldBe(AccountStatuses.Dormant);
        await ExpectAsync(await PostAsync(_supervisor, $"/api/v1/accounts/{account}/dormancy/mark"), ProblemCodes.AccountAlreadyDormant);

        (await PostAsync(_supervisor, $"/api/v1/accounts/{account}/dormancy/reactivate")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await DetailAsync(account)).GetProperty("status").GetString().ShouldBe(AccountStatuses.Active);
        await ExpectAsync(await PostAsync(_supervisor, $"/api/v1/accounts/{account}/dormancy/reactivate"), ProblemCodes.AccountNotDormant);

        await using var context = _host.NewContext();
        (await context.Set<DormantAccountHistory>().CountAsync(row => row.AccountId == account)).ShouldBe(1);
    }

    [Fact]
    public async Task Classifications_need_a_matching_sub_classification_and_are_assigned_once()
    {
        var account = await _seed.AccountAsync(_product, await _seed.CustomerAsync("Efua Class"));
        var risk = await CreatedIdAsync(await _supervisor.PostAsJsonAsync("/api/v1/account-classifications", new { code = "RISK", name = "Risk rating" }));
        var high = await CreatedIdAsync(await _supervisor.PostAsJsonAsync($"/api/v1/account-classifications/{risk}/sub-classifications", new { code = "HIGH", name = "High" }));
        var sector = await CreatedIdAsync(await _supervisor.PostAsJsonAsync("/api/v1/account-classifications", new { code = "SECTOR", name = "Sector" }));
        await ExpectAsync(await _supervisor.PostAsJsonAsync("/api/v1/account-classifications", new { code = "risk", name = "Again" }), ProblemCodes.LookupCodeTaken);

        using var wrongSub = await _supervisor.PostAsJsonAsync($"/api/v1/accounts/{account}/classifications", new { accountClassificationId = sector, accountSubClassificationId = high });
        await ExpectFieldAsync(wrongSub, "accountSubClassificationId");

        var assignment = await CreatedIdAsync(await _supervisor.PostAsJsonAsync($"/api/v1/accounts/{account}/classifications", new { accountClassificationId = risk, accountSubClassificationId = high }));
        await ExpectAsync(await _supervisor.PostAsJsonAsync($"/api/v1/accounts/{account}/classifications", new { accountClassificationId = risk }), ProblemCodes.AccountClassificationExists);
        (await CreatedIdAsync(await _supervisor.PostAsJsonAsync($"/api/v1/accounts/{account}/classifications", new { accountClassificationId = sector }))).ShouldBeGreaterThan(0);

        using (var list = await _supervisor.GetAsync(new Uri($"/api/v1/accounts/{account}/classifications", UriKind.Relative)))
        {
            var rows = (await ApiTestHost.ReadJsonAsync(list)).EnumerateArray().ToList();
            rows.Count.ShouldBe(2);
            rows.Single(row => row.GetProperty("classificationCode").GetString() == "RISK").GetProperty("subClassificationCode").GetString().ShouldBe("HIGH");
        }

        using var remove = await _supervisor.DeleteAsync(new Uri($"/api/v1/accounts/{account}/classifications/{assignment}", UriKind.Relative));
        remove.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Tellers_add_and_remove_reminders_shown_with_the_account()
    {
        var account = await _seed.AccountAsync(_product, await _seed.CustomerAsync("Yaa Remind"));

        var reminder = await CreatedIdAsync(await _teller.PostAsJsonAsync($"/api/v1/accounts/{account}/reminders", new { reminderText = "Ask for the updated Ghana Card" }));
        await ExpectFieldAsync(await _teller.PostAsJsonAsync($"/api/v1/accounts/{account}/reminders", new { reminderText = new string('x', 201) }), "reminderText");

        (await DetailAsync(account)).GetProperty("reminders")[0].GetProperty("reminderText").GetString().ShouldBe("Ask for the updated Ghana Card");
        (await _teller.DeleteAsync(new Uri($"/api/v1/accounts/{account}/reminders/{reminder}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await DetailAsync(account)).GetProperty("reminders").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Search_finds_accounts_by_number_or_holder_and_filters_by_derived_status()
    {
        var product = await _seed.ProductAsync("SRCH1", prefix: "55");
        var mensah = await _seed.CustomerAsync("Mensah Searchable");
        var active = await _seed.AccountAsync(product, mensah, number: "5500000000101");
        var frozen = await _seed.AccountAsync(product, mensah, clearBalance: 1m, number: "5500000000102");
        var pending = await _seed.AccountAsync(product, await _seed.CustomerAsync("Owusu Searchable"), approved: false, number: "5500000000103");
        (await FreezeAsync(frozen, "FREEZE_ACCOUNT", null)).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await SearchAsync(new { q = "55000000001" })).ShouldBe(new[] { active, frozen, pending });
        (await SearchAsync(new { q = "Mensah Search" })).ShouldBe(new[] { active, frozen });
        (await SearchAsync(new { q = "55000000001", status = "FROZEN" })).ShouldBe(new[] { frozen });
        (await SearchAsync(new { q = "55000000001", status = "PENDING_SUPERVISION" })).ShouldBe(new[] { pending });
        (await SearchAsync(new { q = "55000000001", status = "ACTIVE" })).ShouldBe(new[] { active });

        using (var list = await _teller.GetAsync(new Uri($"/api/v1/accounts?productId={product}&pageSize=50", UriKind.Relative)))
        {
            var body = await ApiTestHost.ReadJsonAsync(list);
            body.GetProperty("totalCount").GetInt32().ShouldBe(3);
            body.GetProperty("items")[0].GetProperty("primaryHolderName").GetString().ShouldNotBeNull();
        }

        // The customer's overview lists the accounts they hold.
        using var overview = await _teller.GetAsync(new Uri($"/api/v1/parties/{mensah}/overview", UriKind.Relative));
        overview.StatusCode.ShouldBe(HttpStatusCode.OK);
        var accounts = (await ApiTestHost.ReadJsonAsync(overview)).GetProperty("accounts").EnumerateArray().ToList();
        accounts.Select(row => row.GetProperty("accountId").GetInt64()).Order().ToArray().ShouldBe(new[] { active, frozen });
        accounts.ShouldAllBe(row => row.GetProperty("roleCode").GetString() == "PRIMARY");
    }

    [Fact]
    public async Task With_20000_accounts_the_indexed_search_only_seeks()
    {
        var product = await _seed.ProductAsync("PLAN1", prefix: "66");
        await using (var context = _host.NewContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                DECLARE @type int = (SELECT [Id] FROM [dbo].[PartyType] WHERE [Code] = 'INDIVIDUAL');
                DECLARE @kyc int = (SELECT [Id] FROM [dbo].[KYCStatusType] WHERE [Code] = 'VERIFIED');
                DECLARE @role int = (SELECT [Id] FROM [dbo].[AccountHolderRoleType] WHERE [Code] = 'PRIMARY');
                WITH numbers AS (SELECT TOP (20000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_objects AS a CROSS JOIN sys.all_objects AS b)
                INSERT INTO [dbo].[Party] ([PartyNumber], [DisplayName], [PartyTypeId], [KYCStatusId], [IsPoliticallyExposed], [IsSupervisionRequired], [CreatedOn])
                SELECT CONCAT('Q', RIGHT(CONCAT('0000000', n), 7)), CONCAT(N'Seeded Holder ', n), @type, @kyc, 0, 0, SYSUTCDATETIME() FROM numbers;
                WITH numbers AS (SELECT TOP (20000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_objects AS a CROSS JOIN sys.all_objects AS b)
                INSERT INTO [dbo].[Account] ([AccountNumber], [BranchId], [ProductId], [AccountName], [DateOpened], [IsDormant], [IsSupervisionRequired], [CreatedOn])
                SELECT CONCAT('66', RIGHT(CONCAT('00000000000', n), 11)), {0}, {1}, CONCAT(N'Seeded account ', n), '2030-01-07', 0, 0, SYSUTCDATETIME() FROM numbers;
                INSERT INTO [dbo].[AccountHolder] ([AccountId], [PartyId], [AccountHolderRoleId], [CreatedOn])
                SELECT a.[AccountId], p.[PartyId], @role, SYSUTCDATETIME()
                FROM [dbo].[Account] AS a
                JOIN [dbo].[Party] AS p ON p.[PartyNumber] = CONCAT('Q', RIGHT(a.[AccountNumber], 7))
                WHERE a.[AccountNumber] LIKE '66%';
                UPDATE STATISTICS [dbo].[Party];
                UPDATE STATISTICS [dbo].[Account];
                UPDATE STATISTICS [dbo].[AccountHolder];
                """,
                _host.HomeBranchId,
                product);
        }

        using var scope = _host.Factory.Services.CreateScope();
        var search = scope.ServiceProvider.GetRequiredService<AccountSearch>();

        foreach (var term in new[] { "660000001234", "Seeded Holder 1999", "Q0004321" })
        {
            var query = (await search.IndexedMatchesAsync(search.Accounts(), term, CancellationToken.None)).Select(account => account.AccountId);
            var plan = await QueryPlans.ActualPlanAsync(query);

            plan.ShouldContain("Index Seek", customMessage: term);
            plan.ShouldNotContain("PhysicalOp=\"Clustered Index Scan\"", customMessage: term);
            plan.ShouldNotContain("PhysicalOp=\"Index Scan\"", customMessage: term);
            plan.ShouldNotContain("PhysicalOp=\"Table Scan\"", customMessage: term);
        }

        (await SearchAsync(new { q = "6600000012345" })).Count.ShouldBe(1);
    }

    private Task<HttpResponseMessage> FreezeAsync(long accountId, string action, decimal? amount) =>
        _supervisor.PostAsJsonAsync($"/api/v1/accounts/{accountId}/freeze-actions", new { action, amount, remarks = "Test" });

    private Task<HttpResponseMessage> CloseAsync(HttpClient client, long accountId) =>
        client.PostAsJsonAsync($"/api/v1/accounts/{accountId}/close", new { closeReasonId = _closeReasonId });

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path) =>
        client.PostAsync(new Uri(path, UriKind.Relative), content: null);

    private async Task<List<long>> SearchAsync(object body)
    {
        using var response = await _teller.PostAsJsonAsync("/api/v1/accounts/search", body);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await ApiTestHost.ReadJsonAsync(response)).GetProperty("items").EnumerateArray()
            .Select(row => row.GetProperty("accountId").GetInt64())
            .ToList();
    }

    private async Task<JsonElement> DetailAsync(long accountId)
    {
        using var response = await _teller.GetAsync(new Uri($"/api/v1/accounts/{accountId}", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await ApiTestHost.ReadJsonAsync(response);
    }

    private async Task<JsonElement> BalanceAsync(long accountId)
    {
        using var response = await _teller.GetAsync(new Uri($"/api/v1/accounts/{accountId}/balance", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await ApiTestHost.ReadJsonAsync(response);
    }

    private static async Task<long> CreatedIdAsync(HttpResponseMessage response)
    {
        using (response)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            return (await ApiTestHost.ReadJsonAsync(response)).GetProperty("id").GetInt64();
        }
    }

    private static async Task ExpectFieldAsync(HttpResponseMessage response, string field)
    {
        using (response)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await ApiTestHost.ReadJsonAsync(response)).GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue(field);
        }
    }

    private static async Task ExpectAsync(HttpResponseMessage response, string problemCode)
    {
        using (response)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await ApiTestHost.ProblemTypeAsync(response)).ShouldBe(problemCode);
        }
    }
}
