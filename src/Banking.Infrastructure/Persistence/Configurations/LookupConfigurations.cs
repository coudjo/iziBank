using Banking.Domain.Lookups;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Banking.Infrastructure.Persistence.Configurations;

internal abstract class LookupConfiguration<TLookup> : IEntityTypeConfiguration<TLookup>
    where TLookup : Lookup
{
    public void Configure(EntityTypeBuilder<TLookup> builder)
    {
        builder.ToTable(typeof(TLookup).Name);
    }
}

internal sealed class RegulatoryCategoryTypeConfiguration : LookupConfiguration<RegulatoryCategoryType>;

internal sealed class BusinessDayStatusTypeConfiguration : LookupConfiguration<BusinessDayStatusType>;

internal sealed class LogoutReasonTypeConfiguration : LookupConfiguration<LogoutReasonType>;

internal sealed class PartyTypeConfiguration : LookupConfiguration<PartyType>;

internal sealed class KYCStatusTypeConfiguration : LookupConfiguration<KYCStatusType>;

internal sealed class GenderTypeConfiguration : LookupConfiguration<GenderType>;

internal sealed class IdentificationDocumentTypeConfiguration : LookupConfiguration<IdentificationDocumentType>;

internal sealed class BusinessCategoryTypeConfiguration : LookupConfiguration<BusinessCategoryType>;

internal sealed class GroupMemberRoleTypeConfiguration : LookupConfiguration<GroupMemberRoleType>;

internal sealed class ProductTypeConfiguration : LookupConfiguration<ProductType>;

internal sealed class RateTypeConfiguration : LookupConfiguration<RateType>;

internal sealed class AccountHolderRoleTypeConfiguration : LookupConfiguration<AccountHolderRoleType>;

internal sealed class OperatingInstructionTypeConfiguration : LookupConfiguration<OperatingInstructionType>;

internal sealed class OperatingModeTypeConfiguration : LookupConfiguration<OperatingModeType>;

internal sealed class CloseReasonTypeConfiguration : LookupConfiguration<CloseReasonType>;

internal sealed class FreezeReleaseActionTypeConfiguration : LookupConfiguration<FreezeReleaseActionType>;

internal sealed class LedgerAccountTypeConfiguration : LookupConfiguration<LedgerAccountType>;
