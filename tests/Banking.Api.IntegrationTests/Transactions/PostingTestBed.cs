using System.Net.Http.Json;
using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Domain.Accounts;
using Banking.Domain.Institution;
using Banking.Domain.Ledger;
using Banking.Domain.Lookups;
using Banking.Domain.Parties;
using Banking.Domain.Products;
using Banking.Domain.Security;
using Banking.Domain.Transactions;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Transactions;
using Banking.Migrator.Sample;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Banking.Api.IntegrationTests.Transactions;

/// <summary>
/// Shared setup for posting tests: open business days in two branches, a savings product with a control account,
/// branch ledger settings (vault, cash short and over, inter-branch, suspense), and helpers to create funded accounts
/// through the engine.
/// </summary>
internal sealed class PostingTestBed
{
    private int _sequence;

    private PostingTestBed(ApiTestHost host)
    {
        Host = host;
    }

    public ApiTestHost Host { get; }

    public int HomeBranchId => Host.HomeBranchId;

    public int OtherBranchId { get; private set; }

    public SampleProduct Savings { get; private set; } = null!;

    public long ControlId { get; private set; }

    public long SuspenseId { get; private set; }

    public long VaultId { get; private set; }

    public long CashShortId { get; private set; }

    public long CashOverId { get; private set; }

    /// <summary>The savings product's charge income account (B5-01).</summary>
    public long ChargeIncomeId { get; private set; }

    public long SetupStaffId { get; private set; }

    public long SetupSupervisorId { get; private set; }

    public static async Task<PostingTestBed> CreateAsync(MsSqlContainerFixture fixture, string name, IReadOnlyDictionary<string, string?>? settings = null)
    {
        var all = new Dictionary<string, string?>(StringComparer.Ordinal) { ["Supervision:AllowSelfApproval"] = "false" };
        foreach (var pair in settings ?? new Dictionary<string, string?>())
        {
            all[pair.Key] = pair.Value;
        }

        var bed = new PostingTestBed(await ApiTestHost.CreateAsync(fixture, name, all));
        await bed.SetUpAsync();
        return bed;
    }

    private async Task SetUpAsync()
    {
        OtherBranchId = await Host.CreateBranchAsync("002");
        SetupStaffId = await Host.CreateStaffAsync("SETUP1", canSwitchBranch: false, (HomeBranchId, RoleGroupCatalog.Teller));
        SetupSupervisorId = await Host.CreateStaffAsync("SETUPSUP", canSwitchBranch: false, (HomeBranchId, RoleGroupCatalog.Supervisor));

        await using var context = Host.NewContext();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var open = await context.Set<BusinessDayStatusType>().Where(row => row.Code == LookupCodes.BusinessDayStatuses.Open).Select(row => row.Id).SingleAsync();
        foreach (var branchId in new[] { HomeBranchId, OtherBranchId })
        {
            context.Add(new BusinessDayStatus { BranchId = branchId, WorkingDate = today, BusinessDayStart = DateTime.UtcNow, CurrentStatusId = open, CreatedOn = DateTime.UtcNow });
        }

        await context.SaveChangesAsync();

        ControlId = await LedgerAccountAsync(context, "2001-01", LookupCodes.LedgerAccountTypes.Liability, HomeBranchId);
        SuspenseId = await LedgerAccountAsync(context, "1901-01", LookupCodes.LedgerAccountTypes.Asset, HomeBranchId);
        VaultId = await LedgerAccountAsync(context, "1001-01", LookupCodes.LedgerAccountTypes.Asset, HomeBranchId);
        CashShortId = await LedgerAccountAsync(context, "5901-01", LookupCodes.LedgerAccountTypes.Expense, HomeBranchId);
        CashOverId = await LedgerAccountAsync(context, "4901-01", LookupCodes.LedgerAccountTypes.Income, HomeBranchId);
        var interHome = await LedgerAccountAsync(context, "1801-01", LookupCodes.LedgerAccountTypes.Asset, HomeBranchId);
        var interOther = await LedgerAccountAsync(context, "1801-02", LookupCodes.LedgerAccountTypes.Asset, OtherBranchId);

        void Setting(int branchId, string code, long ledgerAccountId) =>
            context.Add(new SystemLedgerSetting { BranchId = branchId, SettingCode = code, LedgerAccountId = ledgerAccountId, CreatedOn = DateTime.UtcNow });

        Setting(HomeBranchId, LedgerSettingCodes.Vault, VaultId);
        Setting(HomeBranchId, LedgerSettingCodes.CashShort, CashShortId);
        Setting(HomeBranchId, LedgerSettingCodes.CashOver, CashOverId);
        Setting(HomeBranchId, LedgerSettingCodes.Suspense, SuspenseId);
        Setting(HomeBranchId, LedgerSettingCodes.InterBranch, interHome);
        Setting(OtherBranchId, LedgerSettingCodes.InterBranch, interOther);

        var typeId = await context.Set<ProductType>().Where(row => row.Code == LookupCodes.ProductTypes.Savings).Select(row => row.Id).SingleAsync();
        var currencyId = await context.Set<Currency>().Where(row => row.IsLocalCurrency).Select(row => row.CurrencyId).SingleAsync();
        var product = new Product
        {
            BranchId = HomeBranchId,
            ProductCode = "SAVBED",
            ProductName = "Test savings",
            ProductTypeId = typeId,
            AccountPrefix = "16",
            CurrencyId = currencyId,
            LedgerControlAccountId = ControlId,
            MinimumBalance = 0m,
            IsSupervisionRequired = false,
            CreatedOn = DateTime.UtcNow
        };
        context.Add(product);
        await context.SaveChangesAsync();
        Savings = new SampleProduct(product.ProductId, product.ProductCode, product.AccountPrefix);

        // Plan B 2.2: the product's charge income account, so per-transaction charges can post (B5-01).
        ChargeIncomeId = await LedgerAccountAsync(context, "4801-01", LookupCodes.LedgerAccountTypes.Income, HomeBranchId);
        context.Add(new ProductLedgerSetting { ProductId = product.ProductId, SettingCode = LedgerSettingCodes.ChargeIncome, LedgerAccountId = ChargeIncomeId, CreatedOn = DateTime.UtcNow });
        await context.SaveChangesAsync();
    }

    /// <summary>A staff member who is a teller with their own till ledger account in the home branch.</summary>
    public async Task<(long StaffUserId, long TillAccountId)> TellerAsync(string code, params (int BranchId, string RoleGroupCode)[] groups)
    {
        var staffId = await Host.CreateStaffAsync(code, canSwitchBranch: false, groups.Length == 0 ? [(HomeBranchId, RoleGroupCatalog.Teller)] : groups);
        await using var context = Host.NewContext();
        var till = await LedgerAccountAsync(context, "1002-" + code, LookupCodes.LedgerAccountTypes.Asset, HomeBranchId);
        var staff = await context.Set<StaffUser>().SingleAsync(row => row.StaffUserId == staffId);
        staff.IsTeller = true;
        staff.TellerGLAccountId = till;
        await context.SaveChangesAsync();
        return (staffId, till);
    }

    public async Task LimitAsync(string roleGroupCode, string limitCode, decimal amount)
    {
        await using var context = Host.NewContext();
        var groupId = await context.Set<RoleGroup>().Where(row => row.RoleGroupCode == roleGroupCode).Select(row => row.RoleGroupId).SingleAsync();
        context.Add(new RoleGroupTransactionLimit { RoleGroupId = groupId, LimitCode = limitCode, MaxAmount = amount, CreatedOn = DateTime.UtcNow });
        await context.SaveChangesAsync();
    }

    /// <summary>Puts cash in the vault from suspense, through the engine (requested and approved by the setup staff).</summary>
    public Task FundVaultAsync(decimal amount) =>
        JournalAsync([], [new LedgerLine(VaultId, HomeBranchId, EntrySides.Debit, amount), new LedgerLine(SuspenseId, HomeBranchId, EntrySides.Credit, amount)], amount);

    /// <summary>An approved account with approved, verified signers, funded from suspense when asked.</summary>
    public async Task<(long AccountId, long[] Signers)> AccountAsync(decimal funding = 0m, int? branchId = null, int holders = 1, string mode = LookupCodes.OperatingModes.SingleMode)
    {
        var branch = branchId ?? HomeBranchId;
        await using var context = Host.NewContext();
        var systemId = await context.Set<StaffUser>().Where(row => row.StaffCode == StaffCodes.System).Select(row => row.StaffUserId).SingleAsync();
        var factory = new SampleFactory(
            await SampleLookups.LoadAsync(context, CancellationToken.None),
            new SampleNumberFormats(8, 13, UseCheckDigit: false),
            branch,
            systemId,
            DateTime.UtcNow);

        var partyStart = await SampleFactory.ReserveAsync(context, BankingModelConventions.PartyNumberSequence, holders, CancellationToken.None);
        var parties = Enumerable.Range(0, holders)
            .Select(index => factory.Individual(partyStart + index, new IndividualSpec(
                "Cash" + Interlocked.Increment(ref _sequence),
                "Holder",
                LookupCodes.Genders.Male,
                factory.Today.AddYears(-30),
                "0240000001",
                factory.Today.AddYears(5))))
            .ToList();
        context.Set<Party>().AddRange(parties);

        var number = await SampleFactory.ReserveAsync(context, BankingModelConventions.AccountNumberSequence, 1, CancellationToken.None);
        var account = factory.Account(number, Savings, "Cash account", mode, factory.Today);
        factory.AddHolder(account, parties[0], LookupCodes.AccountHolderRoles.Primary);
        foreach (var party in parties.Skip(1))
        {
            factory.AddHolder(account, party, LookupCodes.AccountHolderRoles.Joint);
        }

        context.Set<Account>().Add(account);
        await context.SaveChangesAsync();

        if (funding > 0m)
        {
            var lines = new List<LedgerLine> { new(SuspenseId, HomeBranchId, EntrySides.Debit, funding) };
            if (branch != HomeBranchId)
            {
                var interHome = await context.Set<SystemLedgerSetting>().Where(row => row.BranchId == HomeBranchId && row.SettingCode == LedgerSettingCodes.InterBranch).Select(row => row.LedgerAccountId).SingleAsync();
                var interOther = await context.Set<SystemLedgerSetting>().Where(row => row.BranchId == branch && row.SettingCode == LedgerSettingCodes.InterBranch).Select(row => row.LedgerAccountId).SingleAsync();
                lines.Add(new LedgerLine(interHome, HomeBranchId, EntrySides.Credit, funding));
                lines.Add(new LedgerLine(interOther, branch, EntrySides.Debit, funding));
            }

            await JournalAsync([new AccountLine(account.AccountId, EntrySides.Credit, funding)], lines, funding);
        }

        return (account.AccountId, parties.Select(party => party.PartyId).ToArray());
    }

    public async Task<decimal> LedgerBalanceAsync(long ledgerAccountId, int? branchId = null)
    {
        await using var context = Host.NewContext();
        var branch = branchId ?? HomeBranchId;
        return await context.Set<LedgerEntry>()
            .Where(row => row.LedgerAccountId == ledgerAccountId && row.BranchId == branch)
            .SumAsync(row => row.CrDb == EntrySides.Debit ? row.Amount : -row.Amount);
    }

    public async Task<AccountBalance> BalanceAsync(long accountId)
    {
        await using var context = Host.NewContext();
        return await context.Set<AccountBalance>().AsNoTracking().SingleAsync(row => row.AccountId == accountId);
    }

    /// <summary>Approves a request through the engine as the setup supervisor.</summary>
    public async Task<string> ApproveAsync(long postingRequestId)
    {
        using var scope = Host.Factory.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<PostingEngine>();
        var approved = await engine.ApproveAsync(new PostingActor(SetupSupervisorId, HomeBranchId), postingRequestId, CancellationToken.None);
        return approved.Succeeded ? approved.Value!.Status : approved.ProblemCode!;
    }

    public static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body, string? key = null)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        message.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString("N"));
        return await client.SendAsync(message);
    }

    private async Task JournalAsync(IReadOnlyList<AccountLine> accountLines, IReadOnlyList<LedgerLine> ledgerLines, decimal amount)
    {
        long requestId;
        using (var scope = Host.Factory.Services.CreateScope())
        {
            var engine = scope.ServiceProvider.GetRequiredService<PostingEngine>();
            var submitted = await engine.SubmitAsync(
                new PostingActor(SetupStaffId, HomeBranchId),
                new PostingDraft(TransactionCodes.Journal, LimitCode: null, amount, "Test funds", accountLines, ledgerLines, CheckSignerKyc: false),
                CancellationToken.None);
            submitted.Succeeded.ShouldBeTrue(submitted.ProblemCode);
            requestId = submitted.Value!.PostingRequestId;
        }

        (await ApproveAsync(requestId)).ShouldBe(PostingRequestStatuses.Posted);
    }

    private static async Task<long> LedgerAccountAsync(BankingDbContext context, string code, string typeCode, int branchId)
    {
        var typeId = await context.Set<LedgerAccountType>().Where(row => row.Code == typeCode).Select(row => row.Id).SingleAsync();
        var account = new LedgerAccount
        {
            BranchId = branchId,
            LedgerAccountCode = code,
            AccountName = "Test " + code,
            LedgerAccountTypeId = typeId,
            IsSupervisionRequired = false,
            CreatedOn = DateTime.UtcNow
        };
        context.Add(account);
        await context.SaveChangesAsync();
        return account.LedgerAccountId;
    }
}
