using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Banking.Infrastructure.Persistence;

public sealed class BankingDbContextFactory : IDesignTimeDbContextFactory<BankingDbContext>
{
    public BankingDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Banking")
            ?? "Server=localhost,14334;Database=iziBank;User Id=sa;Password=unused;TrustServerCertificate=True;Encrypt=True";

        var options = new DbContextOptionsBuilder<BankingDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new BankingDbContext(options);
    }
}
