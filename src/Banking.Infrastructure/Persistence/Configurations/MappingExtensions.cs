using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Banking.Infrastructure.Persistence.Configurations;

internal static class MappingExtensions
{
    public static PropertyBuilder<string> IsVarchar(this PropertyBuilder<string> property, int length)
    {
        return property.IsUnicode(false).HasMaxLength(length).IsRequired();
    }

    public static PropertyBuilder<string?> IsOptionalVarchar(this PropertyBuilder<string?> property, int length)
    {
        return property.IsUnicode(false).HasMaxLength(length);
    }

    public static PropertyBuilder<string> IsNvarchar(this PropertyBuilder<string> property, int length)
    {
        return property.IsUnicode(true).HasMaxLength(length).IsRequired();
    }

    public static PropertyBuilder<string?> IsOptionalNvarchar(this PropertyBuilder<string?> property, int length)
    {
        return property.IsUnicode(true).HasMaxLength(length);
    }

    public static PropertyBuilder<byte[]?> IsVarBinaryMax(this PropertyBuilder<byte[]?> property)
    {
        return property.HasColumnType("varbinary(max)");
    }

    public static PropertyBuilder<string?> IsNvarcharMax(this PropertyBuilder<string?> property)
    {
        return property.IsUnicode(true).HasColumnType("nvarchar(max)");
    }
}
