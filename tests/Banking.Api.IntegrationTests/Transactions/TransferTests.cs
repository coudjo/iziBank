using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Application.Errors;
using Banking.Domain.Accounts;
using Banking.Domain.Institution;
using Banking.Domain.Ledger;
using Banking.Domain.Lookups;
using Banking.Domain.Parties;
using Banking.Domain.Products;
using Banking.Domain.Security;
using Banking.Domain.Supervision;
using Banking.Domain.Transactions;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Transactions;
using Banking.Migrator.Sample;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Banking.Api.IntegrationTests.Transactions;

/// <summary>B1 and B3: the posting engine through transfers: limits, approval, holds, refusals, branches, concurrency.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class TransferTests : IAsyncLifetime
{
    private const decimal TellerLimit = 1_000m;

    private readonly MsSqlContainerFixture _fixture;
    private ApiTestHost _host = null!;
    private int _otherBranchId;
    private long _tellerId;
    private long _supervisorId;
    private long _supervisor2Id;
    private HttpClient _teller = null!;
    private HttpClient _supervisor = null!;
    private HttpClient _supervisor2 = null!;
    private SampleProduct _savings = null!;
    private long _suspenseId;
    private int _sequence;

    public TransferTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _host = await ApiTestHost.CreateAsync(
            _fixture,
            "transfers",
            new Dictionary<string, string?> { ["Supervision:AllowSelfApproval"] = "false" });
        _otherBranchId = await _host.CreateBranchAsync("002");
        _tellerId = await _host.CreateStaffAsync("TELL1", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.Teller));
        _supervisorId = await _host.CreateStaffAsync("SUP1", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.Supervisor));
        _supervisor2Id = await _host.CreateStaffAsync("SUP2", canSwitchBranch: false, (_host.HomeBranchId, RoleGroupCatalog.Supervisor));
        _teller = _host.Client(await _host.TokenAsync("TELL1"));
        _supervisor = _host.Client(await _host.TokenAsync("SUP1"));
        _supervisor2 = _host.Client(await _host.TokenAsync("SUP2"));

        await using var context = _host.NewContext();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var open = await context.Set<BusinessDayStatusType>().Where(row => row.Code == LookupCodes.BusinessDayStatuses.Open).Select(row => row.Id).SingleAsync();
        foreach (var branchId in new[] { _host.HomeBranchId, _otherBranchId })
        {
            context.Add(new BusinessDayStatus { BranchId = branchId, WorkingDate = today, BusinessDayStart = DateTime.UtcNow, CurrentStatusId = open, CreatedOn = DateTime.UtcNow });
        }

        var tellerGroup = await context.Set<RoleGroup>().Where(row => row.RoleGroupCode == RoleGroupCatalog.Teller).Select(row => row.RoleGroupId).SingleAsync();
        context.Add(new RoleGroupTransactionLimit { RoleGroupId = tellerGroup, LimitCode = TransactionLimitCodes.Transfer, MaxAmount = TellerLimit, CreatedOn = DateTime.UtcNow });
        await context.SaveChangesAsync();

        var control = await LedgerAccountAsync(context, "2001-01", LookupCodes.LedgerAccountTypes.Liability, _host.HomeBranchId);
        _suspenseId = await LedgerAccountAsync(context, "1901-01", LookupCodes.LedgerAccountTypes.Asset, _host.HomeBranchId);
        var interHome = await LedgerAccountAsync(context, "1801-01", LookupCodes.LedgerAccountTypes.Asset, _host.HomeBranchId);
        var interOther = await LedgerAccountAsync(context, "1801-02", LookupCodes.LedgerAccountTypes.Asset, _otherBranchId);
        context.Add(new SystemLedgerSetting { BranchId = _host.HomeBranchId, SettingCode = LedgerSettingCodes.InterBranch, LedgerAccountId = interHome, CreatedOn = DateTime.UtcNow });
        context.Add(new SystemLedgerSetting { BranchId = _otherBranchId, SettingCode = LedgerSettingCodes.InterBranch, LedgerAccountId = interOther, CreatedOn = DateTime.UtcNow });

        var typeId = await context.Set<ProductType>().Where(row => row.Code == LookupCodes.ProductTypes.Savings).Select(row => row.Id).SingleAsync();
        var currencyId = await context.Set<Currency>().Where(row => row.IsLocalCurrency).Select(row => row.CurrencyId).SingleAsync();
        var product = new Product
        {
            BranchId = _host.HomeBranchId,
            ProductCode = "SAVTX",
            ProductName = "Transfer savings",
            ProductTypeId = typeId,
            AccountPrefix = "15",
            CurrencyId = currencyId,
            LedgerControlAccountId = control,
            MinimumBalance = 10m,
            IsSupervisionRequired = false,
            CreatedOn = DateTime.UtcNow
        };
        context.Add(product);
        await context.SaveChangesAsync();
        _savings = new SampleProduct(product.ProductId, product.ProductCode, product.AccountPrefix);
    }

    public async Task DisposeAsync()
    {
        _teller.Dispose();
        _supervisor.Dispose();
        _supervisor2.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task A_transfer_within_the_limit_posts_at_once_and_a_retry_does_not_post_twice()
    {
        var (from, ama) = await AccountAsync(5_000m);
        var (to, _) = await AccountAsync(0m);
        var key = Guid.NewGuid().ToString("N");

        using var first = await TransferAsync(_teller, from, to, 300m, [ama], key);
        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        var outcome = await ApiTestHost.ReadJsonAsync(first);
        outcome.GetProperty("status").GetString().ShouldBe(PostingRequestStatuses.Posted);
        var reference = outcome.GetProperty("reference").GetString()!;
        reference.ShouldStartWith("001-");

        using var retry = await TransferAsync(_teller, from, to, 300m, [ama], key);
        retry.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await ApiTestHost.ReadJsonAsync(retry)).GetProperty("reference").GetString().ShouldBe(reference);

        (await BalanceAsync(from)).ClearBalance.ShouldBe(4_700m);
        (await BalanceAsync(to)).ClearBalance.ShouldBe(300m);

        await using var context = _host.NewContext();
        var posted = await context.Set<PostedTransaction>()
            .Include(row => row.AccountEntries)
            .Include(row => row.LedgerEntries)
            .SingleAsync(row => row.Reference == reference);
        posted.OperatorStaffUserId.ShouldBe(_tellerId);
        posted.SupervisorStaffUserId.ShouldBeNull();
        posted.AccountEntries.Single(row => row.AccountId == from).BalanceAfter.ShouldBe(4_700m);
        posted.AccountEntries.Single(row => row.AccountId == to).BalanceAfter.ShouldBe(300m);
        posted.LedgerEntries.Count.ShouldBe(2);
        PostingRules.UnbalancedBranches(posted.LedgerEntries.Select(row => new LedgerLine(row.LedgerAccountId, row.BranchId, row.CrDb, row.Amount)))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task Above_the_limit_the_amount_is_held_until_a_different_supervisor_approves()
    {
        var (from, ama) = await AccountAsync(5_000m);
        var (to, _) = await AccountAsync(0m);

        using var response = await TransferAsync(_teller, from, to, 2_000m, [ama]);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var requestId = (await ApiTestHost.ReadJsonAsync(response)).GetProperty("postingRequestId").GetInt64();

        var held = await BalanceAsync(from);
        held.ClearBalance.ShouldBe(5_000m);
        held.AvailableBalance.ShouldBe(3_000m);
        held.UnsupervisedDebit.ShouldBe(2_000m);
        (await BalanceAsync(to)).TotalBalance.ShouldBe(2_000m);

        using (var inbox = await _supervisor.GetAsync(new Uri("/api/v1/transaction-requests", UriKind.Relative)))
        {
            var rows = (await ApiTestHost.ReadJsonAsync(inbox)).EnumerateArray().ToList();
            rows.ShouldContain(row => row.GetProperty("postingRequestId").GetInt64() == requestId);
        }

        using (var byTeller = await _teller.PostAsync(new Uri($"/api/v1/transaction-requests/{requestId}/approve", UriKind.Relative), null))
        {
            byTeller.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await ApiTestHost.ProblemTypeAsync(byTeller)).ShouldBe(SupervisionProblems.PermissionDenied);
        }

        using (var approve = await _supervisor.PostAsync(new Uri($"/api/v1/transaction-requests/{requestId}/approve", UriKind.Relative), null))
        {
            approve.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await ApiTestHost.ReadJsonAsync(approve)).GetProperty("status").GetString().ShouldBe(PostingRequestStatuses.Posted);
        }

        var after = await BalanceAsync(from);
        after.ClearBalance.ShouldBe(3_000m);
        after.UnsupervisedDebit.ShouldBe(0m);
        after.AvailableBalance.ShouldBe(3_000m);
        (await BalanceAsync(to)).ClearBalance.ShouldBe(2_000m);

        await using var context = _host.NewContext();
        var request = await context.Set<PostingRequest>().SingleAsync(row => row.PostingRequestId == requestId);
        var posted = await context.Set<PostedTransaction>().SingleAsync(row => row.PostedTransactionId == request.PostedTransactionId);
        posted.OperatorStaffUserId.ShouldBe(_tellerId);
        posted.SupervisorStaffUserId.ShouldBe(_supervisorId);
    }

    [Fact]
    public async Task A_supervisor_cannot_approve_their_own_request()
    {
        var (from, ama) = await AccountAsync(5_000m);
        var (to, _) = await AccountAsync(0m);

        // Supervisors have no transfer limit in this test, so everything they post waits.
        using var response = await TransferAsync(_supervisor, from, to, 50m, [ama]);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var requestId = (await ApiTestHost.ReadJsonAsync(response)).GetProperty("postingRequestId").GetInt64();

        using (var own = await _supervisor.PostAsync(new Uri($"/api/v1/transaction-requests/{requestId}/approve", UriKind.Relative), null))
        {
            own.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await ApiTestHost.ProblemTypeAsync(own)).ShouldBe(SupervisionProblems.SelfApproval);
        }

        using var other = await _supervisor2.PostAsync(new Uri($"/api/v1/transaction-requests/{requestId}/approve", UriKind.Relative), null);
        other.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await BalanceAsync(to)).ClearBalance.ShouldBe(50m);
        _supervisor2Id.ShouldNotBe(_supervisorId);
    }

    [Fact]
    public async Task Rejecting_or_cancelling_releases_the_hold()
    {
        var (from, ama) = await AccountAsync(5_000m);
        var (to, _) = await AccountAsync(0m);

        var rejected = await PendingAsync(from, to, 1_500m, ama);
        using (var noReason = await _supervisor.PostAsJsonAsync($"/api/v1/transaction-requests/{rejected}/reject", new { reason = " " }))
        {
            noReason.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using (var reject = await _supervisor.PostAsJsonAsync($"/api/v1/transaction-requests/{rejected}/reject", new { reason = "Customer did not sign" }))
        {
            reject.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var cancelled = await PendingAsync(from, to, 1_200m, ama);
        using (var notYours = await _supervisor.PostAsync(new Uri($"/api/v1/transaction-requests/{cancelled}/cancel", UriKind.Relative), null))
        {
            notYours.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await ApiTestHost.ProblemTypeAsync(notYours)).ShouldBe(ProblemCodes.PostingNotRequester);
        }

        using (var cancel = await _teller.PostAsync(new Uri($"/api/v1/transaction-requests/{cancelled}/cancel", UriKind.Relative), null))
        {
            cancel.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var again = await _supervisor.PostAsync(new Uri($"/api/v1/transaction-requests/{cancelled}/approve", UriKind.Relative), null))
        {
            again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await ApiTestHost.ProblemTypeAsync(again)).ShouldBe(SupervisionProblems.NotPending);
        }

        var balance = await BalanceAsync(from);
        balance.ClearBalance.ShouldBe(5_000m);
        balance.AvailableBalance.ShouldBe(5_000m);
        balance.UnsupervisedDebit.ShouldBe(0m);
        (await BalanceAsync(to)).UnsupervisedCredit.ShouldBe(0m);
    }

    [Fact]
    public async Task Refusals_name_the_reason()
    {
        var (from, ama) = await AccountAsync(500m);
        var (to, kofi) = await AccountAsync(0m);

        using (var noKey = await _teller.PostAsJsonAsync("/api/v1/transactions/transfers", Body(from, to, 10m, [ama])))
        {
            noKey.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await ApiTestHost.ProblemTypeAsync(noKey)).ShouldBe(ProblemCodes.IdempotencyKeyRequired);
        }

        await ExpectAsync(await TransferAsync(_teller, from, to, 495m, [ama]), HttpStatusCode.Conflict, ProblemCodes.AccountInsufficientFunds);
        await ExpectAsync(await TransferAsync(_teller, from, to, 10m, [kofi]), HttpStatusCode.BadRequest, ProblemCodes.Validation);
        await ExpectAsync(await TransferAsync(_teller, from, from, 10m, [ama]), HttpStatusCode.BadRequest, ProblemCodes.Validation);
        await ExpectAsync(await TransferAsync(_teller, from, to, 10.001m, [ama]), HttpStatusCode.BadRequest, ProblemCodes.Validation);

        await using (var context = _host.NewContext())
        {
            var condition = await context.Set<AccountCondition>().SingleAsync(row => row.AccountId == to);
            condition.IsFreezed = true;
            await context.SaveChangesAsync();
        }

        await ExpectAsync(await TransferAsync(_teller, from, to, 10m, [ama]), HttpStatusCode.Conflict, ProblemCodes.AccountFrozen);

        var (elsewhere, esi) = await AccountAsync(100m, _otherBranchId);
        await ExpectAsync(await TransferAsync(_teller, elsewhere, from, 10m, [esi]), HttpStatusCode.Conflict, ProblemCodes.RecordOtherBranch);
        (await BalanceAsync(from)).ClearBalance.ShouldBe(500m);
    }

    [Fact]
    public async Task All_to_sign_needs_every_signer()
    {
        var (joint, ama, kofi) = await JointAccountAsync(1_000m);
        var (to, _) = await AccountAsync(0m);

        await ExpectAsync(await TransferAsync(_teller, joint, to, 10m, [ama]), HttpStatusCode.Conflict, ProblemCodes.TransactionMandateNotConfirmed);
        using var both = await TransferAsync(_teller, joint, to, 10m, [ama, kofi]);
        both.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_transfer_to_another_branch_balances_each_branch_through_inter_branch_accounts()
    {
        var (from, ama) = await AccountAsync(800m);
        var (to, _) = await AccountAsync(0m, _otherBranchId);

        using var response = await TransferAsync(_teller, from, to, 100m, [ama]);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var reference = (await ApiTestHost.ReadJsonAsync(response)).GetProperty("reference").GetString();

        await using var context = _host.NewContext();
        var posted = await context.Set<PostedTransaction>()
            .Include(row => row.LedgerEntries)
            .Include(row => row.TransactionCode)
            .SingleAsync(row => row.Reference == reference);
        posted.TransactionCode!.Code.ShouldBe(TransactionCodes.TransferAcrossBranches);
        posted.LedgerEntries.Count.ShouldBe(4);
        posted.LedgerEntries.Select(row => row.BranchId).Distinct().Count().ShouldBe(2);
        PostingRules.UnbalancedBranches(posted.LedgerEntries.Select(row => new LedgerLine(row.LedgerAccountId, row.BranchId, row.CrDb, row.Amount)))
            .ShouldBeEmpty();
        (await BalanceAsync(to)).ClearBalance.ShouldBe(100m);
    }

    [Fact]
    public async Task Parallel_transfers_both_ways_never_lose_an_update()
    {
        var (left, leftSigner) = await AccountAsync(1_000m);
        var (right, rightSigner) = await AccountAsync(1_000m);

        var tasks = Enumerable.Range(0, 20).Select(index => Task.Run(async () =>
        {
            using var scope = _host.Factory.Services.CreateScope();
            var engine = scope.ServiceProvider.GetRequiredService<PostingEngine>();
            var (from, to) = index % 2 == 0 ? (left, right) : (right, left);
            var result = await engine.SubmitAsync(
                new PostingActor(_tellerId, _host.HomeBranchId),
                new PostingDraft(
                    TransactionCodes.Transfer,
                    TransactionLimitCodes.Transfer,
                    10m,
                    "Parallel",
                    [new AccountLine(from, EntrySides.Debit, 10m), new AccountLine(to, EntrySides.Credit, 10m)],
                    [],
                    CheckSignerKyc: true),
                CancellationToken.None);
            result.Succeeded.ShouldBeTrue(result.ProblemCode);
        }));
        await Task.WhenAll(tasks);

        foreach (var id in new[] { left, right })
        {
            var balance = await BalanceAsync(id);
            balance.ClearBalance.ShouldBe(1_000m);
            await using var context = _host.NewContext();
            var last = await context.Set<AccountLedgerEntry>().Where(row => row.AccountId == id).OrderByDescending(row => row.AccountLedgerEntryId).FirstAsync();
            last.BalanceAfter.ShouldBe(balance.ClearBalance);
        }

        leftSigner.ShouldNotBe(rightSigner);
    }

    // ---------------------------------------------------------------- Helpers

    private static object Body(long from, long to, decimal amount, long[] signers) =>
        new { fromAccountId = from, toAccountId = to, amount, narration = "Test", confirmedSignerPartyIds = signers };

    private static async Task<HttpResponseMessage> TransferAsync(HttpClient client, long from, long to, decimal amount, long[] signers, string? key = null)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/transactions/transfers")
        {
            Content = JsonContent.Create(Body(from, to, amount, signers))
        };
        message.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString("N"));
        return await client.SendAsync(message);
    }

    private async Task<long> PendingAsync(long from, long to, decimal amount, long signer)
    {
        using var response = await TransferAsync(_teller, from, to, amount, [signer]);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        return (await ApiTestHost.ReadJsonAsync(response)).GetProperty("postingRequestId").GetInt64();
    }

    private static async Task ExpectAsync(HttpResponseMessage response, HttpStatusCode status, string problem)
    {
        using (response)
        {
            response.StatusCode.ShouldBe(status);
            (await ApiTestHost.ProblemTypeAsync(response)).ShouldBe(problem);
        }
    }

    private async Task<AccountBalance> BalanceAsync(long accountId)
    {
        await using var context = _host.NewContext();
        return await context.Set<AccountBalance>().AsNoTracking().SingleAsync(row => row.AccountId == accountId);
    }

    /// <summary>An approved single-holder account with an approved, verified signer; funded through the engine.</summary>
    private async Task<(long AccountId, long SignerPartyId)> AccountAsync(decimal funding, int? branchId = null)
    {
        var (account, parties) = await CreateAccountAsync(branchId ?? _host.HomeBranchId, 1, LookupCodes.OperatingModes.SingleMode);
        await FundAsync(account, funding, branchId ?? _host.HomeBranchId);
        return (account, parties[0]);
    }

    private async Task<(long AccountId, long First, long Second)> JointAccountAsync(decimal funding)
    {
        var (account, parties) = await CreateAccountAsync(_host.HomeBranchId, 2, LookupCodes.OperatingModes.AllToSign);
        await FundAsync(account, funding, _host.HomeBranchId);
        return (account, parties[0], parties[1]);
    }

    private async Task<(long AccountId, long[] Parties)> CreateAccountAsync(int branchId, int holders, string mode)
    {
        await using var context = _host.NewContext();
        var systemId = await context.Set<StaffUser>().Where(row => row.StaffCode == StaffCodes.System).Select(row => row.StaffUserId).SingleAsync();
        var factory = new SampleFactory(
            await SampleLookups.LoadAsync(context, CancellationToken.None),
            new SampleNumberFormats(8, 13, UseCheckDigit: false),
            branchId,
            systemId,
            DateTime.UtcNow);

        var partyStart = await SampleFactory.ReserveAsync(context, BankingModelConventions.PartyNumberSequence, holders, CancellationToken.None);
        var parties = Enumerable.Range(0, holders)
            .Select(index => factory.Individual(partyStart + index, new IndividualSpec(
                "Test" + Interlocked.Increment(ref _sequence),
                "Holder",
                LookupCodes.Genders.Female,
                factory.Today.AddYears(-30),
                "0240000000",
                factory.Today.AddYears(5))))
            .ToList();
        context.Set<Party>().AddRange(parties);

        var accountNumber = await SampleFactory.ReserveAsync(context, BankingModelConventions.AccountNumberSequence, 1, CancellationToken.None);
        var account = factory.Account(accountNumber, _savings, "Test account", mode, factory.Today);
        factory.AddHolder(account, parties[0], LookupCodes.AccountHolderRoles.Primary);
        foreach (var party in parties.Skip(1))
        {
            factory.AddHolder(account, party, LookupCodes.AccountHolderRoles.Joint);
        }

        context.Set<Account>().Add(account);
        await context.SaveChangesAsync();
        return (account.AccountId, parties.Select(party => party.PartyId).ToArray());
    }

    /// <summary>Opening money from suspense, requested by the teller and approved by a supervisor (exercises approval).</summary>
    private async Task FundAsync(long accountId, decimal amount, int branchId)
    {
        if (amount == 0m)
        {
            return;
        }

        var suspense = _suspenseId;
        var lines = new List<LedgerLine> { new(suspense, _host.HomeBranchId, EntrySides.Debit, amount) };
        if (branchId != _host.HomeBranchId)
        {
            // Different branch: fund straight from that branch's inter-branch account to keep each branch balanced.
            await using var context = _host.NewContext();
            var interHome = await context.Set<SystemLedgerSetting>().Where(row => row.BranchId == _host.HomeBranchId && row.SettingCode == LedgerSettingCodes.InterBranch).Select(row => row.LedgerAccountId).SingleAsync();
            var interOther = await context.Set<SystemLedgerSetting>().Where(row => row.BranchId == branchId && row.SettingCode == LedgerSettingCodes.InterBranch).Select(row => row.LedgerAccountId).SingleAsync();
            lines.Add(new LedgerLine(interHome, _host.HomeBranchId, EntrySides.Credit, amount));
            lines.Add(new LedgerLine(interOther, branchId, EntrySides.Debit, amount));
        }

        long requestId;
        using (var scope = _host.Factory.Services.CreateScope())
        {
            var engine = scope.ServiceProvider.GetRequiredService<PostingEngine>();
            var submitted = await engine.SubmitAsync(
                new PostingActor(_tellerId, _host.HomeBranchId),
                new PostingDraft(TransactionCodes.Journal, LimitCode: null, amount, "Opening funds", [new AccountLine(accountId, EntrySides.Credit, amount)], lines, CheckSignerKyc: false),
                CancellationToken.None);
            submitted.Succeeded.ShouldBeTrue(submitted.ProblemCode);
            submitted.Value!.Status.ShouldBe(PostingRequestStatuses.Pending);
            requestId = submitted.Value.PostingRequestId;
        }

        using (var scope = _host.Factory.Services.CreateScope())
        {
            var engine = scope.ServiceProvider.GetRequiredService<PostingEngine>();
            var approved = await engine.ApproveAsync(new PostingActor(_supervisorId, _host.HomeBranchId), requestId, CancellationToken.None);
            approved.Succeeded.ShouldBeTrue(approved.ProblemCode);
        }
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
