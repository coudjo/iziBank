using Banking.Domain.Auditing;
using Banking.Domain.Lookups;
using Banking.Domain.Security;

namespace Banking.Domain.Institution;

public sealed class InstitutionProfile : AuditableEntity
{
    public int InstitutionProfileId { get; set; }

    public string InstitutionName { get; set; } = string.Empty;

    public string? LicenseNumber { get; set; }

    public int? RegulatoryCategoryId { get; set; }

    public DateOnly? IncorporationDate { get; set; }

    public string? HeadOfficeAddress { get; set; }

    public RegulatoryCategoryType? RegulatoryCategory { get; set; }
}

public sealed class Branch : AuditableEntity
{
    public int BranchId { get; set; }

    public string BranchCode { get; set; } = string.Empty;

    public string BranchName { get; set; } = string.Empty;

    public string? OperatingLicenseNo { get; set; }

    public short? ClearingDays { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<StaffUser> StaffUsers { get; } = new List<StaffUser>();
}

public sealed class Currency : AuditableEntity
{
    public int CurrencyId { get; set; }

    public string CurrencyCode { get; set; } = string.Empty;

    public string CurrencyName { get; set; } = string.Empty;

    public decimal? ExchangeRate { get; set; }

    public bool IsLocalCurrency { get; set; }
}

public sealed class BusinessDayStatus : AuditableEntity
{
    public long BusinessDayStatusId { get; set; }

    public int BranchId { get; set; }

    public DateOnly WorkingDate { get; set; }

    public DateTime? BusinessDayStart { get; set; }

    public DateTime? BusinessDayEnd { get; set; }

    public int? CurrentStatusId { get; set; }

    public Branch? Branch { get; set; }

    public BusinessDayStatusType? CurrentStatus { get; set; }
}

public sealed class DefinedHoliday : AuditableEntity
{
    public int HolidayId { get; set; }

    public DateOnly HolidayDate { get; set; }

    public string? Remarks { get; set; }
}
