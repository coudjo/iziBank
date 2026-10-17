using System.Data;
using System.Text;
using Banking.Domain.Accounts;
using Banking.Domain.Institution;
using Banking.Domain.Ledger;
using Banking.Domain.Lookups;
using Banking.Domain.Parties;
using Banking.Domain.Products;
using Banking.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Banking.Api.IntegrationTests.Infrastructure;

/// <summary>Direct inserts for account tests: products, ready customers and approved accounts, skipping the screens.</summary>
public sealed class AccountSeed
{
    private readonly ApiTestHost _host;
    private int _counter;

    public AccountSeed(ApiTestHost host)
    {
        _host = host;
    }

    public async Task<int> ProductAsync(string code, string type = LookupCodes.ProductTypes.Savings, string prefix = "10")
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
            MinimumBalance = 10m,
            IsFreezed = false,
            IsSupervisionRequired = false
        };
        context.Add(product);
        await context.SaveChangesAsync();
        return product.ProductId;
    }

    /// <summary>An approved, KYC-verified individual with an approved specimen signature.</summary>
    public async Task<long> CustomerAsync(string name)
    {
        await using var context = _host.NewContext();
        var number = Interlocked.Increment(ref _counter);
        var party = new Party
        {
            PartyNumber = $"S{number:D7}",
            DisplayName = name,
            PartyTypeId = await context.Set<PartyType>().Where(row => row.Code == LookupCodes.PartyTypes.Individual).Select(row => row.Id).SingleAsync(),
            KYCStatusId = await context.Set<KYCStatusType>().Where(row => row.Code == LookupCodes.KycStatuses.Verified).Select(row => row.Id).SingleAsync(),
            IsSupervisionRequired = false
        };
        context.Add(party);
        await context.SaveChangesAsync();

        var names = name.Split(' ', 2);
        context.Add(new IndividualDetail { PartyId = party.PartyId, FirstName = names[0], LastName = names.Length > 1 ? names[1] : names[0] });
        context.Add(new PartySignature
        {
            PartyId = party.PartyId,
            ImageValue = [0x89, 0x50, 0x4E, 0x47],
            CapturedOn = DateTime.UtcNow,
            CapturedByUserId = await context.Set<StaffUser>().Where(row => row.StaffCode == ApiTestHost.AdminCode).Select(row => row.StaffUserId).SingleAsync(),
            IsSupervisionRequired = false
        });
        await context.SaveChangesAsync();
        return party.PartyId;
    }

    /// <summary>An account held by <paramref name="partyId"/> as primary holder, approved unless <paramref name="approved"/> is false.</summary>
    public async Task<long> AccountAsync(int productId, long partyId, decimal clearBalance = 0m, bool approved = true, string? number = null)
    {
        await using var context = _host.NewContext();
        var sequence = Interlocked.Increment(ref _counter);
        var account = new Account
        {
            AccountNumber = number ?? $"77{sequence:D11}",
            BranchId = _host.HomeBranchId,
            ProductId = productId,
            AccountName = "Account " + sequence,
            DateOpened = new DateOnly(2030, 1, 7),
            OperatingModeId = await context.Set<OperatingModeType>().Where(row => row.Code == LookupCodes.OperatingModes.SingleMode).Select(row => row.Id).SingleAsync(),
            IsSupervisionRequired = !approved,
            Balance = new AccountBalance { ClearBalance = clearBalance },
            Condition = new AccountCondition()
        };
        AccountBalanceCalculator.Recompute(account.Balance);
        account.Holders.Add(new AccountHolder
        {
            PartyId = partyId,
            AccountHolderRoleId = await context.Set<AccountHolderRoleType>().Where(row => row.Code == LookupCodes.AccountHolderRoles.Primary).Select(row => row.Id).SingleAsync()
        });
        context.Add(account);
        await context.SaveChangesAsync();
        return account.AccountId;
    }

    public async Task SetBalanceAsync(long accountId, decimal clearBalance)
    {
        await using var context = _host.NewContext();
        var balance = await context.Set<AccountBalance>().SingleAsync(row => row.AccountId == accountId);
        balance.ClearBalance = clearBalance;
        AccountBalanceCalculator.Recompute(balance);
        await context.SaveChangesAsync();
    }
}

/// <summary>Actual execution plans of EF queries, for index-use checks.</summary>
public static class QueryPlans
{
    /// <summary>
    /// Runs EF's own parameterised command with STATISTICS XML on and returns the actual plan. A DECLARE-based
    /// script would compile with unknown values and guess scans, which is not what the API does.
    /// </summary>
    public static async Task<string> ActualPlanAsync<T>(IQueryable<T> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var command = query.CreateDbCommand();
        var connection = command.Connection!;
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using (var on = connection.CreateCommand())
        {
            on.CommandText = "SET STATISTICS XML ON;";
            await on.ExecuteNonQueryAsync();
        }

        var plan = new StringBuilder();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            do
            {
                var isPlan = reader.FieldCount == 1 && reader.GetName(0).Contains("Showplan", StringComparison.OrdinalIgnoreCase);
                while (await reader.ReadAsync())
                {
                    if (isPlan)
                    {
                        plan.Append(reader.GetString(0));
                    }
                }
            }
            while (await reader.NextResultAsync());
        }

        await using (var off = connection.CreateCommand())
        {
            off.CommandText = "SET STATISTICS XML OFF;";
            await off.ExecuteNonQueryAsync();
        }

        return plan.ToString();
    }
}
