using Banking.Application.Accounts;
using Banking.Domain.Accounts;
using Banking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Banking.Infrastructure.Accounts;

/// <summary>A5-03: the next account number for a product prefix (plan 5.9). Gaps are acceptable.</summary>
public sealed class AccountNumbers
{
    private const string NextAccountNumberSql = "SELECT NEXT VALUE FOR [dbo].[AccountNumberSequence] AS [Value]";

    private readonly BankingDbContext _db;
    private readonly AccountNumberOptions _options;

    public AccountNumbers(BankingDbContext db, IOptions<AccountNumberOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _db = db;
        _options = options.Value;
    }

    public async Task<string> NextAsync(string prefix, CancellationToken cancellationToken)
    {
        // Not composed (no Single/First): SQL Server does not allow NEXT VALUE FOR inside a derived table.
        var values = await _db.Database
            .SqlQueryRaw<long>(NextAccountNumberSql)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return AccountNumberFormat.Compose(prefix, values[0], _options.Length, _options.UseCheckDigit);
    }
}
