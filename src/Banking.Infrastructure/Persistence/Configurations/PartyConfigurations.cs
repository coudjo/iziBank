using Banking.Domain.Parties;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Banking.Infrastructure.Persistence.Configurations;

internal sealed class PartyConfiguration : IEntityTypeConfiguration<Party>
{
    public void Configure(EntityTypeBuilder<Party> builder)
    {
        builder.ToTable(nameof(Party));
        builder.HasKey(entity => entity.PartyId);
        builder.Property(entity => entity.PartyNumber).IsVarchar(20);
        builder.HasIndex(entity => entity.PartyNumber).IsUnique();
        builder.Property(entity => entity.DisplayName).IsNvarchar(200);
        builder.HasIndex(entity => entity.DisplayName);
        builder.Property(entity => entity.Address1).IsOptionalNvarchar(200);
        builder.Property(entity => entity.Address2).IsOptionalNvarchar(200);
        builder.Property(entity => entity.Phone).IsOptionalVarchar(20);
        builder.Property(entity => entity.Mobile).IsOptionalVarchar(20);
        builder.HasIndex(entity => entity.Mobile);
        builder.Property(entity => entity.EmailAddress).IsOptionalVarchar(100);
        builder.Property(entity => entity.SourceOfFunds).IsOptionalVarchar(200);
        builder.Property(entity => entity.OccupationOrSector).IsOptionalVarchar(100);
        builder.Property(entity => entity.IsPoliticallyExposed).HasDefaultValue(false);
        builder.Property(entity => entity.IsSupervisionRequired).HasDefaultValue(true);
        builder.HasIndex(entity => entity.CreditOfficerId);
        builder.HasIndex(entity => new { entity.PartyTypeId, entity.KYCStatusId })
            .HasDatabaseName("IX_Party_PartyTypeId_KYCStatusId");

        builder.HasOne(entity => entity.PartyType)
            .WithMany()
            .HasForeignKey(entity => entity.PartyTypeId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.KYCStatus)
            .WithMany()
            .HasForeignKey(entity => entity.KYCStatusId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.CreditOfficer)
            .WithMany()
            .HasForeignKey(entity => entity.CreditOfficerId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.SupervisedByUser)
            .WithMany()
            .HasForeignKey(entity => entity.SupervisedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class IndividualDetailConfiguration : IEntityTypeConfiguration<IndividualDetail>
{
    public void Configure(EntityTypeBuilder<IndividualDetail> builder)
    {
        builder.ToTable(nameof(IndividualDetail));
        builder.HasKey(entity => entity.PartyId);
        builder.Property(entity => entity.Title).IsOptionalNvarchar(10);
        builder.Property(entity => entity.FirstName).IsNvarchar(60);
        builder.Property(entity => entity.LastName).IsNvarchar(60);
        builder.Property(entity => entity.Nationality).IsOptionalVarchar(50);
        builder.Property(entity => entity.IdentificationNumber).IsOptionalVarchar(30);
        builder.HasIndex(entity => new { entity.IdentificationTypeId, entity.IdentificationNumber })
            .IsUnique()
            .HasFilter("[IdentificationNumber] IS NOT NULL")
            .HasDatabaseName("UX_IndividualDetail_Identification");

        builder.HasOne(entity => entity.Party)
            .WithOne(party => party.IndividualDetail)
            .HasForeignKey<IndividualDetail>(entity => entity.PartyId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.Gender)
            .WithMany()
            .HasForeignKey(entity => entity.GenderId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.IdentificationType)
            .WithMany()
            .HasForeignKey(entity => entity.IdentificationTypeId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class BusinessDetailConfiguration : IEntityTypeConfiguration<BusinessDetail>
{
    public void Configure(EntityTypeBuilder<BusinessDetail> builder)
    {
        builder.ToTable(nameof(BusinessDetail));
        builder.HasKey(entity => entity.PartyId);
        builder.Property(entity => entity.BusinessName).IsNvarchar(150);
        builder.Property(entity => entity.RegistrationNumber).IsOptionalVarchar(40);
        builder.HasIndex(entity => entity.RegistrationNumber)
            .IsUnique()
            .HasFilter("[RegistrationNumber] IS NOT NULL")
            .HasDatabaseName("UX_BusinessDetail_RegistrationNumber");
        builder.Property(entity => entity.TaxIdentificationNumber).IsOptionalVarchar(30);

        builder.HasOne(entity => entity.Party)
            .WithOne(party => party.BusinessDetail)
            .HasForeignKey<BusinessDetail>(entity => entity.PartyId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.BusinessCategory)
            .WithMany()
            .HasForeignKey(entity => entity.BusinessCategoryId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class GroupDetailConfiguration : IEntityTypeConfiguration<GroupDetail>
{
    public void Configure(EntityTypeBuilder<GroupDetail> builder)
    {
        builder.ToTable(nameof(GroupDetail));
        builder.HasKey(entity => entity.PartyId);
        builder.Property(entity => entity.GroupName).IsNvarchar(100);
        builder.Property(entity => entity.MeetingFrequency).IsOptionalVarchar(30);

        builder.HasOne(entity => entity.Party)
            .WithOne(party => party.GroupDetail)
            .HasForeignKey<GroupDetail>(entity => entity.PartyId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.Chairperson)
            .WithMany()
            .HasForeignKey(entity => entity.ChairpersonPartyId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class GroupMemberConfiguration : IEntityTypeConfiguration<GroupMember>
{
    public void Configure(EntityTypeBuilder<GroupMember> builder)
    {
        builder.ToTable(nameof(GroupMember));
        builder.HasKey(entity => entity.GroupMemberId);
        builder.HasIndex(entity => entity.GroupPartyId);
        builder.HasIndex(entity => entity.MemberPartyId)
            .IsUnique()
            .HasFilter("[LeftOn] IS NULL")
            .HasDatabaseName("UX_GroupMember_ActiveMember");
        builder.HasIndex(entity => new { entity.GroupPartyId, entity.MemberPartyId })
            .IsUnique()
            .HasFilter("[LeftOn] IS NULL")
            .HasDatabaseName("UX_GroupMember_ActiveGroupMember");

        builder.HasOne(entity => entity.GroupParty)
            .WithMany()
            .HasForeignKey(entity => entity.GroupPartyId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.MemberParty)
            .WithMany(party => party.GroupMemberships)
            .HasForeignKey(entity => entity.MemberPartyId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.Role)
            .WithMany()
            .HasForeignKey(entity => entity.GroupMemberRoleId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class PartyBeneficialOwnerConfiguration : IEntityTypeConfiguration<PartyBeneficialOwner>
{
    public void Configure(EntityTypeBuilder<PartyBeneficialOwner> builder)
    {
        builder.ToTable(nameof(PartyBeneficialOwner), table =>
            table.HasCheckConstraint(
                "CK_PartyBeneficialOwner_OwnershipPercentage",
                "[OwnershipPercentage] > 0 AND [OwnershipPercentage] <= 100"));

        builder.HasKey(entity => entity.PartyBeneficialOwnerId);
        builder.Property(entity => entity.OwnershipPercentage).HasPrecision(5, 2);
        builder.HasIndex(entity => new { entity.BusinessPartyId, entity.OwnerPartyId })
            .IsUnique()
            .HasDatabaseName("UX_PartyBeneficialOwner_Business_Owner");

        builder.HasOne(entity => entity.Business)
            .WithMany(business => business.BeneficialOwners)
            .HasForeignKey(entity => entity.BusinessPartyId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.Owner)
            .WithMany(party => party.BeneficialOwners)
            .HasForeignKey(entity => entity.OwnerPartyId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class PartyIdentificationDocumentConfiguration : IEntityTypeConfiguration<PartyIdentificationDocument>
{
    public void Configure(EntityTypeBuilder<PartyIdentificationDocument> builder)
    {
        builder.ToTable(nameof(PartyIdentificationDocument), table =>
            table.HasCheckConstraint(
                "CK_PartyIdentificationDocument_Dates",
                "[ExpiresOn] IS NULL OR [IssuedOn] IS NULL OR [ExpiresOn] >= [IssuedOn]"));

        builder.HasKey(entity => entity.PartyIdentificationDocumentId);
        builder.Property(entity => entity.DocumentNumber).IsVarchar(40);
        builder.Property(entity => entity.ImageValue).IsVarBinaryMax();
        builder.HasIndex(entity => entity.PartyId);

        builder.HasOne(entity => entity.Party)
            .WithMany(party => party.IdentificationDocuments)
            .HasForeignKey(entity => entity.PartyId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.DocumentType)
            .WithMany()
            .HasForeignKey(entity => entity.DocumentTypeId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class PartyImageConfiguration : IEntityTypeConfiguration<PartyImage>
{
    public void Configure(EntityTypeBuilder<PartyImage> builder)
    {
        builder.ToTable(nameof(PartyImage));
        builder.HasKey(entity => entity.PartyId);
        builder.Property(entity => entity.ImageValue).IsVarBinaryMax();
        builder.Property(entity => entity.IsSupervisionRequired).HasDefaultValue(true);

        builder.HasOne(entity => entity.Party)
            .WithOne(party => party.Photo)
            .HasForeignKey<PartyImage>(entity => entity.PartyId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.SupervisedByUser)
            .WithMany()
            .HasForeignKey(entity => entity.SupervisedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class PartySignatureConfiguration : IEntityTypeConfiguration<PartySignature>
{
    public void Configure(EntityTypeBuilder<PartySignature> builder)
    {
        builder.ToTable(nameof(PartySignature));
        builder.HasKey(entity => entity.PartySignatureId);
        builder.Property(entity => entity.ImageValue).HasColumnType("varbinary(max)").IsRequired();
        builder.Property(entity => entity.IsSupervisionRequired).HasDefaultValue(true);
        builder.HasIndex(entity => new { entity.PartyId, entity.SupervisedOn })
            .HasDatabaseName("IX_PartySignature_PartyId_SupervisedOn");

        builder.HasOne(entity => entity.Party)
            .WithMany(party => party.Signatures)
            .HasForeignKey(entity => entity.PartyId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.CapturedByUser)
            .WithMany()
            .HasForeignKey(entity => entity.CapturedByUserId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.SupervisedByUser)
            .WithMany()
            .HasForeignKey(entity => entity.SupervisedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class PartyNextOfKinConfiguration : IEntityTypeConfiguration<PartyNextOfKin>
{
    public void Configure(EntityTypeBuilder<PartyNextOfKin> builder)
    {
        builder.ToTable(nameof(PartyNextOfKin));
        builder.HasKey(entity => entity.PartyNextOfKinId);
        builder.Property(entity => entity.FullName).IsNvarchar(100);
        builder.Property(entity => entity.Relationship).IsOptionalVarchar(40);
        builder.Property(entity => entity.MobileNumber).IsOptionalVarchar(20);

        builder.HasOne(entity => entity.Party)
            .WithMany(party => party.NextOfKin)
            .HasForeignKey(entity => entity.PartyId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class PartyRegulatoryAttributeConfiguration : IEntityTypeConfiguration<PartyRegulatoryAttribute>
{
    public void Configure(EntityTypeBuilder<PartyRegulatoryAttribute> builder)
    {
        builder.ToTable(nameof(PartyRegulatoryAttribute));
        builder.HasKey(entity => entity.PartyRegulatoryAttributeId);
        builder.Property(entity => entity.AttributeCode).IsVarchar(50);
        builder.Property(entity => entity.AttributeValue).IsOptionalVarchar(500);
        builder.HasIndex(entity => new { entity.PartyId, entity.AttributeCode, entity.RecordedOn })
            .IsDescending(false, false, true)
            .HasDatabaseName("IX_PartyRegulatoryAttribute_PartyId_AttributeCode_RecordedOn");

        builder.HasOne(entity => entity.Party)
            .WithMany(party => party.RegulatoryAttributes)
            .HasForeignKey(entity => entity.PartyId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class CreditOfficerConfiguration : IEntityTypeConfiguration<CreditOfficer>
{
    public void Configure(EntityTypeBuilder<CreditOfficer> builder)
    {
        builder.ToTable(nameof(CreditOfficer));
        builder.HasKey(entity => entity.CreditOfficerId);
        builder.Property(entity => entity.IsDisabled).HasDefaultValue(false);
        builder.HasIndex(entity => entity.StaffUserId)
            .IsUnique()
            .HasFilter("[IsDisabled] = 0")
            .HasDatabaseName("UX_CreditOfficer_ActiveStaffUser");

        builder.HasOne(entity => entity.Branch)
            .WithMany()
            .HasForeignKey(entity => entity.BranchId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(entity => entity.StaffUser)
            .WithMany()
            .HasForeignKey(entity => entity.StaffUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
