using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Banking.Api.IntegrationTests.Schema;

[Collection(SqlServerCollection.Name)]
public sealed class SchemaBaselineTests
{
    private static readonly string[] ExpectedTables =
    [
        "InstitutionProfile", "Branch", "Currency", "BusinessDayStatus", "DefinedHoliday",
        "Module", "SubModule", "StaffUser", "CustomerDigitalAccess",
        "RoleGroup", "RoleGroupPermission", "StaffUserRoleGroup", "IdempotencyKey", "ChangeRequest",
        "UserSession", "EventType", "UserActivityLog", "DataChangeAuditLog",
        "Party", "IndividualDetail", "BusinessDetail", "GroupDetail", "GroupMember",
        "PartyBeneficialOwner", "PartyIdentificationDocument", "PartyImage", "PartySignature", "PartyNextOfKin",
        "PartyRegulatoryAttribute", "CreditOfficer",
        "Product", "ProductInterestRate",
        "Account", "AccountHolder", "AccountBalance", "AccountBalanceDailySnapshot",
        "AccountClassification", "AccountSubClassification", "AccountClassificationAssignment",
        "AccountCondition", "AccountReminder", "DormantAccountHistory",
        "FreezeReleaseAccountBalance",
        "LedgerAccount",
        "RegulatoryCategoryType", "BusinessDayStatusType", "LogoutReasonType", "PartyType",
        "KYCStatusType", "GenderType", "IdentificationDocumentType", "BusinessCategoryType",
        "GroupMemberRoleType", "ProductType", "RateType", "AccountHolderRoleType",
        "OperatingInstructionType", "OperatingModeType", "CloseReasonType",
        "FreezeReleaseActionType", "LedgerAccountType",
        "TransactionCode", "PostingRequest", "PostedTransaction", "AccountLedgerEntry", "LedgerEntry",
        "LedgerAccountDailyBalance", "SystemLedgerSetting", "ProductLedgerSetting", "RoleGroupTransactionLimit",
        "TillSession",
        "ChargeCategoryType", "ChargeConditionType", "ChargeBasisType", "ChargeShortfallPolicyType",
        "ChargeCode", "ProductCharge", "ProductChargeTier"
    ];

    private readonly MsSqlContainerFixture _fixture;

    public SchemaBaselineTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Baseline_migrations_apply_and_match_phase_a_schema()
    {
        var databaseName = $"iziBank_schema_{Guid.NewGuid():N}";
        var connectionString = await _fixture.CreateDatabaseAsync(databaseName);

        var options = new DbContextOptionsBuilder<BankingDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        await using (var context = new BankingDbContext(options))
        {
            await context.Database.MigrateAsync();
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var tables = await QueryStringsAsync(
            connection,
            """
            SELECT name
            FROM sys.tables
            WHERE schema_id = SCHEMA_ID('dbo')
              AND is_ms_shipped = 0
              AND name NOT IN ('__EFMigrationsHistory')
              AND name NOT LIKE 'MSSQL_Ledger%'
            UNION
            SELECT name
            FROM sys.views
            WHERE schema_id = SCHEMA_ID('dbo')
              AND name IN ('UserActivityLog', 'DataChangeAuditLog')
            ORDER BY name;
            """);

        tables.ShouldBe(ExpectedTables.OrderBy(static name => name, StringComparer.Ordinal));

        await AssertLedgerAsync(connection, "UserActivityLog");
        await AssertLedgerAsync(connection, "DataChangeAuditLog");
        await AssertLedgerAsync(connection, "PostedTransaction");
        await AssertLedgerAsync(connection, "AccountLedgerEntry");
        await AssertLedgerAsync(connection, "LedgerEntry");
        await AssertSequencesAsync(connection);
        await AssertSection24Async(connection);
    }

    private static async Task AssertLedgerAsync(SqlConnection connection, string name)
    {
        var rows = await QueryRowsAsync(
            connection,
            """
            SELECT t.name, t.ledger_type, t.ledger_type_desc
            FROM sys.tables t
            WHERE t.name = @name
               OR t.name LIKE '%' + @name + '%'
            """,
            ("name", name));

        var appendOnly = rows.Exists(static row =>
        {
            var type = Convert.ToString(row["ledger_type_desc"], System.Globalization.CultureInfo.InvariantCulture);
            return type is not null && type.Contains("APPEND_ONLY", StringComparison.OrdinalIgnoreCase);
        });

        appendOnly.ShouldBe(true, $"Expected an append-only ledger table for {name}. Rows: {rows.Count}");
    }

    private static async Task AssertSequencesAsync(SqlConnection connection)
    {
        var sequences = await QueryRowsAsync(
            connection,
            """
            SELECT name, CAST(cache_size AS int) AS cache_size
            FROM sys.sequences
            WHERE schema_id = SCHEMA_ID('dbo')
            """);

        sequences.Select(row => Convert.ToString(row["name"], System.Globalization.CultureInfo.InvariantCulture))
            .ShouldBe(["AccountNumberSequence", "PartyNumberSequence", "TransactionReferenceSequence"], ignoreOrder: true);

        foreach (var row in sequences)
        {
            Convert.ToInt32(row["cache_size"], System.Globalization.CultureInfo.InvariantCulture).ShouldBe(50);
        }
    }

    private static async Task AssertSection24Async(SqlConnection connection)
    {
        var checks = await QueryStringsAsync(
            connection,
            """
            SELECT name
            FROM sys.check_constraints
            WHERE parent_object_id IN (
                SELECT object_id FROM sys.tables WHERE schema_id = SCHEMA_ID('dbo'))
            """);

        checks.ShouldContain("CK_InstitutionProfile_Singleton");
        checks.ShouldContain("CK_UserSession_ExactlyOnePrincipal");
        checks.ShouldContain("CK_Account_CloseFields");
        checks.ShouldContain("CK_PartyBeneficialOwner_OwnershipPercentage");
        checks.ShouldContain("CK_PartyIdentificationDocument_Dates");
        checks.ShouldContain("CK_FreezeReleaseAccountBalance_Amount");
        checks.ShouldContain("CK_ProductInterestRate_EffectiveDates");
        checks.ShouldContain("CK_ProductInterestRate_AmountBand");
        checks.ShouldContain("CK_RoleGroupPermission_ViewImplied");
        checks.ShouldContain("CK_PostingRequest_Amount");
        checks.ShouldContain("CK_PostingRequest_Status");
        checks.ShouldContain("CK_AccountLedgerEntry_Amount");
        checks.ShouldContain("CK_AccountLedgerEntry_CrDb");
        checks.ShouldContain("CK_LedgerEntry_Amount");
        checks.ShouldContain("CK_LedgerEntry_CrDb");
        checks.ShouldContain("CK_RoleGroupTransactionLimit_MaxAmount");
        checks.ShouldContain("CK_TillSession_Status");
        checks.ShouldContain("CK_ProductCharge_EffectiveDates");
        checks.ShouldContain("CK_ProductCharge_FreeTransactions");
        checks.ShouldContain("CK_ProductChargeTier_Amount");
        checks.ShouldContain("CK_ProductChargeTier_MinMax");

        var indexes = await QueryStringsAsync(
            connection,
            """
            SELECT i.name
            FROM sys.indexes i
            INNER JOIN sys.tables t ON t.object_id = i.object_id
            WHERE t.schema_id = SCHEMA_ID('dbo')
              AND i.name IS NOT NULL
            """);

        indexes.ShouldContain("UX_Currency_IsLocalCurrency");
        indexes.ShouldContain("UX_IndividualDetail_Identification");
        indexes.ShouldContain("UX_BusinessDetail_RegistrationNumber");
        indexes.ShouldContain("UX_GroupMember_ActiveMember");
        indexes.ShouldContain("UX_GroupMember_ActiveGroupMember");
        indexes.ShouldContain("UX_CreditOfficer_ActiveStaffUser");
        indexes.ShouldContain("UX_AccountClassificationAssignment_Account_Classification");
        indexes.ShouldContain("UX_RoleGroupPermission_Group_Module_SubModule");
        indexes.ShouldContain("UX_StaffUserRoleGroup_Staff_Group_Branch");
        indexes.ShouldContain("UX_IdempotencyKey_Staff_Key");
        indexes.ShouldContain("UX_ChangeRequest_OnePendingPerEntity");
        indexes.ShouldContain("IX_PartySignature_PartyId_SupervisedOn");
        indexes.ShouldNotContain("UX_UserPermission_Staff_Branch_Module_SubModule");
        indexes.ShouldContain("IX_UserSession_StaffUserId_Open");
        indexes.ShouldContain("IX_PartyRegulatoryAttribute_PartyId_AttributeCode_RecordedOn");
        indexes.ShouldContain("IX_UserActivityLog_SessionId_EventTimestamp");
        indexes.ShouldContain("IX_DataChangeAuditLog_TableName_RecordId_LoggedOn");
        indexes.ShouldContain("UX_PostedTransaction_ReversesTransactionId");
        indexes.ShouldContain("IX_AccountLedgerEntry_AccountId_EntryId");
        indexes.ShouldContain("IX_LedgerEntry_Account_Branch_EntryDate");
        indexes.ShouldContain("IX_PostingRequest_Branch_Status_WorkingDate");
        indexes.ShouldContain("UX_LedgerAccountDailyBalance_Account_Branch_Date");
        indexes.ShouldContain("UX_SystemLedgerSetting_Branch_Code");
        indexes.ShouldContain("UX_ProductLedgerSetting_Product_Code");
        indexes.ShouldContain("UX_RoleGroupTransactionLimit_Group_Code");
        indexes.ShouldContain("UX_TillSession_OneActivePerStaff");
        indexes.ShouldContain("IX_TillSession_Branch_WorkingDate");
        indexes.ShouldContain("UX_ChargeCode_Code");
        indexes.ShouldContain("UX_ProductCharge_Product_Charge_Condition_From");
        indexes.ShouldContain("UX_ProductChargeTier_Charge_Tier");
    }

    private static async Task<string[]> QueryStringsAsync(
        SqlConnection connection,
        string sql)
    {
        var values = new List<string>();
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return [.. values];
    }

    private static async Task<List<Dictionary<string, object>>> QueryRowsAsync(
        SqlConnection connection,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        var rows = new List<Dictionary<string, object>>();
        await using var command = new SqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.GetValue(i);
            }

            rows.Add(row);
        }

        return rows;
    }
}
