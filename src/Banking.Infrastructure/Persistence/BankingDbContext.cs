using Banking.Domain.Auditing;
using Microsoft.EntityFrameworkCore;

namespace Banking.Infrastructure.Persistence;

public class BankingDbContext : DbContext
{
    public BankingDbContext(DbContextOptions<BankingDbContext> options)
        : base(options)
    {
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureLedgerEntriesAreAppendOnly();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        EnsureLedgerEntriesAreAppendOnly();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        BankingModelConventions.Configure(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BankingDbContext).Assembly);
        BankingModelConventions.Apply(modelBuilder);
    }

    private void EnsureLedgerEntriesAreAppendOnly()
    {
        var mutated = ChangeTracker.Entries()
            .Where(static entry => entry.Entity is ILedgerRecord)
            .Where(static entry => entry.State is EntityState.Modified or EntityState.Deleted)
            .Select(static entry => entry.Metadata.ClrType.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (mutated.Length == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            "Ledger tables are append-only and cannot be updated or deleted: "
            + string.Join(", ", mutated)
            + ".");
    }
}
