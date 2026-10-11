using Banking.Domain.Institution;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Banking.Infrastructure.Persistence.Configurations;

internal sealed class InstitutionProfileConfiguration : IEntityTypeConfiguration<InstitutionProfile>
{
    public void Configure(EntityTypeBuilder<InstitutionProfile> builder)
    {
        builder.ToTable(nameof(InstitutionProfile), table =>
            table.HasCheckConstraint("CK_InstitutionProfile_Singleton", "[InstitutionProfileId] = 1"));

        builder.HasKey(entity => entity.InstitutionProfileId);
        builder.Property(entity => entity.InstitutionName).IsVarchar(150);
        builder.Property(entity => entity.LicenseNumber).IsOptionalVarchar(30);
        builder.HasIndex(entity => entity.LicenseNumber).IsUnique();
        builder.Property(entity => entity.HeadOfficeAddress).IsOptionalNvarchar(300);

        builder.HasOne(entity => entity.RegulatoryCategory)
            .WithMany()
            .HasForeignKey(entity => entity.RegulatoryCategoryId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable(nameof(Branch));
        builder.HasKey(entity => entity.BranchId);
        builder.Property(entity => entity.BranchCode).IsVarchar(10);
        builder.HasIndex(entity => entity.BranchCode).IsUnique();
        builder.Property(entity => entity.BranchName).IsVarchar(100);
        builder.Property(entity => entity.OperatingLicenseNo).IsOptionalVarchar(30);
        builder.Property(entity => entity.IsActive).HasDefaultValue(true);
    }
}

internal sealed class CurrencyConfiguration : IEntityTypeConfiguration<Currency>
{
    public void Configure(EntityTypeBuilder<Currency> builder)
    {
        builder.ToTable(nameof(Currency));
        builder.HasKey(entity => entity.CurrencyId);
        builder.Property(entity => entity.CurrencyCode).IsVarchar(5);
        builder.HasIndex(entity => entity.CurrencyCode).IsUnique();
        builder.Property(entity => entity.CurrencyName).IsVarchar(50);
        builder.Property(entity => entity.ExchangeRate).HasPrecision(18, 6);
        builder.Property(entity => entity.IsLocalCurrency).HasDefaultValue(false);
        builder.HasIndex(entity => entity.IsLocalCurrency)
            .IsUnique()
            .HasFilter("[IsLocalCurrency] = 1")
            .HasDatabaseName("UX_Currency_IsLocalCurrency");
    }
}

internal sealed class BusinessDayStatusConfiguration : IEntityTypeConfiguration<BusinessDayStatus>
{
    public void Configure(EntityTypeBuilder<BusinessDayStatus> builder)
    {
        builder.ToTable(nameof(BusinessDayStatus));
        builder.HasKey(entity => entity.BusinessDayStatusId);
        builder.HasIndex(entity => new { entity.BranchId, entity.WorkingDate })
            .IsUnique()
            .HasDatabaseName("UX_BusinessDayStatus_BranchId_WorkingDate");

        builder.HasOne(entity => entity.Branch)
            .WithMany()
            .HasForeignKey(entity => entity.BranchId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.CurrentStatus)
            .WithMany()
            .HasForeignKey(entity => entity.CurrentStatusId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class DefinedHolidayConfiguration : IEntityTypeConfiguration<DefinedHoliday>
{
    public void Configure(EntityTypeBuilder<DefinedHoliday> builder)
    {
        builder.ToTable(nameof(DefinedHoliday));
        builder.HasKey(entity => entity.HolidayId);
        builder.HasIndex(entity => entity.HolidayDate).IsUnique();
        builder.Property(entity => entity.Remarks).IsOptionalVarchar(100);
    }
}
