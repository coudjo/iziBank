using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Application.Errors;
using Banking.Domain.Ledger;
using Banking.Domain.Security;
using Banking.Domain.Supervision;

namespace Banking.Api.IntegrationTests.Ledger;

/// <summary>A3-03: chart of accounts with four-eyes approval, locked fields, and the teller till-account rule.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class LedgerAccountTests : IAsyncLifetime
{
    private static readonly Dictionary<string, string?> FourEyes = new(StringComparer.Ordinal)
    {
        ["Supervision:AllowSelfApproval"] = "false"
    };

    private readonly MsSqlContainerFixture _fixture;
    private ApiTestHost _host = null!;
    private HttpClient _maker = null!;
    private HttpClient _checker = null!;
    private Dictionary<string, int> _types = null!;
    private int _ghsId;

    public LedgerAccountTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _host = await ApiTestHost.CreateAsync(_fixture, "ledger", FourEyes);
        _maker = _host.Client(await _host.AdminTokenAsync());
        await _host.CreateStaffAsync("CHECKER1", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.Supervisor));
        _checker = _host.Client(await _host.TokenAsync("CHECKER1"));

        using var types = await _maker.GetAsync(new Uri("/api/v1/lookups/ledger-account-types", UriKind.Relative));
        _types = (await ApiTestHost.ReadJsonAsync(types)).EnumerateArray()
            .ToDictionary(row => row.GetProperty("code").GetString()!, row => row.GetProperty("id").GetInt32(), StringComparer.Ordinal);

        using var currencies = await _maker.GetAsync(new Uri("/api/v1/currencies", UriKind.Relative));
        _ghsId = (await ApiTestHost.ReadJsonAsync(currencies)).EnumerateArray()
            .Single(row => row.GetProperty("currencyCode").GetString() == "GHS")
            .GetProperty("currencyId").GetInt32();
    }

    public async Task DisposeAsync()
    {
        _maker.Dispose();
        _checker.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task A_new_account_is_pending_until_a_different_supervisor_approves_it()
    {
        var id = await CreateAsync("1010-01", "Vault cash", "ASSET");
        (await GetAsync(id)).GetProperty("supervisionState").GetString().ShouldBe("PENDING");

        using var duplicate = await _maker.PostAsJsonAsync("/api/v1/ledger-accounts", Body("1010-01", "Again", "ASSET"));
        await ExpectAsync(duplicate, HttpStatusCode.Conflict, ProblemCodes.ReferenceCodeTaken);

        await ExpectAsync(await ApproveAsync(_maker, id), HttpStatusCode.Conflict, SupervisionProblems.SelfApproval);

        (await ApproveAsync(_checker, id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GetAsync(id)).GetProperty("supervisionState").GetString().ShouldBe("APPROVED");

        await ExpectAsync(await ApproveAsync(_checker, id), HttpStatusCode.Conflict, SupervisionProblems.NotPending);
    }

    [Fact]
    public async Task A_pending_account_is_edited_in_place_and_can_be_rejected()
    {
        var id = await CreateAsync("4010-01", "Fees", "INCOME");

        using var edited = await _maker.PutAsJsonAsync($"/api/v1/ledger-accounts/{id}", Body(null, "Fee income", "EXPENSE"));
        edited.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var account = await GetAsync(id);
        account.GetProperty("accountName").GetString().ShouldBe("Fee income");
        account.GetProperty("ledgerAccountTypeCode").GetString().ShouldBe("EXPENSE");
        account.GetProperty("supervisionState").GetString().ShouldBe("PENDING");

        // Supervisors approve but do not edit the chart (SUPERVISOR has no LED/CHART edit).
        (await _checker.PutAsJsonAsync($"/api/v1/ledger-accounts/{id}", Body(null, "Fees", "INCOME"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await RejectAsync(_checker, id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await _maker.GetAsync(new Uri($"/api/v1/ledger-accounts/{id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_approved_account_keeps_its_type_currency_and_code_and_a_rename_needs_approval()
    {
        var id = await CreateAsync("2010-01", "Customer savings", "LIABILITY");
        (await ApproveAsync(_checker, id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var retype = await _maker.PutAsJsonAsync($"/api/v1/ledger-accounts/{id}", Body(null, "Customer savings", "ASSET"));
        await ExpectAsync(retype, HttpStatusCode.Conflict, ProblemCodes.LedgerFieldLocked);

        using var recode = await _maker.PutAsJsonAsync($"/api/v1/ledger-accounts/{id}", Body("2010-02", "Customer savings", "LIABILITY"));
        await ExpectAsync(recode, HttpStatusCode.BadRequest, ProblemCodes.ReferenceCodeImmutable);

        using var rename = await _maker.PutAsJsonAsync($"/api/v1/ledger-accounts/{id}", Body(null, "Savings deposits", "LIABILITY"));
        rename.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var pending = await GetAsync(id);
        pending.GetProperty("accountName").GetString().ShouldBe("Customer savings");
        pending.GetProperty("supervisionState").GetString().ShouldBe("APPROVED");
        pending.GetProperty("pendingChange").GetProperty("proposedName").GetString().ShouldBe("Savings deposits");

        using var second = await _maker.PutAsJsonAsync($"/api/v1/ledger-accounts/{id}", Body(null, "Other name", "LIABILITY"));
        await ExpectAsync(second, HttpStatusCode.Conflict, ProblemCodes.ChangePendingExists);

        // Ledger change requests stay out of the security approval list.
        using var security = await _maker.GetAsync(new Uri("/api/v1/change-requests", UriKind.Relative));
        (await ApiTestHost.ReadJsonAsync(security)).EnumerateArray()
            .ShouldNotContain(row => row.GetProperty("entityType").GetString() == ChangeRequestEntities.LedgerAccount);

        await ExpectAsync(await ApproveAsync(_maker, id), HttpStatusCode.Conflict, SupervisionProblems.SelfApproval);
        (await ApproveAsync(_checker, id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var renamed = await GetAsync(id);
        renamed.GetProperty("accountName").GetString().ShouldBe("Savings deposits");
        renamed.GetProperty("pendingChange").ValueKind.ShouldBe(JsonValueKind.Null);

        // An approved account cannot be rejected; a pending rename can, and leaves the name alone.
        await ExpectAsync(await RejectAsync(_checker, id), HttpStatusCode.Conflict, SupervisionProblems.NotPending);
        (await _maker.PutAsJsonAsync($"/api/v1/ledger-accounts/{id}", Body(null, "Wrong name", "LIABILITY"))).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await RejectAsync(_checker, id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GetAsync(id)).GetProperty("accountName").GetString().ShouldBe("Savings deposits");
    }

    [Fact]
    public async Task Accounts_of_another_branch_are_read_only_and_tellers_only_read_the_chart()
    {
        var otherBranch = await _host.CreateBranchAsync("LG2");
        var foreignId = await InsertApprovedAsync("1010-77", "Other vault", "ASSET", otherBranch);

        using var edit = await _maker.PutAsJsonAsync($"/api/v1/ledger-accounts/{foreignId}", Body(null, "Mine now", "ASSET"));
        await ExpectAsync(edit, HttpStatusCode.Conflict, ProblemCodes.RecordOtherBranch);

        await _host.CreateStaffAsync("TELLER51", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.Teller));
        using var teller = _host.Client(await _host.TokenAsync("TELLER51"));
        (await teller.GetAsync(new Uri("/api/v1/ledger-accounts", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await teller.PostAsJsonAsync("/api/v1/ledger-accounts", Body("1010-99", "Till", "ASSET"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_teller_needs_an_approved_asset_till_account_in_their_own_branch()
    {
        var income = await InsertApprovedAsync("4020-01", "Commission", "INCOME", _host.HomeBranchId);
        var pendingAsset = await CreateAsync("1020-01", "Till 1 (pending)", "ASSET");
        var elsewhere = await InsertApprovedAsync("1020-02", "Till in another branch", "ASSET", await _host.CreateBranchAsync("TL2"));
        var till = await InsertApprovedAsync("1020-03", "Till 3", "ASSET", _host.HomeBranchId);

        long?[] wrongAccounts = [null, income, pendingAsset, elsewhere];
        for (var index = 0; index < wrongAccounts.Length; index++)
        {
            using var refused = await CreateTellerAsync($"KOFIX{index}", wrongAccounts[index]);
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await ApiTestHost.ReadJsonAsync(refused)).GetProperty("errors").TryGetProperty("tellerGLAccountId", out _).ShouldBeTrue();
        }

        using var created = await CreateTellerAsync("KOFI1", till);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var staffId = (await ApiTestHost.ReadJsonAsync(created)).GetProperty("id").GetInt64();

        using var detail = await _maker.GetAsync(new Uri($"/api/v1/staff-users/{staffId}", UriKind.Relative));
        var staff = await ApiTestHost.ReadJsonAsync(detail);
        staff.GetProperty("isTeller").GetBoolean().ShouldBeTrue();
        staff.GetProperty("tellerGLAccountId").GetInt64().ShouldBe(till);
    }

    private Task<HttpResponseMessage> CreateTellerAsync(string code, long? tellerAccountId) =>
        _maker.PostAsJsonAsync("/api/v1/staff-users", new
        {
            staffCode = code,
            fullName = "Kofi Boateng",
            branchId = _host.HomeBranchId,
            canSwitchBranch = false,
            initialSecretKey = "Welcome-2026",
            isTeller = true,
            tellerGLAccountId = tellerAccountId
        });

    private object Body(string? code, string name, string type) =>
        new { ledgerAccountCode = code, accountName = name, ledgerAccountTypeId = _types[type], currencyId = _ghsId };

    private async Task<long> CreateAsync(string code, string name, string type)
    {
        using var response = await _maker.PostAsJsonAsync("/api/v1/ledger-accounts", Body(code, name, type));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await ApiTestHost.ReadJsonAsync(response)).GetProperty("id").GetInt64();
    }

    private async Task<long> InsertApprovedAsync(string code, string name, string type, int branchId)
    {
        await using var context = _host.NewContext();
        var account = new LedgerAccount
        {
            BranchId = branchId,
            LedgerAccountCode = code,
            AccountName = name,
            LedgerAccountTypeId = _types[type],
            CurrencyId = _ghsId,
            IsSupervisionRequired = false
        };
        context.Add(account);
        await context.SaveChangesAsync();
        return account.LedgerAccountId;
    }

    private async Task<JsonElement> GetAsync(long id)
    {
        using var response = await _maker.GetAsync(new Uri($"/api/v1/ledger-accounts/{id}", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await ApiTestHost.ReadJsonAsync(response);
    }

    private static Task<HttpResponseMessage> ApproveAsync(HttpClient client, long id) =>
        client.PostAsync(new Uri($"/api/v1/ledger-accounts/{id}/approve", UriKind.Relative), content: null);

    private static Task<HttpResponseMessage> RejectAsync(HttpClient client, long id) =>
        client.PostAsJsonAsync($"/api/v1/ledger-accounts/{id}/reject", new { reason = "Not needed" });

    private static async Task ExpectAsync(HttpResponseMessage response, HttpStatusCode status, string problemCode)
    {
        using (response)
        {
            response.StatusCode.ShouldBe(status);
            (await ApiTestHost.ProblemTypeAsync(response)).ShouldBe(problemCode);
        }
    }
}
