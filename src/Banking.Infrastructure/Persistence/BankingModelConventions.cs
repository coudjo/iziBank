using Banking.Domain.Auditing;
using Banking.Domain.Lookups;
using Banking.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;

namespace Banking.Infrastructure.Persistence;

public static class BankingModelConventions
{
    public const int DecimalPrecision = 19;
    public const int DecimalScale = 4;
    public const int LookupCodeLength = 30;
    public const int LookupNameLength = 100;
    public const string DefaultSchema = "dbo";
    public const string CreatedOnDefaultSql = "SYSUTCDATETIME()";

    public static void Configure(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>().HavePrecision(DecimalPrecision, DecimalScale);
        configurationBuilder.Properties<DateTime>().HaveColumnType("datetime2");
        configurationBuilder.Properties<DateTime?>().HaveColumnType("datetime2");
        configurationBuilder.Properties<DateOnly>().HaveColumnType("date");
        configurationBuilder.Properties<DateOnly?>().HaveColumnType("date");
        configurationBuilder.Properties<string>().AreUnicode(false);
        configurationBuilder.Conventions.Add(_ => new RequiredStringLengthConvention());
    }

    public static void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(DefaultSchema);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToArray())
        {
            if (entityType.IsOwned())
            {
                continue;
            }

            modelBuilder.Entity(entityType.ClrType).ToTable(entityType.ClrType.Name);
            ApplyAuditable(modelBuilder, entityType.ClrType);
            ApplyLookup(modelBuilder, entityType.ClrType);
        }
    }

    private static void ApplyAuditable(ModelBuilder modelBuilder, Type clrType)
    {
        if (!typeof(IAuditable).IsAssignableFrom(clrType))
        {
            return;
        }

        modelBuilder.Entity(clrType, builder =>
        {
            builder.Property(nameof(IAuditable.CreatedByUserId));
            builder.Property(nameof(IAuditable.CreatedOn))
                .IsRequired()
                .HasDefaultValueSql(CreatedOnDefaultSql);
            builder.Property(nameof(IAuditable.ModifiedByUserId));
            builder.Property(nameof(IAuditable.ModifiedOn));
        });
    }

    private static void ApplyLookup(ModelBuilder modelBuilder, Type clrType)
    {
        if (!typeof(Lookup).IsAssignableFrom(clrType) || clrType.IsAbstract)
        {
            return;
        }

        modelBuilder.Entity(clrType, builder =>
        {
            builder.HasKey(nameof(Lookup.Id));
            builder.Property(nameof(Lookup.Id)).ValueGeneratedOnAdd();
            builder.Property(nameof(Lookup.Code))
                .IsRequired()
                .HasMaxLength(LookupCodeLength)
                .IsUnicode(false);
            builder.HasAlternateKey(nameof(Lookup.Code));
            builder.Property(nameof(Lookup.Name))
                .IsRequired()
                .HasMaxLength(LookupNameLength)
                .IsUnicode(false);
            builder.Property(nameof(Lookup.IsActive))
                .IsRequired()
                .HasDefaultValue(true);
        });
    }
}
