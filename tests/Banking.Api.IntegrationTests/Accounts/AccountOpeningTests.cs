using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Application.Accounts;
using Banking.Application.Errors;
using Banking.Domain.Accounts;
using Banking.Domain.Institution;
using Banking.Domain.Ledger;
using Banking.Domain.Lookups;
using Banking.Domain.Parties;
using Banking.Domain.Products;
using Banking.Domain.Security;
using Banking.Domain.Supervision;
using Banking.Infrastructure.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Banking.Api.IntegrationTests.Accounts;

/// <summary>A5-03 numbers, A5-04 opening (one test per rule in plan 6.9 and addendum 3a), A5-05 balance read.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class AccountOpeningTests : IAsyncLifetime
{
    private const string WorkingDate = "2030-01-07";

    private static readonly Dictionary<string, string?> FourEyes = new(StringComparer.Ordinal)
    {
        ["Supervision:AllowSelfApproval"] = "false"
    };

    private readonly MsSqlContainerFixture _fixture;
    private ApiTestHost _host = null!;
    private HttpClient _teller = null!;
    private HttpClient _checker = null!;
    private Dictionary<string, int> _roles = null!;
    private Dictionary<string, int> _modes = null!;
    private long _adminId;
    private int _partyCount;
    private int _savings;

    public AccountOpeningTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _host = await ApiTestHost.CreateAsync(_fixture, "accounts", FourEyes);
        using var admin = _host.Client(await _host.AdminTokenAsync());
        using (var open = await admin.PostAsJsonAsync($"/api/v1/branches/{_host.HomeBranchId}/business-days/open", new { workingDate = WorkingDate }))
        {
            open.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        await _host.CreateStaffAsync("TELLER1", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.Teller));
        await _host.CreateStaffAsync("CHECKER1", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.Supervisor));
        _teller = _host.Client(await _host.TokenAsync("TELLER1"));
        _checker = _host.Client(await _host.TokenAsync("CHECKER1"));

        await using var context = _host.NewContext();
        _roles = await context.Set<AccountHolderRoleType>().ToDictionaryAsync(row => row.Code, row => row.Id, StringComparer.Ordinal);
        _modes = await context.Set<OperatingModeType>().ToDictionaryAsync(row => row.Code, row => row.Id, StringComparer.Ordinal);
        _adminId = await context.Set<StaffUser>().Where(row => row.StaffCode == ApiTestHost.AdminCode).Select(row => row.StaffUserId).SingleAsync();
        _savings = await InsertProductAsync("SAV01", LookupCodes.ProductTypes.Savings, "10");
    }

    public async Task DisposeAsync()
    {
        _teller.Dispose();
        _checker.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task An_individual_account_opens_pending_on_the_working_date_with_zero_balances()
    {
        var ama = await PartyAsync(LookupCodes.PartyTypes.Individual, "Ama Mensah");

        using var response = await OpenAsync(_teller, _savings, null, Holder(ama, "PRIMARY"));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var opened = await ApiTestHost.ReadJsonAsync(response);
        var number = opened.GetProperty("accountNumber").GetString()!;
        number.Length.ShouldBe(13);
        number.ShouldStartWith("10");
        opened.GetProperty("accountName").GetString().ShouldBe("Ama Mensah");
        opened.GetProperty("status").GetString().ShouldBe(AccountStatuses.PendingSupervision);
        var id = opened.GetProperty("accountId").GetInt64();

        var account = await GetAsync(id);
        account.GetProperty("dateOpened").GetString().ShouldBe(WorkingDate);
        account.GetProperty("operatingModeCode").GetString().ShouldBe(LookupCodes.OperatingModes.SingleMode);
        account.GetProperty("holders").GetArrayLength().ShouldBe(1);
        account.GetProperty("holders")[0].GetProperty("roleCode").GetString().ShouldBe("PRIMARY");
        account.GetProperty("condition").GetProperty("isFreezed").GetBoolean().ShouldBeFalse();

        using var balanceResponse = await _teller.GetAsync(new Uri($"/api/v1/accounts/{id}/balance", UriKind.Relative));
        var balance = await ApiTestHost.ReadJsonAsync(balanceResponse);
        balance.GetProperty("clearBalance").GetDecimal().ShouldBe(0m);
        balance.GetProperty("availableBalance").GetDecimal().ShouldBe(0m);
        balance.GetProperty("totalBalance").GetDecimal().ShouldBe(0m);
        balance.GetProperty("currencyCode").GetString().ShouldBe("GHS");

        // Tellers open accounts; a different supervisor approves them.
        (await SuperviseAsync(_teller, id)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SuperviseAsync(_checker, id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GetAsync(id)).GetProperty("status").GetString().ShouldBe(AccountStatuses.Active);
        await ExpectAsync(await SuperviseAsync(_checker, id), HttpStatusCode.Conflict, SupervisionProblems.NotPending);
    }

    [Fact]
    public async Task Without_an_open_business_day_no_account_opens()
    {
        var otherBranch = await _host.CreateBranchAsync("NB2");
        await _host.CreateStaffAsync("TELLER2", canSwitchBranch: true, (otherBranch, RoleGroupCatalog.Teller));
        using var teller = _host.Client(await _host.TokenAsync("TELLER2", branchId: otherBranch));
        var kofi = await PartyAsync(LookupCodes.PartyTypes.Individual, "Kofi Boateng");

        await ExpectAsync(await OpenAsync(teller, _savings, null, Holder(kofi, "PRIMARY")), HttpStatusCode.Conflict, ProblemCodes.BusinessDayNotOpen);
    }

    [Fact]
    public async Task Only_approved_active_unfrozen_savings_or_current_products_can_be_opened()
    {
        var yaw = await PartyAsync(LookupCodes.PartyTypes.Individual, "Yaw Asante");
        var loan = await InsertProductAsync("LN01", LookupCodes.ProductTypes.Loan, "30");
        var frozen = await InsertProductAsync("SAV02", LookupCodes.ProductTypes.Savings, "11", frozen: true);
        var pending = await InsertProductAsync("SAV03", LookupCodes.ProductTypes.Savings, "12", approved: false);
        var inactive = await InsertProductAsync("SAV04", LookupCodes.ProductTypes.Savings, "13", active: false);

        foreach (var product in new[] { loan, frozen, pending, inactive })
        {
            await ExpectAsync(await OpenAsync(_teller, product, null, Holder(yaw, "PRIMARY")), HttpStatusCode.Conflict, ProblemCodes.AccountProductNotOpenable);
        }

        (await OpenAsync(_teller, null, null, Holder(yaw, "PRIMARY"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Every_holder_must_be_approved_and_kyc_verified()
    {
        var verified = await PartyAsync(LookupCodes.PartyTypes.Individual, "Efua Owusu");
        var unverified = await PartyAsync(LookupCodes.PartyTypes.Individual, "Esi Owusu", kyc: LookupCodes.KycStatuses.Pending);
        var awaiting = await PartyAsync(LookupCodes.PartyTypes.Individual, "Akua Owusu", approved: false);
        var owner = await PartyAsync(LookupCodes.PartyTypes.Business, "Owusu Holdings", kyc: LookupCodes.KycStatuses.Rejected, signature: false);

        using var response = await OpenAsync(
            _teller,
            _savings,
            null,
            Holder(verified, "PRIMARY"),
            Holder(unverified, "SIGNATORY"),
            Holder(awaiting, "SIGNATORY"),
            Holder(owner, "BENEFICIAL_OWNER"));
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await ApiTestHost.ReadJsonAsync(response);
        problem.GetProperty("type").GetString().ShouldBe(ProblemCodes.AccountHolderNotReady);
        var expected = new[] { await NumberAsync(unverified), await NumberAsync(awaiting), await NumberAsync(owner) }.Order(StringComparer.Ordinal).ToArray();
        problem.GetProperty("partyNumbers").EnumerateArray().Select(row => row.GetString()!).Order(StringComparer.Ordinal).ToArray().ShouldBe(expected);
    }

    [Fact]
    public async Task Everyone_who_signs_needs_an_approved_specimen_signature()
    {
        var holder = await PartyAsync(LookupCodes.PartyTypes.Individual, "Kwame Darko", signature: false);
        await ExpectAsync(await OpenAsync(_teller, _savings, null, Holder(holder, "PRIMARY")), HttpStatusCode.Conflict, ProblemCodes.AccountSignatureMissing);

        var pendingSignature = await PartyAsync(LookupCodes.PartyTypes.Individual, "Abena Darko", signature: false);
        await AddSignatureAsync(pendingSignature, approved: false);
        await ExpectAsync(await OpenAsync(_teller, _savings, null, Holder(pendingSignature, "PRIMARY")), HttpStatusCode.Conflict, ProblemCodes.AccountSignatureMissing);

        // A business does not sign; its signatory does.
        var business = await PartyAsync(LookupCodes.PartyTypes.Business, "Darko Traders", signature: false);
        using var refused = await OpenAsync(_teller, _savings, "EITHER_TO_SIGN", Holder(business, "PRIMARY"), Holder(holder, "SIGNATORY"));
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApiTestHost.ReadJsonAsync(refused)).GetProperty("partyNumbers")[0].GetString().ShouldBe(await NumberAsync(holder));

        var signer = await PartyAsync(LookupCodes.PartyTypes.Individual, "Yaa Darko");
        (await OpenAsync(_teller, _savings, "EITHER_TO_SIGN", Holder(business, "PRIMARY"), Holder(signer, "SIGNATORY"))).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_joint_account_needs_a_signing_mode_and_is_named_after_both_holders()
    {
        var first = await PartyAsync(LookupCodes.PartyTypes.Individual, "Adwoa Frimpong");
        var second = await PartyAsync(LookupCodes.PartyTypes.Individual, "Kojo Frimpong");

        using var noMode = await OpenAsync(_teller, _savings, null, Holder(first, "PRIMARY"), Holder(second, "JOINT"));
        await ExpectFieldAsync(noMode, "operatingModeId");

        using var opened = await OpenAsync(_teller, _savings, "ALL_TO_SIGN", Holder(first, "PRIMARY"), Holder(second, "JOINT"));
        opened.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await ApiTestHost.ReadJsonAsync(opened);
        body.GetProperty("accountName").GetString().ShouldBe("Adwoa Frimpong & Kojo Frimpong");
        (await GetAsync(body.GetProperty("accountId").GetInt64())).GetProperty("operatingModeCode").GetString().ShouldBe("ALL_TO_SIGN");
    }

    [Fact]
    public async Task A_business_account_needs_an_individual_signatory()
    {
        var business = await PartyAsync(LookupCodes.PartyTypes.Business, "Mensah Foods", signature: false);
        var otherBusiness = await PartyAsync(LookupCodes.PartyTypes.Business, "Mensah Farms", signature: false);

        await ExpectFieldAsync(await OpenAsync(_teller, _savings, "EITHER_TO_SIGN", Holder(business, "PRIMARY")), "holders");
        await ExpectFieldAsync(await OpenAsync(_teller, _savings, "EITHER_TO_SIGN", Holder(business, "PRIMARY"), Holder(otherBusiness, "SIGNATORY")), "holders");
    }

    [Fact]
    public async Task A_group_account_is_signed_by_active_members_only()
    {
        var member = await PartyAsync(LookupCodes.PartyTypes.Individual, "Esi Badu");
        var outsider = await PartyAsync(LookupCodes.PartyTypes.Individual, "Ekow Badu");
        var group = await PartyAsync(LookupCodes.PartyTypes.Group, "Badu Susu Group", signature: false);
        await using (var context = _host.NewContext())
        {
            context.Add(new GroupMember { GroupPartyId = group, MemberPartyId = member, JoinedOn = new DateOnly(2029, 1, 1) });
            await context.SaveChangesAsync();
        }

        await ExpectFieldAsync(await OpenAsync(_teller, _savings, "EITHER_TO_SIGN", Holder(group, "PRIMARY"), Holder(outsider, "SIGNATORY")), "holders");
        (await OpenAsync(_teller, _savings, "EITHER_TO_SIGN", Holder(group, "PRIMARY"), Holder(member, "SIGNATORY"))).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task The_accounts_per_customer_limit_holds_even_under_parallel_requests()
    {
        var limited = await InsertProductAsync("CUR01", LookupCodes.ProductTypes.Current, "20", accountsPerCustomer: 1);
        var customer = await PartyAsync(LookupCodes.PartyTypes.Individual, "Nana Ofori");

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => OpenAsync(_teller, limited, null, Holder(customer, "PRIMARY"))));
        try
        {
            responses.Count(response => response.StatusCode == HttpStatusCode.Created).ShouldBe(1);
            foreach (var response in responses.Where(response => response.StatusCode != HttpStatusCode.Created))
            {
                (await ApiTestHost.ProblemTypeAsync(response)).ShouldBe(ProblemCodes.AccountLimitReached);
            }
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        // As a joint holder (not primary) the customer is not counted.
        var other = await PartyAsync(LookupCodes.PartyTypes.Individual, "Ama Ofori");
        (await OpenAsync(_teller, limited, "EITHER_TO_SIGN", Holder(other, "PRIMARY"), Holder(customer, "JOINT"))).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_retry_with_the_same_idempotency_key_returns_the_first_account()
    {
        var customer = await PartyAsync(LookupCodes.PartyTypes.Individual, "Kwesi Amoah");
        var body = Body(_savings, null, Holder(customer, "PRIMARY"));
        var key = Guid.NewGuid().ToString("N");

        using var first = await PostWithKeyAsync(body, key);
        using var second = await PostWithKeyAsync(body, key);
        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await ApiTestHost.ReadJsonAsync(second)).GetProperty("accountNumber").GetString()
            .ShouldBe((await ApiTestHost.ReadJsonAsync(first)).GetProperty("accountNumber").GetString());

        await using var context = _host.NewContext();
        (await context.Set<AccountHolder>().CountAsync(row => row.PartyId == customer)).ShouldBe(1);
    }

    [Fact]
    public async Task A_pending_account_can_be_rejected_and_is_removed()
    {
        var customer = await PartyAsync(LookupCodes.PartyTypes.Individual, "Afua Sarpong");
        using var opened = await OpenAsync(_teller, _savings, null, Holder(customer, "PRIMARY"));
        var id = (await ApiTestHost.ReadJsonAsync(opened)).GetProperty("accountId").GetInt64();

        using var reject = await _checker.PostAsJsonAsync($"/api/v1/accounts/{id}/reject", new { reason = "Opened in error" });
        reject.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await _teller.GetAsync(new Uri($"/api/v1/accounts/{id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task One_thousand_parallel_numbers_are_unique_and_of_the_configured_length()
    {
        var options = Options.Create(new AccountNumberOptions { Length = 13 });
        var numbers = new System.Collections.Concurrent.ConcurrentBag<string>();

        await Parallel.ForEachAsync(Enumerable.Range(0, 1000), new ParallelOptions { MaxDegreeOfParallelism = 32 }, async (_, cancellationToken) =>
        {
            await using var context = _host.NewContext();
            numbers.Add(await new AccountNumbers(context, options).NextAsync("10", cancellationToken));
        });

        numbers.Count.ShouldBe(1000);
        numbers.Distinct(StringComparer.Ordinal).Count().ShouldBe(1000);
        numbers.ShouldAllBe(number => number.Length == 13 && number.StartsWith("10", StringComparison.Ordinal));
    }

    private static HolderRef Holder(long partyId, string role) => new(partyId, role);

    private object Body(int? productId, string? mode, params HolderRef[] holders) =>
        new
        {
            productId,
            operatingModeId = mode is null ? (int?)null : _modes[mode],
            holders = holders.Select(holder => new { partyId = holder.PartyId, accountHolderRoleId = _roles[holder.Role] }).ToList()
        };

    private Task<HttpResponseMessage> OpenAsync(HttpClient client, int? productId, string? mode, params HolderRef[] holders) =>
        client.PostAsJsonAsync("/api/v1/accounts", Body(productId, mode, holders));

    private async Task<HttpResponseMessage> PostWithKeyAsync(object body, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/accounts", UriKind.Relative)) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key);
        return await _teller.SendAsync(request);
    }

    private static Task<HttpResponseMessage> SuperviseAsync(HttpClient client, long id) =>
        client.PostAsync(new Uri($"/api/v1/accounts/{id}/supervise", UriKind.Relative), content: null);

    private async Task<JsonElement> GetAsync(long id)
    {
        using var response = await _teller.GetAsync(new Uri($"/api/v1/accounts/{id}", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await ApiTestHost.ReadJsonAsync(response);
    }

    private async Task<long> PartyAsync(string type, string name, string kyc = LookupCodes.KycStatuses.Verified, bool approved = true, bool signature = true)
    {
        await using var context = _host.NewContext();
        var number = Interlocked.Increment(ref _partyCount);
        var party = new Party
        {
            PartyNumber = $"T{number:D7}",
            DisplayName = name,
            PartyTypeId = await context.Set<PartyType>().Where(row => row.Code == type).Select(row => row.Id).SingleAsync(),
            KYCStatusId = await context.Set<KYCStatusType>().Where(row => row.Code == kyc).Select(row => row.Id).SingleAsync(),
            IsSupervisionRequired = !approved
        };
        context.Add(party);
        await context.SaveChangesAsync();

        if (signature)
        {
            await AddSignatureAsync(party.PartyId, approved: true);
        }

        return party.PartyId;
    }

    private async Task AddSignatureAsync(long partyId, bool approved)
    {
        await using var context = _host.NewContext();
        context.Add(new PartySignature
        {
            PartyId = partyId,
            ImageValue = [0x89, 0x50, 0x4E, 0x47],
            CapturedOn = DateTime.UtcNow,
            CapturedByUserId = _adminId,
            IsSupervisionRequired = !approved
        });
        await context.SaveChangesAsync();
    }

    private async Task<string> NumberAsync(long partyId)
    {
        await using var context = _host.NewContext();
        return await context.Set<Party>().Where(row => row.PartyId == partyId).Select(row => row.PartyNumber).SingleAsync();
    }

    private async Task<int> InsertProductAsync(
        string code,
        string type,
        string prefix,
        bool approved = true,
        bool frozen = false,
        bool active = true,
        short? accountsPerCustomer = null)
    {
        await using var context = _host.NewContext();
        var ghs = await context.Set<Currency>().Where(row => row.CurrencyCode == "GHS").Select(row => row.CurrencyId).SingleAsync();
        var ledgerType = type == LookupCodes.ProductTypes.Loan ? LookupCodes.LedgerAccountTypes.Asset : LookupCodes.LedgerAccountTypes.Liability;
        var control = new LedgerAccount
        {
            BranchId = _host.HomeBranchId,
            LedgerAccountCode = "C-" + code,
            AccountName = "Control " + code,
            LedgerAccountTypeId = await context.Set<LedgerAccountType>().Where(row => row.Code == ledgerType).Select(row => row.Id).SingleAsync(),
            CurrencyId = ghs,
            IsSupervisionRequired = false
        };
        context.Add(control);
        await context.SaveChangesAsync();

        var product = new Product
        {
            BranchId = _host.HomeBranchId,
            ProductCode = code,
            ProductName = "Product " + code,
            ProductTypeId = await context.Set<ProductType>().Where(row => row.Code == type).Select(row => row.Id).SingleAsync(),
            AccountPrefix = prefix,
            CurrencyId = ghs,
            LedgerControlAccountId = control.LedgerAccountId,
            AccountsPerCustomer = accountsPerCustomer,
            IsFreezed = frozen,
            IsActive = active,
            IsSupervisionRequired = !approved
        };
        context.Add(product);
        await context.SaveChangesAsync();
        return product.ProductId;
    }

    private static async Task ExpectFieldAsync(HttpResponseMessage response, string field)
    {
        using (response)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await ApiTestHost.ReadJsonAsync(response)).GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue(field);
        }
    }

    private static async Task ExpectAsync(HttpResponseMessage response, HttpStatusCode status, string problemCode)
    {
        using (response)
        {
            response.StatusCode.ShouldBe(status);
            (await ApiTestHost.ProblemTypeAsync(response)).ShouldBe(problemCode);
        }
    }

    private sealed record HolderRef(long PartyId, string Role);
}
