using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Application.Errors;
using Banking.Domain.Institution;
using Banking.Domain.Ledger;
using Banking.Domain.Lookups;
using Banking.Domain.Products;
using Banking.Domain.Security;
using Banking.Domain.Supervision;
using Banking.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Banking.Api.IntegrationTests.Transactions;

/// <summary>B0-02: branch and product ledger settings and role-group transaction limits, all four-eyes.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class PostingSettingsTests : IAsyncLifetime
{
    private readonly MsSqlContainerFixture _fixture;
    private ApiTestHost _host = null!;
    private HttpClient _accountant1 = null!;
    private HttpClient _accountant2 = null!;
    private HttpClient _admin1 = null!;
    private HttpClient _admin2 = null!;
    private int _otherBranchId;

    public PostingSettingsTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _host = await ApiTestHost.CreateAsync(
            _fixture,
            "postsettings",
            new Dictionary<string, string?> { ["Supervision:AllowSelfApproval"] = "false" });
        _otherBranchId = await _host.CreateBranchAsync("002");
        await _host.CreateStaffAsync("ACCT1", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.Accountant));
        await _host.CreateStaffAsync("ACCT2", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.Accountant));
        await _host.CreateStaffAsync(
            "ADMIN2",
            canSwitchBranch: false,
            (_host.HomeBranchId, RoleGroupCatalog.Administrator),
            (_host.HomeBranchId, RoleGroupCatalog.Supervisor));
        _accountant1 = _host.Client(await _host.TokenAsync("ACCT1"));
        _accountant2 = _host.Client(await _host.TokenAsync("ACCT2"));
        _admin1 = _host.Client(await _host.AdminTokenAsync());
        _admin2 = _host.Client(await _host.TokenAsync("ADMIN2"));
    }

    public async Task DisposeAsync()
    {
        _accountant1.Dispose();
        _accountant2.Dispose();
        _admin1.Dispose();
        _admin2.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task Branch_settings_apply_only_when_a_different_person_approves_them()
    {
        var vault = await LedgerAccountAsync("1011-01", LookupCodes.LedgerAccountTypes.Asset, _host.HomeBranchId);
        var shortages = await LedgerAccountAsync("1012-01", LookupCodes.LedgerAccountTypes.Expense, _host.HomeBranchId);
        var path = $"/api/v1/branches/{_host.HomeBranchId}/ledger-settings";

        using (var put = await _accountant1.PutAsJsonAsync(path, Settings(("vault", vault), ("CASH_SHORT", shortages))))
        {
            put.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        var waiting = await ReadAsync(_accountant1, path);
        Setting(waiting, LedgerSettingCodes.Vault).GetProperty("ledgerAccountId").ValueKind.ShouldBe(JsonValueKind.Null);
        waiting.GetProperty("pending").GetProperty("proposed").GetArrayLength().ShouldBe(2);

        using (var again = await _accountant2.PutAsJsonAsync(path, Settings(("VAULT", vault))))
        {
            again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await ApiTestHost.ProblemTypeAsync(again)).ShouldBe(ProblemCodes.ChangePendingExists);
        }

        using (var own = await _accountant1.PostAsync(new Uri(path + "/approve", UriKind.Relative), null))
        {
            own.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await ApiTestHost.ProblemTypeAsync(own)).ShouldBe(SupervisionProblems.SelfApproval);
        }

        using (var approve = await _accountant2.PostAsync(new Uri(path + "/approve", UriKind.Relative), null))
        {
            approve.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        var applied = await ReadAsync(_accountant1, path);
        Setting(applied, LedgerSettingCodes.Vault).GetProperty("ledgerAccountId").GetInt64().ShouldBe(vault);
        Setting(applied, LedgerSettingCodes.CashShort).GetProperty("ledgerAccountCode").GetString().ShouldBe("1012-01");
        applied.GetProperty("pending").ValueKind.ShouldBe(JsonValueKind.Null);

        // The whole set is replaced: leaving CASH_SHORT out removes it.
        using (var replace = await _accountant2.PutAsJsonAsync(path, Settings(("VAULT", vault))))
        {
            replace.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        using (var approve = await _accountant1.PostAsync(new Uri(path + "/approve", UriKind.Relative), null))
        {
            approve.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        await using var context = _host.NewContext();
        (await context.Set<SystemLedgerSetting>().Where(row => row.BranchId == _host.HomeBranchId).Select(row => row.SettingCode).ToListAsync())
            .ShouldBe([LedgerSettingCodes.Vault]);
    }

    [Fact]
    public async Task Settings_must_point_at_approved_accounts_of_the_right_type_branch_and_currency()
    {
        var liability = await LedgerAccountAsync("2011-01", LookupCodes.LedgerAccountTypes.Liability, _host.HomeBranchId);
        var elsewhere = await LedgerAccountAsync("1013-02", LookupCodes.LedgerAccountTypes.Asset, _otherBranchId);
        var pending = await LedgerAccountAsync("1014-01", LookupCodes.LedgerAccountTypes.Asset, _host.HomeBranchId, approved: false);
        var path = $"/api/v1/branches/{_host.HomeBranchId}/ledger-settings";

        foreach (var body in new[]
                 {
                     Settings(("VAULT", liability)),
                     Settings(("VAULT", elsewhere)),
                     Settings(("VAULT", pending)),
                     Settings(("NOT_A_SETTING", liability)),
                     Settings(("VAULT", 999_999)),
                     Settings(("WHT_PAYABLE", liability), ("WHT_PAYABLE", liability))
                 })
        {
            using var response = await _accountant1.PutAsJsonAsync(path, body);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using var missing = await _accountant1.PutAsJsonAsync($"/api/v1/branches/9999/ledger-settings", Settings(("VAULT", liability)));
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Product_settings_follow_the_same_four_eyes_rule()
    {
        var expense = await LedgerAccountAsync("5011-01", LookupCodes.LedgerAccountTypes.Expense, _host.HomeBranchId);
        var income = await LedgerAccountAsync("4011-01", LookupCodes.LedgerAccountTypes.Income, _host.HomeBranchId);
        var productId = await ProductAsync("SAVT1");
        var path = $"/api/v1/products/{productId}/ledger-settings";

        using (var wrong = await _accountant1.PutAsJsonAsync(path, Settings(("INTEREST_EXPENSE", income))))
        {
            wrong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using (var put = await _accountant1.PutAsJsonAsync(path, Settings(("INTEREST_EXPENSE", expense), ("CHARGE_INCOME", income))))
        {
            put.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        using (var reject = await _accountant2.PostAsJsonAsync(path + "/reject", new { reason = "Use the savings expense account" }))
        {
            reject.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        var view = await ReadAsync(_accountant1, path);
        view.GetProperty("pending").ValueKind.ShouldBe(JsonValueKind.Null);
        Setting(view, LedgerSettingCodes.InterestExpense).GetProperty("ledgerAccountId").ValueKind.ShouldBe(JsonValueKind.Null);
        view.GetProperty("settings").GetArrayLength().ShouldBe(LedgerSettingCodes.Product.Count);
    }

    [Fact]
    public async Task Tellers_cannot_see_or_change_ledger_settings()
    {
        await _host.CreateStaffAsync("TELL9", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.Teller));
        using var teller = _host.Client(await _host.TokenAsync("TELL9"));
        using var read = await teller.GetAsync(new Uri($"/api/v1/branches/{_host.HomeBranchId}/ledger-settings", UriKind.Relative));
        read.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Limits_wait_for_approval_and_the_largest_across_a_persons_groups_applies()
    {
        var staffId = await _host.CreateStaffAsync(
            "TELL8",
            canSwitchBranch: false,
            (_host.HomeBranchId, RoleGroupCatalog.Teller),
            (_host.HomeBranchId, RoleGroupCatalog.Supervisor));
        var tellerGroup = await RoleGroupIdAsync(RoleGroupCatalog.Teller);
        var supervisorGroup = await RoleGroupIdAsync(RoleGroupCatalog.Supervisor);

        var request = await LimitsAsync(_admin1, tellerGroup, (TransactionLimitCodes.CashDeposit, 5_000m), (TransactionLimitCodes.CashWithdrawal, 2_000m));
        (await LimitAsync(staffId, TransactionLimitCodes.CashDeposit)).ShouldBeNull();

        using (var own = await _admin1.PostAsync(new Uri($"/api/v1/change-requests/{request}/approve", UriKind.Relative), null))
        {
            own.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }

        await ApproveChangeAsync(_admin2, request);
        (await LimitAsync(staffId, TransactionLimitCodes.CashDeposit)).ShouldBe(5_000m);
        (await LimitAsync(staffId, TransactionLimitCodes.Transfer)).ShouldBeNull();

        await ApproveChangeAsync(_admin2, await LimitsAsync(_admin1, supervisorGroup, (TransactionLimitCodes.CashDeposit, 20_000m)));
        (await LimitAsync(staffId, TransactionLimitCodes.CashDeposit)).ShouldBe(20_000m);
        (await LimitAsync(staffId, TransactionLimitCodes.CashWithdrawal)).ShouldBe(2_000m);
        (await LimitAsync(staffId, TransactionLimitCodes.CashDeposit, _otherBranchId)).ShouldBeNull();

        using var detail = await _admin1.GetAsync(new Uri($"/api/v1/role-groups/{tellerGroup}", UriKind.Relative));
        var limits = (await ApiTestHost.ReadJsonAsync(detail)).GetProperty("transactionLimits");
        limits.GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task Limits_are_validated()
    {
        var group = await RoleGroupIdAsync(RoleGroupCatalog.FieldOfficer);
        foreach (var body in new object[]
                 {
                     new { limits = new[] { new { limitCode = "CASH_DEPOSIT", maxAmount = -1m } } },
                     new { limits = new[] { new { limitCode = "CASH_DEPOSIT", maxAmount = 10.123m } } },
                     new { limits = new[] { new { limitCode = "LOANS", maxAmount = 10m } } },
                     new { limits = new[] { new { limitCode = "TRANSFER", maxAmount = 10m }, new { limitCode = "TRANSFER", maxAmount = 20m } } }
                 })
        {
            using var response = await _admin1.PutAsJsonAsync($"/api/v1/role-groups/{group}/transaction-limits", body);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
    }

    // ---------------------------------------------------------------- Helpers

    private static object Settings(params (string Code, long LedgerAccountId)[] values) =>
        new { settings = values.Select(value => new { settingCode = value.Code, ledgerAccountId = value.LedgerAccountId }).ToArray() };

    private static JsonElement Setting(JsonElement view, string code) =>
        view.GetProperty("settings").EnumerateArray().Single(row => row.GetProperty("settingCode").GetString() == code);

    private static async Task<JsonElement> ReadAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await ApiTestHost.ReadJsonAsync(response);
    }

    private static async Task<long> LimitsAsync(HttpClient client, int roleGroupId, params (string Code, decimal Amount)[] limits)
    {
        using var response = await client.PutAsJsonAsync(
            $"/api/v1/role-groups/{roleGroupId}/transaction-limits",
            new { limits = limits.Select(limit => new { limitCode = limit.Code, maxAmount = limit.Amount }).ToArray() });
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        return (await ApiTestHost.ReadJsonAsync(response)).GetProperty("changeRequestId").GetInt64();
    }

    private static async Task ApproveChangeAsync(HttpClient client, long changeRequestId)
    {
        using var response = await client.PostAsync(new Uri($"/api/v1/change-requests/{changeRequestId}/approve", UriKind.Relative), null);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private async Task<decimal?> LimitAsync(long staffUserId, string code, int? branchId = null)
    {
        using var scope = _host.Factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<TransactionLimitResolver>();
        return await resolver.LimitAsync(staffUserId, branchId ?? _host.HomeBranchId, code, CancellationToken.None);
    }

    private async Task<int> RoleGroupIdAsync(string code)
    {
        await using var context = _host.NewContext();
        return await context.Set<RoleGroup>().Where(row => row.RoleGroupCode == code).Select(row => row.RoleGroupId).SingleAsync();
    }

    private async Task<long> LedgerAccountAsync(string code, string typeCode, int branchId, bool approved = true)
    {
        await using var context = _host.NewContext();
        var typeId = await context.Set<LedgerAccountType>().Where(row => row.Code == typeCode).Select(row => row.Id).SingleAsync();
        var adminId = await context.Set<StaffUser>().Where(row => row.StaffCode == ApiTestHost.AdminCode).Select(row => row.StaffUserId).SingleAsync();
        var account = new LedgerAccount
        {
            BranchId = branchId,
            LedgerAccountCode = code,
            AccountName = "Test " + code,
            LedgerAccountTypeId = typeId,
            IsSupervisionRequired = true,
            SupervisedByUserId = approved ? adminId : null,
            SupervisedOn = approved ? DateTime.UtcNow : null,
            CreatedOn = DateTime.UtcNow
        };
        context.Add(account);
        await context.SaveChangesAsync();
        return account.LedgerAccountId;
    }

    private async Task<int> ProductAsync(string code)
    {
        await using var context = _host.NewContext();
        var typeId = await context.Set<ProductType>().Where(row => row.Code == LookupCodes.ProductTypes.Savings).Select(row => row.Id).SingleAsync();
        var currencyId = await context.Set<Currency>().Where(row => row.IsLocalCurrency).Select(row => row.CurrencyId).SingleAsync();
        var product = new Product
        {
            BranchId = _host.HomeBranchId,
            ProductCode = code,
            ProductName = "Test savings",
            ProductTypeId = typeId,
            AccountPrefix = "19",
            CurrencyId = currencyId,
            IsSupervisionRequired = false,
            CreatedOn = DateTime.UtcNow
        };
        context.Add(product);
        await context.SaveChangesAsync();
        return product.ProductId;
    }
}
