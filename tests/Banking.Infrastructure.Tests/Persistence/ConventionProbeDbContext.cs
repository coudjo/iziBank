using Banking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Banking.Infrastructure.Tests.Persistence;

internal sealed class ConventionProbeDbContext : DbContext
{
    public ConventionProbeDbContext(DbContextOptions<ConventionProbeDbContext> options)
        : base(options)
    {
    }

    public DbSet<ConventionProbe> ConventionProbes => Set<ConventionProbe>();

    public DbSet<ProbeType> ProbeTypes => Set<ProbeType>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        BankingModelConventions.Configure(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ConventionProbeConfiguration());
        BankingModelConventions.Apply(modelBuilder);
    }
}

internal sealed class MissingStringLengthDbContext : DbContext
{
    public MissingStringLengthDbContext(DbContextOptions<MissingStringLengthDbContext> options)
        : base(options)
    {
    }

    public DbSet<MissingStringLengthProbe> MissingStringLengthProbes => Set<MissingStringLengthProbe>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        BankingModelConventions.Configure(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        BankingModelConventions.Apply(modelBuilder);
    }
}
