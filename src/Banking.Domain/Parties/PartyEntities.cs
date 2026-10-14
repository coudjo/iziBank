using Banking.Domain.Auditing;
using Banking.Domain.Institution;
using Banking.Domain.Lookups;
using Banking.Domain.Security;

namespace Banking.Domain.Parties;

public sealed class Party : AuditableEntity, ISupervisable
{
    public long PartyId { get; set; }

    public string PartyNumber { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public int PartyTypeId { get; set; }

    public string? Address1 { get; set; }

    public string? Address2 { get; set; }

    public string? Phone { get; set; }

    public string? Mobile { get; set; }

    public string? EmailAddress { get; set; }

    public int? KYCStatusId { get; set; }

    public string? SourceOfFunds { get; set; }

    public string? OccupationOrSector { get; set; }

    public bool IsPoliticallyExposed { get; set; }

    public long? CreditOfficerId { get; set; }

    public bool IsSupervisionRequired { get; set; } = true;

    public long? SupervisedByUserId { get; set; }

    public DateTime? SupervisedOn { get; set; }

    public PartyType? PartyType { get; set; }

    public KYCStatusType? KYCStatus { get; set; }

    public CreditOfficer? CreditOfficer { get; set; }

    public StaffUser? SupervisedByUser { get; set; }

    public IndividualDetail? IndividualDetail { get; set; }

    public BusinessDetail? BusinessDetail { get; set; }

    public GroupDetail? GroupDetail { get; set; }

    public PartyImage? Photo { get; set; }

    public ICollection<PartySignature> Signatures { get; } = new List<PartySignature>();

    public ICollection<GroupMember> GroupMemberships { get; } = new List<GroupMember>();

    public ICollection<PartyIdentificationDocument> IdentificationDocuments { get; } = new List<PartyIdentificationDocument>();

    public ICollection<PartyNextOfKin> NextOfKin { get; } = new List<PartyNextOfKin>();

    public ICollection<PartyRegulatoryAttribute> RegulatoryAttributes { get; } = new List<PartyRegulatoryAttribute>();

    public ICollection<PartyBeneficialOwner> BeneficialOwners { get; } = new List<PartyBeneficialOwner>();
}

public sealed class IndividualDetail : AuditableEntity
{
    public long PartyId { get; set; }

    public string? Title { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public int? GenderId { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public string? Nationality { get; set; }

    public int? IdentificationTypeId { get; set; }

    public string? IdentificationNumber { get; set; }

    public Party? Party { get; set; }

    public GenderType? Gender { get; set; }

    public IdentificationDocumentType? IdentificationType { get; set; }
}

public sealed class BusinessDetail : AuditableEntity
{
    public long PartyId { get; set; }

    public string BusinessName { get; set; } = string.Empty;

    public string? RegistrationNumber { get; set; }

    public string? TaxIdentificationNumber { get; set; }

    public DateOnly? IncorporationDate { get; set; }

    public int? BusinessCategoryId { get; set; }

    public Party? Party { get; set; }

    public BusinessCategoryType? BusinessCategory { get; set; }

    public ICollection<PartyBeneficialOwner> BeneficialOwners { get; } = new List<PartyBeneficialOwner>();
}

public sealed class GroupDetail : AuditableEntity
{
    public long PartyId { get; set; }

    public string GroupName { get; set; } = string.Empty;

    public DateOnly? FormationDate { get; set; }

    public string? MeetingFrequency { get; set; }

    public long? ChairpersonPartyId { get; set; }

    public Party? Party { get; set; }

    public Party? Chairperson { get; set; }
}

public sealed class GroupMember : AuditableEntity
{
    public long GroupMemberId { get; set; }

    public long GroupPartyId { get; set; }

    public long MemberPartyId { get; set; }

    public int? GroupMemberRoleId { get; set; }

    public DateOnly? JoinedOn { get; set; }

    public DateOnly? LeftOn { get; set; }

    public Party? GroupParty { get; set; }

    public Party? MemberParty { get; set; }

    public GroupMemberRoleType? Role { get; set; }
}

public sealed class PartyBeneficialOwner : AuditableEntity
{
    public long PartyBeneficialOwnerId { get; set; }

    public long BusinessPartyId { get; set; }

    public long OwnerPartyId { get; set; }

    public decimal OwnershipPercentage { get; set; }

    public BusinessDetail? Business { get; set; }

    public Party? Owner { get; set; }
}

public sealed class PartyIdentificationDocument : AuditableEntity
{
    public long PartyIdentificationDocumentId { get; set; }

    public long PartyId { get; set; }

    public int DocumentTypeId { get; set; }

    public string DocumentNumber { get; set; } = string.Empty;

    public byte[]? ImageValue { get; set; }

    public DateOnly? IssuedOn { get; set; }

    public DateOnly? ExpiresOn { get; set; }

    public Party? Party { get; set; }

    public IdentificationDocumentType? DocumentType { get; set; }
}

public sealed class PartyImage : AuditableEntity, ISupervisable
{
    public long PartyId { get; set; }

    public byte[]? ImageValue { get; set; }

    public bool IsSupervisionRequired { get; set; } = true;

    public long? SupervisedByUserId { get; set; }

    public DateTime? SupervisedOn { get; set; }

    public Party? Party { get; set; }

    public StaffUser? SupervisedByUser { get; set; }
}

/// <summary>
/// Specimen signature. The effective specimen is the most recent supervised row; a recapture
/// does not replace it until supervised (addendum section 3).
/// </summary>
public sealed class PartySignature : AuditableEntity, ISupervisable
{
    public long PartySignatureId { get; set; }

    public long PartyId { get; set; }

    public byte[] ImageValue { get; set; } = [];

    public DateTime CapturedOn { get; set; }

    public long CapturedByUserId { get; set; }

    public bool IsSupervisionRequired { get; set; } = true;

    public long? SupervisedByUserId { get; set; }

    public DateTime? SupervisedOn { get; set; }

    public Party? Party { get; set; }

    public StaffUser? CapturedByUser { get; set; }

    public StaffUser? SupervisedByUser { get; set; }
}

public sealed class PartyNextOfKin : AuditableEntity
{
    public long PartyNextOfKinId { get; set; }

    public long PartyId { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string? Relationship { get; set; }

    public string? MobileNumber { get; set; }

    public Party? Party { get; set; }
}

public sealed class PartyRegulatoryAttribute : AuditableEntity
{
    public long PartyRegulatoryAttributeId { get; set; }

    public long PartyId { get; set; }

    public string AttributeCode { get; set; } = string.Empty;

    public string? AttributeValue { get; set; }

    public DateTime RecordedOn { get; set; }

    public Party? Party { get; set; }
}

public sealed class CreditOfficer : AuditableEntity
{
    public long CreditOfficerId { get; set; }

    public int BranchId { get; set; }

    public long StaffUserId { get; set; }

    public bool IsDisabled { get; set; }

    public Branch? Branch { get; set; }

    public StaffUser? StaffUser { get; set; }
}
