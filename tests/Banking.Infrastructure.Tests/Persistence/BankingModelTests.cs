using Banking.Domain.Accounts;
using Banking.Domain.Institution;
using Banking.Domain.Ledger;
using Banking.Domain.Lookups;
using Banking.Domain.Parties;
using Banking.Domain.Products;
using Banking.Domain.Security;
using Banking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Banking.Infrastructure.Tests.Persistence;

public sealed class BankingModelTests
{
    private static readonly string[] ExpectedTables =
    [
        nameof(InstitutionProfile), nameof(Branch), nameof(Currency), nameof(BusinessDayStatus), nameof(DefinedHoliday),
        nameof(Module), nameof(SubModule), nameof(StaffUser), nameof(CustomerDigitalAccess),
        nameof(RoleGroup), nameof(RoleGroupPermission), nameof(StaffUserRoleGroup), nameof(IdempotencyKey),
        nameof(UserSession), nameof(EventType), nameof(UserActivityLog), nameof(DataChangeAuditLog),
        nameof(Party), nameof(IndividualDetail), nameof(BusinessDetail), nameof(GroupDetail), nameof(GroupMember),
        nameof(PartyBeneficialOwner), nameof(PartyIdentificationDocument), nameof(PartyImage), nameof(PartySignature), nameof(PartyNextOfKin),
        nameof(PartyRegulatoryAttribute), nameof(CreditOfficer),
        nameof(Product), nameof(ProductInterestRate),
        nameof(Account), nameof(AccountHolder), nameof(AccountBalance), nameof(AccountBalanceDailySnapshot),
        nameof(AccountClassification), nameof(AccountSubClassification), nameof(AccountClassificationAssignment),
        nameof(AccountCondition), nameof(AccountReminder), nameof(DormantAccountHistory),
        nameof(FreezeReleaseAccountBalance),
        nameof(LedgerAccount),
        nameof(RegulatoryCategoryType), nameof(BusinessDayStatusType), nameof(LogoutReasonType), nameof(PartyType),
        nameof(KYCStatusType), nameof(GenderType), nameof(IdentificationDocumentType), nameof(BusinessCategoryType),
        nameof(GroupMemberRoleType), nameof(ProductType), nameof(RateType), nameof(AccountHolderRoleType),
        nameof(OperatingInstructionType), nameof(OperatingModeType), nameof(CloseReasonType),
        nameof(FreezeReleaseActionType), nameof(LedgerAccountType)
    ];

    [Fact]
    public void Model_contains_exactly_the_phase_a_tables()
    {
        using var context = CreateContext();

        var tables = context.Model.GetEntityTypes()
            .Select(static entity => entity.GetTableName())
            .Where(static name => name is not null)
            .Cast<string>()
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        tables.ShouldBe(ExpectedTables.OrderBy(static name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void Amendments_are_present_on_the_model()
    {
        using var context = CreateContext();

        var party = Entity<Party>(context);
        party.FindProperty(nameof(Party.PartyNumber)).ShouldNotBeNull();
        party.FindProperty(nameof(Party.DisplayName))!.IsUnicode().ShouldBe(true);
        party.GetIndexes().ShouldContain(static index =>
            index.IsUnique && index.Properties[0].Name == nameof(Party.PartyNumber));

        Entity<UserActivityLog>(context).FindProperty(nameof(UserActivityLog.SessionId))!.IsNullable.ShouldBe(true);

        var ledger = Entity<LedgerAccount>(context);
        ledger.FindProperty(nameof(LedgerAccount.LedgerAccountTypeId)).ShouldNotBeNull();
        ledger.FindProperty("LegderAccountTypeId").ShouldBeNull();
        ledger.FindProperty(nameof(LedgerAccount.IsSupervisionRequired)).ShouldNotBeNull();
        ledger.FindProperty("ApprovalRequired").ShouldBeNull();
        ledger.FindProperty(nameof(LedgerAccount.SupervisedByUserId)).ShouldNotBeNull();
        ledger.FindProperty(nameof(LedgerAccount.SupervisedOn)).ShouldNotBeNull();

        var staff = Entity<StaffUser>(context);
        staff.FindProperty(nameof(StaffUser.FullName))!.GetMaxLength().ShouldBe(100);
        staff.FindProperty(nameof(StaffUser.FullName))!.IsUnicode().ShouldBe(true);
        staff.FindProperty(nameof(StaffUser.SecretKeyChangedOn)).ShouldNotBeNull();
        staff.FindProperty(nameof(StaffUser.RowVersion))!.IsConcurrencyToken.ShouldBe(true);

        Entity<IndividualDetail>(context).FindProperty(nameof(IndividualDetail.FirstName))!.IsUnicode().ShouldBe(true);
        Entity<BusinessDetail>(context).FindProperty(nameof(BusinessDetail.BusinessName))!.IsUnicode().ShouldBe(true);
        Entity<GroupDetail>(context).FindProperty(nameof(GroupDetail.GroupName))!.IsUnicode().ShouldBe(true);
        Entity<Party>(context).FindProperty(nameof(Party.Address1))!.IsUnicode().ShouldBe(true);
    }

    [Fact]
    public void Role_groups_replace_per_user_permissions()
    {
        using var context = CreateContext();

        context.Model.GetEntityTypes().Select(static entity => entity.GetTableName())
            .ShouldNotContain("UserPermission");

        var permission = Entity<RoleGroupPermission>(context);
        permission.GetIndexes().ShouldContain(static index =>
            index.IsUnique && index.Properties.Select(static p => p.Name)
                .SequenceEqual(new[] { nameof(RoleGroupPermission.RoleGroupId), nameof(RoleGroupPermission.ModuleId), nameof(RoleGroupPermission.SubModuleId) }));
        permission.FindProperty(nameof(RoleGroupPermission.SubModuleId))!.IsNullable.ShouldBeTrue();
        CheckNames(permission).ShouldContain("CK_RoleGroupPermission_ViewImplied");

        // The composite FK makes the database refuse a sub-module under the wrong module.
        permission.GetForeignKeys().ShouldContain(static key =>
            key.Properties.Count == 2 && key.PrincipalEntityType.ClrType == typeof(SubModule));

        Entity<StaffUserRoleGroup>(context).GetIndexes().ShouldContain(static index =>
            index.IsUnique && index.Properties.Count == 3);
        Entity<RoleGroup>(context).FindProperty(nameof(RoleGroup.IsSupervisionRequired)).ShouldNotBeNull();
    }

    [Fact]
    public void Signature_and_idempotency_tables_are_shaped_as_specified()
    {
        using var context = CreateContext();

        var signature = Entity<PartySignature>(context);
        signature.FindProperty(nameof(PartySignature.ImageValue))!.IsNullable.ShouldBeFalse();
        signature.GetIndexes().ShouldContain(static index =>
            index.Properties.Select(static p => p.Name)
                .SequenceEqual(new[] { nameof(PartySignature.PartyId), nameof(PartySignature.SupervisedOn) }));

        var key = Entity<IdempotencyKey>(context);
        key.GetIndexes().ShouldContain(static index =>
            index.IsUnique && index.Properties.Select(static p => p.Name)
                .SequenceEqual(new[] { nameof(IdempotencyKey.StaffUserId), nameof(IdempotencyKey.KeyValue) }));
        typeof(Banking.Domain.Auditing.IAuditable).IsAssignableFrom(typeof(IdempotencyKey)).ShouldBeFalse();
    }

    [Fact]
    public void Sequences_and_required_constraints_are_mapped()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;

        model.FindSequence(BankingModelConventions.PartyNumberSequence, "dbo").ShouldNotBeNull();
        model.FindSequence(BankingModelConventions.AccountNumberSequence, "dbo").ShouldNotBeNull();

        CheckNames(model.FindEntityType(typeof(InstitutionProfile))!).ShouldContain("CK_InstitutionProfile_Singleton");
        CheckNames(model.FindEntityType(typeof(UserSession))!).ShouldContain("CK_UserSession_ExactlyOnePrincipal");
        CheckNames(model.FindEntityType(typeof(Account))!).ShouldContain("CK_Account_CloseFields");
        CheckNames(model.FindEntityType(typeof(PartyBeneficialOwner))!).ShouldContain("CK_PartyBeneficialOwner_OwnershipPercentage");

        model.FindEntityType(typeof(UserActivityLog))!.IsTableExcludedFromMigrations().ShouldBeTrue();
        model.FindEntityType(typeof(DataChangeAuditLog))!.IsTableExcludedFromMigrations().ShouldBeTrue();
        model.FindEntityType(typeof(Party))!.IsTableExcludedFromMigrations().ShouldBeFalse();
    }

    [Fact]
    public void SaveChanges_rejects_updates_and_deletes_on_ledger_entities()
    {
        using var context = CreateContext();

        var activity = new UserActivityLog
        {
            EventId = 1,
            EventTypeId = 1,
            Outcome = "Success",
            EventTimestamp = DateTime.UtcNow
        };

        context.Attach(activity);
        context.Entry(activity).State = EntityState.Modified;
        Should.Throw<InvalidOperationException>(() => context.SaveChanges())
            .Message.ShouldContain(nameof(UserActivityLog));

        context.Entry(activity).State = EntityState.Detached;

        var audit = new DataChangeAuditLog
        {
            AuditId = 1,
            TableName = "Party",
            ActionType = "UPDATE",
            LoggedOn = DateTime.UtcNow
        };

        context.Attach(audit);
        context.Entry(audit).State = EntityState.Deleted;
        Should.Throw<InvalidOperationException>(() => context.SaveChanges())
            .Message.ShouldContain(nameof(DataChangeAuditLog));
    }

    private static IEnumerable<string> CheckNames(IEntityType entity)
    {
        return entity.GetDeclaredCheckConstraints()
            .Select(static constraint => constraint.Name)
            .Where(static name => name is not null)
            .Cast<string>();
    }

    private static IEntityType Entity<T>(BankingDbContext context)
    {
        var entity = context.Model.FindEntityType(typeof(T));
        entity.ShouldNotBeNull();
        return entity;
    }

    private static BankingDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<BankingDbContext>()
            .UseSqlServer("Server=.;Database=unused;TrustServerCertificate=True")
            .Options;

        return new BankingDbContext(options);
    }
}
