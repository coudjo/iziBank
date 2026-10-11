using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Banking.Infrastructure.Persistence.Conventions;

/// <summary>
/// Every string column must have an explicit max length or an explicit <c>(max)</c> store type.
/// Stops accidental <c>varchar(max)</c> columns when a DBML length was forgotten.
/// </summary>
internal sealed class RequiredStringLengthConvention : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        var missing = modelBuilder.Metadata
            .GetEntityTypes()
            .SelectMany(static entityType => entityType.GetProperties()
                .Where(static property => property.ClrType == typeof(string))
                .Where(static property => property.GetMaxLength() is null)
                .Where(static property => !IsExplicitMax(property.GetColumnType()))
                .Select(property => $"{entityType.ClrType.Name}.{property.Name}"))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        if (missing.Length == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            "String properties must have a max length or an explicit (max) column type: "
            + string.Join(", ", missing)
            + ".");
    }

    private static bool IsExplicitMax(string? columnType)
    {
        return columnType is not null
            && columnType.Contains("(max)", StringComparison.OrdinalIgnoreCase);
    }
}
