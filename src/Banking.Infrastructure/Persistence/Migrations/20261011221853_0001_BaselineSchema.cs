using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Banking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class _0001_BaselineSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.Sql(
                """
                CREATE SEQUENCE [dbo].[AccountNumberSequence] AS bigint START WITH 1 INCREMENT BY 1 CACHE 50;
                CREATE SEQUENCE [dbo].[PartyNumberSequence] AS bigint START WITH 1 INCREMENT BY 1 CACHE 50;
                """);

            migrationBuilder.CreateTable(
                name: "AccountHolderRoleType",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountHolderRoleType", x => x.Id);
                    table.UniqueConstraint("AK_AccountHolderRoleType_Code", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "BusinessCategoryType",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessCategoryType", x => x.Id);
                    table.UniqueConstraint("AK_BusinessCategoryType_Code", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "BusinessDayStatusType",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessDayStatusType", x => x.Id);
                    table.UniqueConstraint("AK_BusinessDayStatusType_Code", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "CloseReasonType",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CloseReasonType", x => x.Id);
                    table.UniqueConstraint("AK_CloseReasonType_Code", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "FreezeReleaseActionType",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FreezeReleaseActionType", x => x.Id);
                    table.UniqueConstraint("AK_FreezeReleaseActionType_Code", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "GenderType",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GenderType", x => x.Id);
                    table.UniqueConstraint("AK_GenderType_Code", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "GroupMemberRoleType",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupMemberRoleType", x => x.Id);
                    table.UniqueConstraint("AK_GroupMemberRoleType_Code", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "IdentificationDocumentType",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentificationDocumentType", x => x.Id);
                    table.UniqueConstraint("AK_IdentificationDocumentType_Code", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "KYCStatusType",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KYCStatusType", x => x.Id);
                    table.UniqueConstraint("AK_KYCStatusType_Code", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "LedgerAccountType",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerAccountType", x => x.Id);
                    table.UniqueConstraint("AK_LedgerAccountType_Code", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "LogoutReasonType",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogoutReasonType", x => x.Id);
                    table.UniqueConstraint("AK_LogoutReasonType_Code", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "OperatingInstructionType",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperatingInstructionType", x => x.Id);
                    table.UniqueConstraint("AK_OperatingInstructionType_Code", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "OperatingModeType",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperatingModeType", x => x.Id);
                    table.UniqueConstraint("AK_OperatingModeType_Code", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "PartyType",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartyType", x => x.Id);
                    table.UniqueConstraint("AK_PartyType_Code", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "ProductType",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductType", x => x.Id);
                    table.UniqueConstraint("AK_ProductType_Code", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "RateType",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RateType", x => x.Id);
                    table.UniqueConstraint("AK_RateType_Code", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "RegulatoryCategoryType",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegulatoryCategoryType", x => x.Id);
                    table.UniqueConstraint("AK_RegulatoryCategoryType_Code", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "Account",
                schema: "dbo",
                columns: table => new
                {
                    AccountId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountNumber = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    BranchId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    AccountName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    DateOpened = table.Column<DateOnly>(type: "date", nullable: false),
                    OpenedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    OperatingModeId = table.Column<int>(type: "int", nullable: true),
                    ClosedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CloseReasonId = table.Column<int>(type: "int", nullable: true),
                    IsDormant = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsSupervisionRequired = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    SupervisedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    SupervisedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Account", x => x.AccountId);
                    table.CheckConstraint("CK_Account_CloseFields", "([ClosedOn] IS NULL AND [ClosedByUserId] IS NULL AND [CloseReasonId] IS NULL) OR ([ClosedOn] IS NOT NULL AND [ClosedByUserId] IS NOT NULL AND [CloseReasonId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Account_CloseReasonType_CloseReasonId",
                        column: x => x.CloseReasonId,
                        principalSchema: "dbo",
                        principalTable: "CloseReasonType",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Account_OperatingModeType_OperatingModeId",
                        column: x => x.OperatingModeId,
                        principalSchema: "dbo",
                        principalTable: "OperatingModeType",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AccountBalance",
                schema: "dbo",
                columns: table => new
                {
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    OpeningBalance = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false, defaultValue: 0m),
                    ClearBalance = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false, defaultValue: 0m),
                    FreezedAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false, defaultValue: 0m),
                    UnsupervisedCredit = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false, defaultValue: 0m),
                    UnsupervisedDebit = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false, defaultValue: 0m),
                    OverdrawLimit = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false, defaultValue: 0m),
                    AvailableBalance = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false, defaultValue: 0m),
                    TotalBalance = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false, defaultValue: 0m),
                    LastCreditTransactionDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastDebitTransactionDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountBalance", x => x.AccountId);
                    table.ForeignKey(
                        name: "FK_AccountBalance_Account_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "dbo",
                        principalTable: "Account",
                        principalColumn: "AccountId");
                });

            migrationBuilder.CreateTable(
                name: "AccountBalanceDailySnapshot",
                schema: "dbo",
                columns: table => new
                {
                    SnapshotId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    SnapshotDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ClosingBalance = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountBalanceDailySnapshot", x => x.SnapshotId);
                    table.ForeignKey(
                        name: "FK_AccountBalanceDailySnapshot_Account_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "dbo",
                        principalTable: "Account",
                        principalColumn: "AccountId");
                });

            migrationBuilder.CreateTable(
                name: "AccountClassification",
                schema: "dbo",
                columns: table => new
                {
                    AccountClassificationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClassificationCode = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    ClassificationName = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountClassification", x => x.AccountClassificationId);
                });

            migrationBuilder.CreateTable(
                name: "AccountClassificationAssignment",
                schema: "dbo",
                columns: table => new
                {
                    AssignmentId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    AccountClassificationId = table.Column<int>(type: "int", nullable: false),
                    AccountSubClassificationId = table.Column<int>(type: "int", nullable: true),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountClassificationAssignment", x => x.AssignmentId);
                    table.ForeignKey(
                        name: "FK_AccountClassificationAssignment_AccountClassification_AccountClassificationId",
                        column: x => x.AccountClassificationId,
                        principalSchema: "dbo",
                        principalTable: "AccountClassification",
                        principalColumn: "AccountClassificationId");
                    table.ForeignKey(
                        name: "FK_AccountClassificationAssignment_Account_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "dbo",
                        principalTable: "Account",
                        principalColumn: "AccountId");
                });

            migrationBuilder.CreateTable(
                name: "AccountCondition",
                schema: "dbo",
                columns: table => new
                {
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    IsFreezed = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DoNotAllowCredit = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DoNotAllowDebit = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreditLimit = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    DebitLimit = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    SuperviseAllTransactions = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DoNotApplyTransactionCharges = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountCondition", x => x.AccountId);
                    table.ForeignKey(
                        name: "FK_AccountCondition_Account_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "dbo",
                        principalTable: "Account",
                        principalColumn: "AccountId");
                });

            migrationBuilder.CreateTable(
                name: "AccountHolder",
                schema: "dbo",
                columns: table => new
                {
                    AccountHolderId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    PartyId = table.Column<long>(type: "bigint", nullable: false),
                    AccountHolderRoleId = table.Column<int>(type: "int", nullable: false),
                    OperatingInstructionId = table.Column<int>(type: "int", nullable: true),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountHolder", x => x.AccountHolderId);
                    table.ForeignKey(
                        name: "FK_AccountHolder_AccountHolderRoleType_AccountHolderRoleId",
                        column: x => x.AccountHolderRoleId,
                        principalSchema: "dbo",
                        principalTable: "AccountHolderRoleType",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AccountHolder_Account_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "dbo",
                        principalTable: "Account",
                        principalColumn: "AccountId");
                    table.ForeignKey(
                        name: "FK_AccountHolder_OperatingInstructionType_OperatingInstructionId",
                        column: x => x.OperatingInstructionId,
                        principalSchema: "dbo",
                        principalTable: "OperatingInstructionType",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AccountReminder",
                schema: "dbo",
                columns: table => new
                {
                    AccountReminderId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    ReminderText = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountReminder", x => x.AccountReminderId);
                    table.ForeignKey(
                        name: "FK_AccountReminder_Account_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "dbo",
                        principalTable: "Account",
                        principalColumn: "AccountId");
                });

            migrationBuilder.CreateTable(
                name: "AccountSubClassification",
                schema: "dbo",
                columns: table => new
                {
                    AccountSubClassificationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountClassificationId = table.Column<int>(type: "int", nullable: false),
                    SubClassificationCode = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    SubClassificationName = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountSubClassification", x => x.AccountSubClassificationId);
                    table.ForeignKey(
                        name: "FK_AccountSubClassification_AccountClassification_AccountClassificationId",
                        column: x => x.AccountClassificationId,
                        principalSchema: "dbo",
                        principalTable: "AccountClassification",
                        principalColumn: "AccountClassificationId");
                });

            migrationBuilder.CreateTable(
                name: "Branch",
                schema: "dbo",
                columns: table => new
                {
                    BranchId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BranchCode = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    BranchName = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    OperatingLicenseNo = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    ClearingDays = table.Column<short>(type: "smallint", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Branch", x => x.BranchId);
                });

            migrationBuilder.CreateTable(
                name: "BusinessDayStatus",
                schema: "dbo",
                columns: table => new
                {
                    BusinessDayStatusId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BranchId = table.Column<int>(type: "int", nullable: false),
                    WorkingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    BusinessDayStart = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BusinessDayEnd = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CurrentStatusId = table.Column<int>(type: "int", nullable: true),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessDayStatus", x => x.BusinessDayStatusId);
                    table.ForeignKey(
                        name: "FK_BusinessDayStatus_Branch_BranchId",
                        column: x => x.BranchId,
                        principalSchema: "dbo",
                        principalTable: "Branch",
                        principalColumn: "BranchId");
                    table.ForeignKey(
                        name: "FK_BusinessDayStatus_BusinessDayStatusType_CurrentStatusId",
                        column: x => x.CurrentStatusId,
                        principalSchema: "dbo",
                        principalTable: "BusinessDayStatusType",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "BusinessDetail",
                schema: "dbo",
                columns: table => new
                {
                    PartyId = table.Column<long>(type: "bigint", nullable: false),
                    BusinessName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    RegistrationNumber = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: true),
                    TaxIdentificationNumber = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    IncorporationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    BusinessCategoryId = table.Column<int>(type: "int", nullable: true),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessDetail", x => x.PartyId);
                    table.ForeignKey(
                        name: "FK_BusinessDetail_BusinessCategoryType_BusinessCategoryId",
                        column: x => x.BusinessCategoryId,
                        principalSchema: "dbo",
                        principalTable: "BusinessCategoryType",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CreditOfficer",
                schema: "dbo",
                columns: table => new
                {
                    CreditOfficerId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BranchId = table.Column<int>(type: "int", nullable: false),
                    StaffUserId = table.Column<long>(type: "bigint", nullable: false),
                    IsDisabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditOfficer", x => x.CreditOfficerId);
                    table.ForeignKey(
                        name: "FK_CreditOfficer_Branch_BranchId",
                        column: x => x.BranchId,
                        principalSchema: "dbo",
                        principalTable: "Branch",
                        principalColumn: "BranchId");
                });

            migrationBuilder.CreateTable(
                name: "Currency",
                schema: "dbo",
                columns: table => new
                {
                    CurrencyId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CurrencyCode = table.Column<string>(type: "varchar(5)", unicode: false, maxLength: 5, nullable: false),
                    CurrencyName = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    IsLocalCurrency = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Currency", x => x.CurrencyId);
                });

            migrationBuilder.CreateTable(
                name: "CustomerDigitalAccess",
                schema: "dbo",
                columns: table => new
                {
                    CustomerDigitalAccessId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PartyId = table.Column<long>(type: "bigint", nullable: false),
                    AccessCode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    SecretKeyHash = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    IsDisabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerDigitalAccess", x => x.CustomerDigitalAccessId);
                });

            migrationBuilder.CreateTable(
                name: "DefinedHoliday",
                schema: "dbo",
                columns: table => new
                {
                    HolidayId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HolidayDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Remarks = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DefinedHoliday", x => x.HolidayId);
                });

            migrationBuilder.CreateTable(
                name: "DormantAccountHistory",
                schema: "dbo",
                columns: table => new
                {
                    DormantHistoryId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    ActivatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    DateActivated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DormantAccountHistory", x => x.DormantHistoryId);
                    table.ForeignKey(
                        name: "FK_DormantAccountHistory_Account_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "dbo",
                        principalTable: "Account",
                        principalColumn: "AccountId");
                });

            migrationBuilder.CreateTable(
                name: "EventType",
                schema: "dbo",
                columns: table => new
                {
                    EventTypeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventCode = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    EventName = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventType", x => x.EventTypeId);
                });

            migrationBuilder.CreateTable(
                name: "FreezeReleaseAccountBalance",
                schema: "dbo",
                columns: table => new
                {
                    FreezeReleaseId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    FreezeReleaseActionId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    Remarks = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: true),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FreezeReleaseAccountBalance", x => x.FreezeReleaseId);
                    table.CheckConstraint("CK_FreezeReleaseAccountBalance_Amount", "[Amount] IS NULL OR [Amount] > 0");
                    table.ForeignKey(
                        name: "FK_FreezeReleaseAccountBalance_Account_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "dbo",
                        principalTable: "Account",
                        principalColumn: "AccountId");
                    table.ForeignKey(
                        name: "FK_FreezeReleaseAccountBalance_FreezeReleaseActionType_FreezeReleaseActionId",
                        column: x => x.FreezeReleaseActionId,
                        principalSchema: "dbo",
                        principalTable: "FreezeReleaseActionType",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "GroupDetail",
                schema: "dbo",
                columns: table => new
                {
                    PartyId = table.Column<long>(type: "bigint", nullable: false),
                    GroupName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FormationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    MeetingFrequency = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    ChairpersonPartyId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupDetail", x => x.PartyId);
                });

            migrationBuilder.CreateTable(
                name: "GroupMember",
                schema: "dbo",
                columns: table => new
                {
                    GroupMemberId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GroupPartyId = table.Column<long>(type: "bigint", nullable: false),
                    MemberPartyId = table.Column<long>(type: "bigint", nullable: false),
                    GroupMemberRoleId = table.Column<int>(type: "int", nullable: true),
                    JoinedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    LeftOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupMember", x => x.GroupMemberId);
                    table.ForeignKey(
                        name: "FK_GroupMember_GroupMemberRoleType_GroupMemberRoleId",
                        column: x => x.GroupMemberRoleId,
                        principalSchema: "dbo",
                        principalTable: "GroupMemberRoleType",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "IndividualDetail",
                schema: "dbo",
                columns: table => new
                {
                    PartyId = table.Column<long>(type: "bigint", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    FirstName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    GenderId = table.Column<int>(type: "int", nullable: true),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: true),
                    Nationality = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    IdentificationTypeId = table.Column<int>(type: "int", nullable: true),
                    IdentificationNumber = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndividualDetail", x => x.PartyId);
                    table.ForeignKey(
                        name: "FK_IndividualDetail_GenderType_GenderId",
                        column: x => x.GenderId,
                        principalSchema: "dbo",
                        principalTable: "GenderType",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_IndividualDetail_IdentificationDocumentType_IdentificationTypeId",
                        column: x => x.IdentificationTypeId,
                        principalSchema: "dbo",
                        principalTable: "IdentificationDocumentType",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "InstitutionProfile",
                schema: "dbo",
                columns: table => new
                {
                    InstitutionProfileId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InstitutionName = table.Column<string>(type: "varchar(150)", unicode: false, maxLength: 150, nullable: false),
                    LicenseNumber = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    RegulatoryCategoryId = table.Column<int>(type: "int", nullable: true),
                    IncorporationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    HeadOfficeAddress = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstitutionProfile", x => x.InstitutionProfileId);
                    table.CheckConstraint("CK_InstitutionProfile_Singleton", "[InstitutionProfileId] = 1");
                    table.ForeignKey(
                        name: "FK_InstitutionProfile_RegulatoryCategoryType_RegulatoryCategoryId",
                        column: x => x.RegulatoryCategoryId,
                        principalSchema: "dbo",
                        principalTable: "RegulatoryCategoryType",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LedgerAccount",
                schema: "dbo",
                columns: table => new
                {
                    LedgerAccountId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BranchId = table.Column<int>(type: "int", nullable: false),
                    LedgerAccountCode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    AccountName = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    LedgerAccountTypeId = table.Column<int>(type: "int", nullable: true),
                    CurrencyId = table.Column<int>(type: "int", nullable: true),
                    Balance = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false, defaultValue: 0m),
                    IsSupervisionRequired = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    SupervisedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    SupervisedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerAccount", x => x.LedgerAccountId);
                    table.ForeignKey(
                        name: "FK_LedgerAccount_Branch_BranchId",
                        column: x => x.BranchId,
                        principalSchema: "dbo",
                        principalTable: "Branch",
                        principalColumn: "BranchId");
                    table.ForeignKey(
                        name: "FK_LedgerAccount_Currency_CurrencyId",
                        column: x => x.CurrencyId,
                        principalSchema: "dbo",
                        principalTable: "Currency",
                        principalColumn: "CurrencyId");
                    table.ForeignKey(
                        name: "FK_LedgerAccount_LedgerAccountType_LedgerAccountTypeId",
                        column: x => x.LedgerAccountTypeId,
                        principalSchema: "dbo",
                        principalTable: "LedgerAccountType",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StaffUser",
                schema: "dbo",
                columns: table => new
                {
                    StaffUserId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BranchId = table.Column<int>(type: "int", nullable: false),
                    StaffCode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsTeller = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    TellerGLAccountId = table.Column<long>(type: "bigint", nullable: true),
                    IsDisabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    SecretKeyHash = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    CanChangeSecretKey = table.Column<bool>(type: "bit", nullable: true),
                    ChangeKeyAtNextLogOn = table.Column<bool>(type: "bit", nullable: true),
                    SecretKeyChangedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SecretKeyExpiryDays = table.Column<short>(type: "smallint", nullable: true),
                    CanSwitchBranch = table.Column<bool>(type: "bit", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffUser", x => x.StaffUserId);
                    table.ForeignKey(
                        name: "FK_StaffUser_Branch_BranchId",
                        column: x => x.BranchId,
                        principalSchema: "dbo",
                        principalTable: "Branch",
                        principalColumn: "BranchId");
                    table.ForeignKey(
                        name: "FK_StaffUser_LedgerAccount_TellerGLAccountId",
                        column: x => x.TellerGLAccountId,
                        principalSchema: "dbo",
                        principalTable: "LedgerAccount",
                        principalColumn: "LedgerAccountId");
                    table.ForeignKey(
                        name: "FK_StaffUser_StaffUser_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_StaffUser_StaffUser_ModifiedByUserId",
                        column: x => x.ModifiedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                });

            migrationBuilder.CreateTable(
                name: "Module",
                schema: "dbo",
                columns: table => new
                {
                    ModuleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ModuleCode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    ModuleName = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    HasDataEntry = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Module", x => x.ModuleId);
                    table.ForeignKey(
                        name: "FK_Module_StaffUser_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_Module_StaffUser_ModifiedByUserId",
                        column: x => x.ModifiedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                });

            migrationBuilder.CreateTable(
                name: "Party",
                schema: "dbo",
                columns: table => new
                {
                    PartyId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PartyNumber = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PartyTypeId = table.Column<int>(type: "int", nullable: false),
                    Address1 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Address2 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Phone = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    Mobile = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    EmailAddress = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    KYCStatusId = table.Column<int>(type: "int", nullable: true),
                    SourceOfFunds = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: true),
                    OccupationOrSector = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    IsPoliticallyExposed = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreditOfficerId = table.Column<long>(type: "bigint", nullable: true),
                    IsSupervisionRequired = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    SupervisedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    SupervisedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Party", x => x.PartyId);
                    table.ForeignKey(
                        name: "FK_Party_CreditOfficer_CreditOfficerId",
                        column: x => x.CreditOfficerId,
                        principalSchema: "dbo",
                        principalTable: "CreditOfficer",
                        principalColumn: "CreditOfficerId");
                    table.ForeignKey(
                        name: "FK_Party_KYCStatusType_KYCStatusId",
                        column: x => x.KYCStatusId,
                        principalSchema: "dbo",
                        principalTable: "KYCStatusType",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Party_PartyType_PartyTypeId",
                        column: x => x.PartyTypeId,
                        principalSchema: "dbo",
                        principalTable: "PartyType",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Party_StaffUser_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_Party_StaffUser_ModifiedByUserId",
                        column: x => x.ModifiedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_Party_StaffUser_SupervisedByUserId",
                        column: x => x.SupervisedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                });

            migrationBuilder.CreateTable(
                name: "Product",
                schema: "dbo",
                columns: table => new
                {
                    ProductId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BranchId = table.Column<int>(type: "int", nullable: false),
                    ProductCode = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    ProductName = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    ProductTypeId = table.Column<int>(type: "int", nullable: false),
                    AccountPrefix = table.Column<string>(type: "varchar(6)", unicode: false, maxLength: 6, nullable: false),
                    CurrencyId = table.Column<int>(type: "int", nullable: false),
                    LedgerControlAccountId = table.Column<long>(type: "bigint", nullable: true),
                    CreditInterestRate = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    DebitInterestRate = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    MinimumBalance = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    AccountsPerCustomer = table.Column<short>(type: "smallint", nullable: true),
                    IsFreezed = table.Column<bool>(type: "bit", nullable: true),
                    IsChequeBookAllowed = table.Column<bool>(type: "bit", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsSupervisionRequired = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    SupervisedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    SupervisedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Product", x => x.ProductId);
                    table.ForeignKey(
                        name: "FK_Product_Branch_BranchId",
                        column: x => x.BranchId,
                        principalSchema: "dbo",
                        principalTable: "Branch",
                        principalColumn: "BranchId");
                    table.ForeignKey(
                        name: "FK_Product_Currency_CurrencyId",
                        column: x => x.CurrencyId,
                        principalSchema: "dbo",
                        principalTable: "Currency",
                        principalColumn: "CurrencyId");
                    table.ForeignKey(
                        name: "FK_Product_LedgerAccount_LedgerControlAccountId",
                        column: x => x.LedgerControlAccountId,
                        principalSchema: "dbo",
                        principalTable: "LedgerAccount",
                        principalColumn: "LedgerAccountId");
                    table.ForeignKey(
                        name: "FK_Product_ProductType_ProductTypeId",
                        column: x => x.ProductTypeId,
                        principalSchema: "dbo",
                        principalTable: "ProductType",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Product_StaffUser_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_Product_StaffUser_ModifiedByUserId",
                        column: x => x.ModifiedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_Product_StaffUser_SupervisedByUserId",
                        column: x => x.SupervisedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                });

            migrationBuilder.CreateTable(
                name: "UserSession",
                schema: "dbo",
                columns: table => new
                {
                    SessionId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StaffUserId = table.Column<long>(type: "bigint", nullable: true),
                    CustomerDigitalAccessId = table.Column<long>(type: "bigint", nullable: true),
                    LoginAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LogoutAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LogoutReasonId = table.Column<int>(type: "int", nullable: true),
                    IPAddress = table.Column<string>(type: "varchar(45)", unicode: false, maxLength: 45, nullable: true),
                    DeviceInfo = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSession", x => x.SessionId);
                    table.CheckConstraint("CK_UserSession_ExactlyOnePrincipal", "([StaffUserId] IS NOT NULL AND [CustomerDigitalAccessId] IS NULL) OR ([StaffUserId] IS NULL AND [CustomerDigitalAccessId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_UserSession_CustomerDigitalAccess_CustomerDigitalAccessId",
                        column: x => x.CustomerDigitalAccessId,
                        principalSchema: "dbo",
                        principalTable: "CustomerDigitalAccess",
                        principalColumn: "CustomerDigitalAccessId");
                    table.ForeignKey(
                        name: "FK_UserSession_LogoutReasonType_LogoutReasonId",
                        column: x => x.LogoutReasonId,
                        principalSchema: "dbo",
                        principalTable: "LogoutReasonType",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserSession_StaffUser_StaffUserId",
                        column: x => x.StaffUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                });

            migrationBuilder.CreateTable(
                name: "SubModule",
                schema: "dbo",
                columns: table => new
                {
                    SubModuleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ModuleId = table.Column<int>(type: "int", nullable: false),
                    SubModuleCode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    SubModuleName = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubModule", x => x.SubModuleId);
                    table.ForeignKey(
                        name: "FK_SubModule_Module_ModuleId",
                        column: x => x.ModuleId,
                        principalSchema: "dbo",
                        principalTable: "Module",
                        principalColumn: "ModuleId");
                    table.ForeignKey(
                        name: "FK_SubModule_StaffUser_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_SubModule_StaffUser_ModifiedByUserId",
                        column: x => x.ModifiedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                });

            migrationBuilder.CreateTable(
                name: "PartyBeneficialOwner",
                schema: "dbo",
                columns: table => new
                {
                    PartyBeneficialOwnerId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BusinessPartyId = table.Column<long>(type: "bigint", nullable: false),
                    OwnerPartyId = table.Column<long>(type: "bigint", nullable: false),
                    OwnershipPercentage = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartyBeneficialOwner", x => x.PartyBeneficialOwnerId);
                    table.CheckConstraint("CK_PartyBeneficialOwner_OwnershipPercentage", "[OwnershipPercentage] > 0 AND [OwnershipPercentage] <= 100");
                    table.ForeignKey(
                        name: "FK_PartyBeneficialOwner_BusinessDetail_BusinessPartyId",
                        column: x => x.BusinessPartyId,
                        principalSchema: "dbo",
                        principalTable: "BusinessDetail",
                        principalColumn: "PartyId");
                    table.ForeignKey(
                        name: "FK_PartyBeneficialOwner_Party_OwnerPartyId",
                        column: x => x.OwnerPartyId,
                        principalSchema: "dbo",
                        principalTable: "Party",
                        principalColumn: "PartyId");
                    table.ForeignKey(
                        name: "FK_PartyBeneficialOwner_StaffUser_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_PartyBeneficialOwner_StaffUser_ModifiedByUserId",
                        column: x => x.ModifiedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                });

            migrationBuilder.CreateTable(
                name: "PartyIdentificationDocument",
                schema: "dbo",
                columns: table => new
                {
                    PartyIdentificationDocumentId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PartyId = table.Column<long>(type: "bigint", nullable: false),
                    DocumentTypeId = table.Column<int>(type: "int", nullable: false),
                    DocumentNumber = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    ImageValue = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    IssuedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpiresOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartyIdentificationDocument", x => x.PartyIdentificationDocumentId);
                    table.CheckConstraint("CK_PartyIdentificationDocument_Dates", "[ExpiresOn] IS NULL OR [IssuedOn] IS NULL OR [ExpiresOn] >= [IssuedOn]");
                    table.ForeignKey(
                        name: "FK_PartyIdentificationDocument_IdentificationDocumentType_DocumentTypeId",
                        column: x => x.DocumentTypeId,
                        principalSchema: "dbo",
                        principalTable: "IdentificationDocumentType",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PartyIdentificationDocument_Party_PartyId",
                        column: x => x.PartyId,
                        principalSchema: "dbo",
                        principalTable: "Party",
                        principalColumn: "PartyId");
                    table.ForeignKey(
                        name: "FK_PartyIdentificationDocument_StaffUser_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_PartyIdentificationDocument_StaffUser_ModifiedByUserId",
                        column: x => x.ModifiedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                });

            migrationBuilder.CreateTable(
                name: "PartyImage",
                schema: "dbo",
                columns: table => new
                {
                    PartyId = table.Column<long>(type: "bigint", nullable: false),
                    ImageValue = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    IsSupervisionRequired = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    SupervisedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    SupervisedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartyImage", x => x.PartyId);
                    table.ForeignKey(
                        name: "FK_PartyImage_Party_PartyId",
                        column: x => x.PartyId,
                        principalSchema: "dbo",
                        principalTable: "Party",
                        principalColumn: "PartyId");
                    table.ForeignKey(
                        name: "FK_PartyImage_StaffUser_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_PartyImage_StaffUser_ModifiedByUserId",
                        column: x => x.ModifiedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_PartyImage_StaffUser_SupervisedByUserId",
                        column: x => x.SupervisedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                });

            migrationBuilder.CreateTable(
                name: "PartyNextOfKin",
                schema: "dbo",
                columns: table => new
                {
                    PartyNextOfKinId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PartyId = table.Column<long>(type: "bigint", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Relationship = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: true),
                    MobileNumber = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartyNextOfKin", x => x.PartyNextOfKinId);
                    table.ForeignKey(
                        name: "FK_PartyNextOfKin_Party_PartyId",
                        column: x => x.PartyId,
                        principalSchema: "dbo",
                        principalTable: "Party",
                        principalColumn: "PartyId");
                    table.ForeignKey(
                        name: "FK_PartyNextOfKin_StaffUser_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_PartyNextOfKin_StaffUser_ModifiedByUserId",
                        column: x => x.ModifiedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                });

            migrationBuilder.CreateTable(
                name: "PartyRegulatoryAttribute",
                schema: "dbo",
                columns: table => new
                {
                    PartyRegulatoryAttributeId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PartyId = table.Column<long>(type: "bigint", nullable: false),
                    AttributeCode = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    AttributeValue = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: true),
                    RecordedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartyRegulatoryAttribute", x => x.PartyRegulatoryAttributeId);
                    table.ForeignKey(
                        name: "FK_PartyRegulatoryAttribute_Party_PartyId",
                        column: x => x.PartyId,
                        principalSchema: "dbo",
                        principalTable: "Party",
                        principalColumn: "PartyId");
                    table.ForeignKey(
                        name: "FK_PartyRegulatoryAttribute_StaffUser_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_PartyRegulatoryAttribute_StaffUser_ModifiedByUserId",
                        column: x => x.ModifiedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                });

            migrationBuilder.CreateTable(
                name: "ProductInterestRate",
                schema: "dbo",
                columns: table => new
                {
                    ProductInterestRateId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    RateTypeId = table.Column<int>(type: "int", nullable: true),
                    MinimumAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    MaximumAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    InterestRate = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductInterestRate", x => x.ProductInterestRateId);
                    table.CheckConstraint("CK_ProductInterestRate_AmountBand", "[MinimumAmount] IS NULL OR [MaximumAmount] IS NULL OR [MinimumAmount] <= [MaximumAmount]");
                    table.CheckConstraint("CK_ProductInterestRate_EffectiveDates", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
                    table.ForeignKey(
                        name: "FK_ProductInterestRate_Product_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "dbo",
                        principalTable: "Product",
                        principalColumn: "ProductId");
                    table.ForeignKey(
                        name: "FK_ProductInterestRate_RateType_RateTypeId",
                        column: x => x.RateTypeId,
                        principalSchema: "dbo",
                        principalTable: "RateType",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ProductInterestRate_StaffUser_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_ProductInterestRate_StaffUser_ModifiedByUserId",
                        column: x => x.ModifiedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                });

            migrationBuilder.CreateTable(
                name: "UserPermission",
                schema: "dbo",
                columns: table => new
                {
                    UserPermissionId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StaffUserId = table.Column<long>(type: "bigint", nullable: false),
                    BranchId = table.Column<int>(type: "int", nullable: false),
                    ModuleId = table.Column<int>(type: "int", nullable: false),
                    SubModuleId = table.Column<int>(type: "int", nullable: true),
                    AllowAdd = table.Column<bool>(type: "bit", nullable: true),
                    AllowEdit = table.Column<bool>(type: "bit", nullable: true),
                    AllowDelete = table.Column<bool>(type: "bit", nullable: true),
                    CanSupervise = table.Column<bool>(type: "bit", nullable: true),
                    CreditLimit = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    DebitLimit = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    CreditSupervisionLimit = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    DebitSupervisionLimit = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPermission", x => x.UserPermissionId);
                    table.ForeignKey(
                        name: "FK_UserPermission_Branch_BranchId",
                        column: x => x.BranchId,
                        principalSchema: "dbo",
                        principalTable: "Branch",
                        principalColumn: "BranchId");
                    table.ForeignKey(
                        name: "FK_UserPermission_Module_ModuleId",
                        column: x => x.ModuleId,
                        principalSchema: "dbo",
                        principalTable: "Module",
                        principalColumn: "ModuleId");
                    table.ForeignKey(
                        name: "FK_UserPermission_StaffUser_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_UserPermission_StaffUser_ModifiedByUserId",
                        column: x => x.ModifiedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_UserPermission_StaffUser_StaffUserId",
                        column: x => x.StaffUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_UserPermission_SubModule_SubModuleId",
                        column: x => x.SubModuleId,
                        principalSchema: "dbo",
                        principalTable: "SubModule",
                        principalColumn: "SubModuleId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Account_AccountNumber",
                schema: "dbo",
                table: "Account",
                column: "AccountNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Account_BranchId_ProductId",
                schema: "dbo",
                table: "Account",
                columns: new[] { "BranchId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_Account_ClosedByUserId",
                schema: "dbo",
                table: "Account",
                column: "ClosedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Account_CloseReasonId",
                schema: "dbo",
                table: "Account",
                column: "CloseReasonId");

            migrationBuilder.CreateIndex(
                name: "IX_Account_CreatedByUserId",
                schema: "dbo",
                table: "Account",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Account_ModifiedByUserId",
                schema: "dbo",
                table: "Account",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Account_OpenedByUserId",
                schema: "dbo",
                table: "Account",
                column: "OpenedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Account_OperatingModeId",
                schema: "dbo",
                table: "Account",
                column: "OperatingModeId");

            migrationBuilder.CreateIndex(
                name: "IX_Account_ProductId",
                schema: "dbo",
                table: "Account",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Account_SupervisedByUserId",
                schema: "dbo",
                table: "Account",
                column: "SupervisedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_AccountBalanceDailySnapshot_AccountId_SnapshotDate",
                schema: "dbo",
                table: "AccountBalanceDailySnapshot",
                columns: new[] { "AccountId", "SnapshotDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountClassification_ClassificationCode",
                schema: "dbo",
                table: "AccountClassification",
                column: "ClassificationCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountClassification_CreatedByUserId",
                schema: "dbo",
                table: "AccountClassification",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountClassification_ModifiedByUserId",
                schema: "dbo",
                table: "AccountClassification",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountClassificationAssignment_AccountClassificationId",
                schema: "dbo",
                table: "AccountClassificationAssignment",
                column: "AccountClassificationId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountClassificationAssignment_AccountSubClassificationId",
                schema: "dbo",
                table: "AccountClassificationAssignment",
                column: "AccountSubClassificationId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountClassificationAssignment_CreatedByUserId",
                schema: "dbo",
                table: "AccountClassificationAssignment",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountClassificationAssignment_ModifiedByUserId",
                schema: "dbo",
                table: "AccountClassificationAssignment",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_AccountClassificationAssignment_Account_Classification",
                schema: "dbo",
                table: "AccountClassificationAssignment",
                columns: new[] { "AccountId", "AccountClassificationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountCondition_CreatedByUserId",
                schema: "dbo",
                table: "AccountCondition",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountCondition_ModifiedByUserId",
                schema: "dbo",
                table: "AccountCondition",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountHolder_AccountHolderRoleId",
                schema: "dbo",
                table: "AccountHolder",
                column: "AccountHolderRoleId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountHolder_AccountId_Role",
                schema: "dbo",
                table: "AccountHolder",
                columns: new[] { "AccountId", "AccountHolderRoleId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountHolder_CreatedByUserId",
                schema: "dbo",
                table: "AccountHolder",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountHolder_ModifiedByUserId",
                schema: "dbo",
                table: "AccountHolder",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountHolder_OperatingInstructionId",
                schema: "dbo",
                table: "AccountHolder",
                column: "OperatingInstructionId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountHolder_PartyId",
                schema: "dbo",
                table: "AccountHolder",
                column: "PartyId");

            migrationBuilder.CreateIndex(
                name: "UX_AccountHolder_AccountId_PartyId",
                schema: "dbo",
                table: "AccountHolder",
                columns: new[] { "AccountId", "PartyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountReminder_AccountId",
                schema: "dbo",
                table: "AccountReminder",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountReminder_CreatedByUserId",
                schema: "dbo",
                table: "AccountReminder",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountReminder_ModifiedByUserId",
                schema: "dbo",
                table: "AccountReminder",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountSubClassification_AccountClassificationId",
                schema: "dbo",
                table: "AccountSubClassification",
                column: "AccountClassificationId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountSubClassification_CreatedByUserId",
                schema: "dbo",
                table: "AccountSubClassification",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountSubClassification_ModifiedByUserId",
                schema: "dbo",
                table: "AccountSubClassification",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountSubClassification_SubClassificationCode",
                schema: "dbo",
                table: "AccountSubClassification",
                column: "SubClassificationCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Branch_BranchCode",
                schema: "dbo",
                table: "Branch",
                column: "BranchCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Branch_CreatedByUserId",
                schema: "dbo",
                table: "Branch",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Branch_ModifiedByUserId",
                schema: "dbo",
                table: "Branch",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessDayStatus_CreatedByUserId",
                schema: "dbo",
                table: "BusinessDayStatus",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessDayStatus_CurrentStatusId",
                schema: "dbo",
                table: "BusinessDayStatus",
                column: "CurrentStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessDayStatus_ModifiedByUserId",
                schema: "dbo",
                table: "BusinessDayStatus",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_BusinessDayStatus_BranchId_WorkingDate",
                schema: "dbo",
                table: "BusinessDayStatus",
                columns: new[] { "BranchId", "WorkingDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessDetail_BusinessCategoryId",
                schema: "dbo",
                table: "BusinessDetail",
                column: "BusinessCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessDetail_CreatedByUserId",
                schema: "dbo",
                table: "BusinessDetail",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessDetail_ModifiedByUserId",
                schema: "dbo",
                table: "BusinessDetail",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_BusinessDetail_RegistrationNumber",
                schema: "dbo",
                table: "BusinessDetail",
                column: "RegistrationNumber",
                unique: true,
                filter: "[RegistrationNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CreditOfficer_BranchId",
                schema: "dbo",
                table: "CreditOfficer",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditOfficer_CreatedByUserId",
                schema: "dbo",
                table: "CreditOfficer",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditOfficer_ModifiedByUserId",
                schema: "dbo",
                table: "CreditOfficer",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_CreditOfficer_ActiveStaffUser",
                schema: "dbo",
                table: "CreditOfficer",
                column: "StaffUserId",
                unique: true,
                filter: "[IsDisabled] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Currency_CreatedByUserId",
                schema: "dbo",
                table: "Currency",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Currency_CurrencyCode",
                schema: "dbo",
                table: "Currency",
                column: "CurrencyCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Currency_ModifiedByUserId",
                schema: "dbo",
                table: "Currency",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_Currency_IsLocalCurrency",
                schema: "dbo",
                table: "Currency",
                column: "IsLocalCurrency",
                unique: true,
                filter: "[IsLocalCurrency] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDigitalAccess_AccessCode",
                schema: "dbo",
                table: "CustomerDigitalAccess",
                column: "AccessCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDigitalAccess_CreatedByUserId",
                schema: "dbo",
                table: "CustomerDigitalAccess",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDigitalAccess_ModifiedByUserId",
                schema: "dbo",
                table: "CustomerDigitalAccess",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDigitalAccess_PartyId",
                schema: "dbo",
                table: "CustomerDigitalAccess",
                column: "PartyId");

            migrationBuilder.CreateIndex(
                name: "IX_DefinedHoliday_CreatedByUserId",
                schema: "dbo",
                table: "DefinedHoliday",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DefinedHoliday_HolidayDate",
                schema: "dbo",
                table: "DefinedHoliday",
                column: "HolidayDate",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DefinedHoliday_ModifiedByUserId",
                schema: "dbo",
                table: "DefinedHoliday",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DormantAccountHistory_AccountId",
                schema: "dbo",
                table: "DormantAccountHistory",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_DormantAccountHistory_ActivatedByUserId",
                schema: "dbo",
                table: "DormantAccountHistory",
                column: "ActivatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DormantAccountHistory_CreatedByUserId",
                schema: "dbo",
                table: "DormantAccountHistory",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DormantAccountHistory_ModifiedByUserId",
                schema: "dbo",
                table: "DormantAccountHistory",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EventType_CreatedByUserId",
                schema: "dbo",
                table: "EventType",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EventType_EventCode",
                schema: "dbo",
                table: "EventType",
                column: "EventCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventType_ModifiedByUserId",
                schema: "dbo",
                table: "EventType",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FreezeReleaseAccountBalance_AccountId_FreezeReleaseId",
                schema: "dbo",
                table: "FreezeReleaseAccountBalance",
                columns: new[] { "AccountId", "FreezeReleaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_FreezeReleaseAccountBalance_CreatedByUserId",
                schema: "dbo",
                table: "FreezeReleaseAccountBalance",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FreezeReleaseAccountBalance_FreezeReleaseActionId",
                schema: "dbo",
                table: "FreezeReleaseAccountBalance",
                column: "FreezeReleaseActionId");

            migrationBuilder.CreateIndex(
                name: "IX_FreezeReleaseAccountBalance_ModifiedByUserId",
                schema: "dbo",
                table: "FreezeReleaseAccountBalance",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupDetail_ChairpersonPartyId",
                schema: "dbo",
                table: "GroupDetail",
                column: "ChairpersonPartyId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupDetail_CreatedByUserId",
                schema: "dbo",
                table: "GroupDetail",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupDetail_ModifiedByUserId",
                schema: "dbo",
                table: "GroupDetail",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupMember_CreatedByUserId",
                schema: "dbo",
                table: "GroupMember",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupMember_GroupMemberRoleId",
                schema: "dbo",
                table: "GroupMember",
                column: "GroupMemberRoleId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupMember_GroupPartyId",
                schema: "dbo",
                table: "GroupMember",
                column: "GroupPartyId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupMember_ModifiedByUserId",
                schema: "dbo",
                table: "GroupMember",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_GroupMember_ActiveGroupMember",
                schema: "dbo",
                table: "GroupMember",
                columns: new[] { "GroupPartyId", "MemberPartyId" },
                unique: true,
                filter: "[LeftOn] IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_GroupMember_ActiveMember",
                schema: "dbo",
                table: "GroupMember",
                column: "MemberPartyId",
                unique: true,
                filter: "[LeftOn] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IndividualDetail_CreatedByUserId",
                schema: "dbo",
                table: "IndividualDetail",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_IndividualDetail_GenderId",
                schema: "dbo",
                table: "IndividualDetail",
                column: "GenderId");

            migrationBuilder.CreateIndex(
                name: "IX_IndividualDetail_ModifiedByUserId",
                schema: "dbo",
                table: "IndividualDetail",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_IndividualDetail_Identification",
                schema: "dbo",
                table: "IndividualDetail",
                columns: new[] { "IdentificationTypeId", "IdentificationNumber" },
                unique: true,
                filter: "[IdentificationNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionProfile_CreatedByUserId",
                schema: "dbo",
                table: "InstitutionProfile",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionProfile_LicenseNumber",
                schema: "dbo",
                table: "InstitutionProfile",
                column: "LicenseNumber",
                unique: true,
                filter: "[LicenseNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionProfile_ModifiedByUserId",
                schema: "dbo",
                table: "InstitutionProfile",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionProfile_RegulatoryCategoryId",
                schema: "dbo",
                table: "InstitutionProfile",
                column: "RegulatoryCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerAccount_BranchId",
                schema: "dbo",
                table: "LedgerAccount",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerAccount_CreatedByUserId",
                schema: "dbo",
                table: "LedgerAccount",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerAccount_CurrencyId",
                schema: "dbo",
                table: "LedgerAccount",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerAccount_LedgerAccountCode",
                schema: "dbo",
                table: "LedgerAccount",
                column: "LedgerAccountCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerAccount_LedgerAccountTypeId",
                schema: "dbo",
                table: "LedgerAccount",
                column: "LedgerAccountTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerAccount_ModifiedByUserId",
                schema: "dbo",
                table: "LedgerAccount",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerAccount_SupervisedByUserId",
                schema: "dbo",
                table: "LedgerAccount",
                column: "SupervisedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Module_CreatedByUserId",
                schema: "dbo",
                table: "Module",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Module_ModifiedByUserId",
                schema: "dbo",
                table: "Module",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Module_ModuleCode",
                schema: "dbo",
                table: "Module",
                column: "ModuleCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Party_CreatedByUserId",
                schema: "dbo",
                table: "Party",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Party_CreditOfficerId",
                schema: "dbo",
                table: "Party",
                column: "CreditOfficerId");

            migrationBuilder.CreateIndex(
                name: "IX_Party_DisplayName",
                schema: "dbo",
                table: "Party",
                column: "DisplayName");

            migrationBuilder.CreateIndex(
                name: "IX_Party_KYCStatusId",
                schema: "dbo",
                table: "Party",
                column: "KYCStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_Party_Mobile",
                schema: "dbo",
                table: "Party",
                column: "Mobile");

            migrationBuilder.CreateIndex(
                name: "IX_Party_ModifiedByUserId",
                schema: "dbo",
                table: "Party",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Party_PartyNumber",
                schema: "dbo",
                table: "Party",
                column: "PartyNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Party_PartyTypeId_KYCStatusId",
                schema: "dbo",
                table: "Party",
                columns: new[] { "PartyTypeId", "KYCStatusId" });

            migrationBuilder.CreateIndex(
                name: "IX_Party_SupervisedByUserId",
                schema: "dbo",
                table: "Party",
                column: "SupervisedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PartyBeneficialOwner_CreatedByUserId",
                schema: "dbo",
                table: "PartyBeneficialOwner",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PartyBeneficialOwner_ModifiedByUserId",
                schema: "dbo",
                table: "PartyBeneficialOwner",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PartyBeneficialOwner_OwnerPartyId",
                schema: "dbo",
                table: "PartyBeneficialOwner",
                column: "OwnerPartyId");

            migrationBuilder.CreateIndex(
                name: "UX_PartyBeneficialOwner_Business_Owner",
                schema: "dbo",
                table: "PartyBeneficialOwner",
                columns: new[] { "BusinessPartyId", "OwnerPartyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartyIdentificationDocument_CreatedByUserId",
                schema: "dbo",
                table: "PartyIdentificationDocument",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PartyIdentificationDocument_DocumentTypeId",
                schema: "dbo",
                table: "PartyIdentificationDocument",
                column: "DocumentTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_PartyIdentificationDocument_ModifiedByUserId",
                schema: "dbo",
                table: "PartyIdentificationDocument",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PartyIdentificationDocument_PartyId",
                schema: "dbo",
                table: "PartyIdentificationDocument",
                column: "PartyId");

            migrationBuilder.CreateIndex(
                name: "IX_PartyImage_CreatedByUserId",
                schema: "dbo",
                table: "PartyImage",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PartyImage_ModifiedByUserId",
                schema: "dbo",
                table: "PartyImage",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PartyImage_SupervisedByUserId",
                schema: "dbo",
                table: "PartyImage",
                column: "SupervisedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PartyNextOfKin_CreatedByUserId",
                schema: "dbo",
                table: "PartyNextOfKin",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PartyNextOfKin_ModifiedByUserId",
                schema: "dbo",
                table: "PartyNextOfKin",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PartyNextOfKin_PartyId",
                schema: "dbo",
                table: "PartyNextOfKin",
                column: "PartyId");

            migrationBuilder.CreateIndex(
                name: "IX_PartyRegulatoryAttribute_CreatedByUserId",
                schema: "dbo",
                table: "PartyRegulatoryAttribute",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PartyRegulatoryAttribute_ModifiedByUserId",
                schema: "dbo",
                table: "PartyRegulatoryAttribute",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PartyRegulatoryAttribute_PartyId_AttributeCode_RecordedOn",
                schema: "dbo",
                table: "PartyRegulatoryAttribute",
                columns: new[] { "PartyId", "AttributeCode", "RecordedOn" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Product_BranchId",
                schema: "dbo",
                table: "Product",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Product_CreatedByUserId",
                schema: "dbo",
                table: "Product",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Product_CurrencyId",
                schema: "dbo",
                table: "Product",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_Product_LedgerControlAccountId",
                schema: "dbo",
                table: "Product",
                column: "LedgerControlAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Product_ModifiedByUserId",
                schema: "dbo",
                table: "Product",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Product_ProductCode",
                schema: "dbo",
                table: "Product",
                column: "ProductCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Product_ProductTypeId",
                schema: "dbo",
                table: "Product",
                column: "ProductTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Product_SupervisedByUserId",
                schema: "dbo",
                table: "Product",
                column: "SupervisedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductInterestRate_CreatedByUserId",
                schema: "dbo",
                table: "ProductInterestRate",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductInterestRate_ModifiedByUserId",
                schema: "dbo",
                table: "ProductInterestRate",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductInterestRate_ProductId_RateTypeId_EffectiveFrom",
                schema: "dbo",
                table: "ProductInterestRate",
                columns: new[] { "ProductId", "RateTypeId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductInterestRate_RateTypeId",
                schema: "dbo",
                table: "ProductInterestRate",
                column: "RateTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffUser_BranchId",
                schema: "dbo",
                table: "StaffUser",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffUser_CreatedByUserId",
                schema: "dbo",
                table: "StaffUser",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffUser_ModifiedByUserId",
                schema: "dbo",
                table: "StaffUser",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffUser_StaffCode",
                schema: "dbo",
                table: "StaffUser",
                column: "StaffCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffUser_TellerGLAccountId",
                schema: "dbo",
                table: "StaffUser",
                column: "TellerGLAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SubModule_CreatedByUserId",
                schema: "dbo",
                table: "SubModule",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SubModule_ModifiedByUserId",
                schema: "dbo",
                table: "SubModule",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SubModule_ModuleId",
                schema: "dbo",
                table: "SubModule",
                column: "ModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_SubModule_SubModuleCode",
                schema: "dbo",
                table: "SubModule",
                column: "SubModuleCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserPermission_BranchId",
                schema: "dbo",
                table: "UserPermission",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPermission_CreatedByUserId",
                schema: "dbo",
                table: "UserPermission",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPermission_ModifiedByUserId",
                schema: "dbo",
                table: "UserPermission",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPermission_ModuleId",
                schema: "dbo",
                table: "UserPermission",
                column: "ModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPermission_SubModuleId",
                schema: "dbo",
                table: "UserPermission",
                column: "SubModuleId");

            migrationBuilder.CreateIndex(
                name: "UX_UserPermission_Staff_Branch_Module_SubModule",
                schema: "dbo",
                table: "UserPermission",
                columns: new[] { "StaffUserId", "BranchId", "ModuleId", "SubModuleId" },
                unique: true,
                filter: "[SubModuleId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserSession_CustomerDigitalAccessId",
                schema: "dbo",
                table: "UserSession",
                column: "CustomerDigitalAccessId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSession_LogoutReasonId",
                schema: "dbo",
                table: "UserSession",
                column: "LogoutReasonId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSession_StaffUserId_Open",
                schema: "dbo",
                table: "UserSession",
                column: "StaffUserId",
                filter: "[LogoutAt] IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Account_Branch_BranchId",
                schema: "dbo",
                table: "Account",
                column: "BranchId",
                principalSchema: "dbo",
                principalTable: "Branch",
                principalColumn: "BranchId");

            migrationBuilder.AddForeignKey(
                name: "FK_Account_Product_ProductId",
                schema: "dbo",
                table: "Account",
                column: "ProductId",
                principalSchema: "dbo",
                principalTable: "Product",
                principalColumn: "ProductId");

            migrationBuilder.AddForeignKey(
                name: "FK_Account_StaffUser_ClosedByUserId",
                schema: "dbo",
                table: "Account",
                column: "ClosedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Account_StaffUser_CreatedByUserId",
                schema: "dbo",
                table: "Account",
                column: "CreatedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Account_StaffUser_ModifiedByUserId",
                schema: "dbo",
                table: "Account",
                column: "ModifiedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Account_StaffUser_OpenedByUserId",
                schema: "dbo",
                table: "Account",
                column: "OpenedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Account_StaffUser_SupervisedByUserId",
                schema: "dbo",
                table: "Account",
                column: "SupervisedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_AccountClassification_StaffUser_CreatedByUserId",
                schema: "dbo",
                table: "AccountClassification",
                column: "CreatedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_AccountClassification_StaffUser_ModifiedByUserId",
                schema: "dbo",
                table: "AccountClassification",
                column: "ModifiedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_AccountClassificationAssignment_AccountSubClassification_AccountSubClassificationId",
                schema: "dbo",
                table: "AccountClassificationAssignment",
                column: "AccountSubClassificationId",
                principalSchema: "dbo",
                principalTable: "AccountSubClassification",
                principalColumn: "AccountSubClassificationId");

            migrationBuilder.AddForeignKey(
                name: "FK_AccountClassificationAssignment_StaffUser_CreatedByUserId",
                schema: "dbo",
                table: "AccountClassificationAssignment",
                column: "CreatedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_AccountClassificationAssignment_StaffUser_ModifiedByUserId",
                schema: "dbo",
                table: "AccountClassificationAssignment",
                column: "ModifiedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_AccountCondition_StaffUser_CreatedByUserId",
                schema: "dbo",
                table: "AccountCondition",
                column: "CreatedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_AccountCondition_StaffUser_ModifiedByUserId",
                schema: "dbo",
                table: "AccountCondition",
                column: "ModifiedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_AccountHolder_Party_PartyId",
                schema: "dbo",
                table: "AccountHolder",
                column: "PartyId",
                principalSchema: "dbo",
                principalTable: "Party",
                principalColumn: "PartyId");

            migrationBuilder.AddForeignKey(
                name: "FK_AccountHolder_StaffUser_CreatedByUserId",
                schema: "dbo",
                table: "AccountHolder",
                column: "CreatedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_AccountHolder_StaffUser_ModifiedByUserId",
                schema: "dbo",
                table: "AccountHolder",
                column: "ModifiedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_AccountReminder_StaffUser_CreatedByUserId",
                schema: "dbo",
                table: "AccountReminder",
                column: "CreatedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_AccountReminder_StaffUser_ModifiedByUserId",
                schema: "dbo",
                table: "AccountReminder",
                column: "ModifiedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_AccountSubClassification_StaffUser_CreatedByUserId",
                schema: "dbo",
                table: "AccountSubClassification",
                column: "CreatedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_AccountSubClassification_StaffUser_ModifiedByUserId",
                schema: "dbo",
                table: "AccountSubClassification",
                column: "ModifiedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Branch_StaffUser_CreatedByUserId",
                schema: "dbo",
                table: "Branch",
                column: "CreatedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Branch_StaffUser_ModifiedByUserId",
                schema: "dbo",
                table: "Branch",
                column: "ModifiedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_BusinessDayStatus_StaffUser_CreatedByUserId",
                schema: "dbo",
                table: "BusinessDayStatus",
                column: "CreatedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_BusinessDayStatus_StaffUser_ModifiedByUserId",
                schema: "dbo",
                table: "BusinessDayStatus",
                column: "ModifiedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_BusinessDetail_Party_PartyId",
                schema: "dbo",
                table: "BusinessDetail",
                column: "PartyId",
                principalSchema: "dbo",
                principalTable: "Party",
                principalColumn: "PartyId");

            migrationBuilder.AddForeignKey(
                name: "FK_BusinessDetail_StaffUser_CreatedByUserId",
                schema: "dbo",
                table: "BusinessDetail",
                column: "CreatedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_BusinessDetail_StaffUser_ModifiedByUserId",
                schema: "dbo",
                table: "BusinessDetail",
                column: "ModifiedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_CreditOfficer_StaffUser_CreatedByUserId",
                schema: "dbo",
                table: "CreditOfficer",
                column: "CreatedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_CreditOfficer_StaffUser_ModifiedByUserId",
                schema: "dbo",
                table: "CreditOfficer",
                column: "ModifiedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_CreditOfficer_StaffUser_StaffUserId",
                schema: "dbo",
                table: "CreditOfficer",
                column: "StaffUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Currency_StaffUser_CreatedByUserId",
                schema: "dbo",
                table: "Currency",
                column: "CreatedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Currency_StaffUser_ModifiedByUserId",
                schema: "dbo",
                table: "Currency",
                column: "ModifiedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerDigitalAccess_Party_PartyId",
                schema: "dbo",
                table: "CustomerDigitalAccess",
                column: "PartyId",
                principalSchema: "dbo",
                principalTable: "Party",
                principalColumn: "PartyId");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerDigitalAccess_StaffUser_CreatedByUserId",
                schema: "dbo",
                table: "CustomerDigitalAccess",
                column: "CreatedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerDigitalAccess_StaffUser_ModifiedByUserId",
                schema: "dbo",
                table: "CustomerDigitalAccess",
                column: "ModifiedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_DefinedHoliday_StaffUser_CreatedByUserId",
                schema: "dbo",
                table: "DefinedHoliday",
                column: "CreatedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_DefinedHoliday_StaffUser_ModifiedByUserId",
                schema: "dbo",
                table: "DefinedHoliday",
                column: "ModifiedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_DormantAccountHistory_StaffUser_ActivatedByUserId",
                schema: "dbo",
                table: "DormantAccountHistory",
                column: "ActivatedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_DormantAccountHistory_StaffUser_CreatedByUserId",
                schema: "dbo",
                table: "DormantAccountHistory",
                column: "CreatedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_DormantAccountHistory_StaffUser_ModifiedByUserId",
                schema: "dbo",
                table: "DormantAccountHistory",
                column: "ModifiedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_EventType_StaffUser_CreatedByUserId",
                schema: "dbo",
                table: "EventType",
                column: "CreatedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_EventType_StaffUser_ModifiedByUserId",
                schema: "dbo",
                table: "EventType",
                column: "ModifiedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_FreezeReleaseAccountBalance_StaffUser_CreatedByUserId",
                schema: "dbo",
                table: "FreezeReleaseAccountBalance",
                column: "CreatedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_FreezeReleaseAccountBalance_StaffUser_ModifiedByUserId",
                schema: "dbo",
                table: "FreezeReleaseAccountBalance",
                column: "ModifiedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_GroupDetail_Party_ChairpersonPartyId",
                schema: "dbo",
                table: "GroupDetail",
                column: "ChairpersonPartyId",
                principalSchema: "dbo",
                principalTable: "Party",
                principalColumn: "PartyId");

            migrationBuilder.AddForeignKey(
                name: "FK_GroupDetail_Party_PartyId",
                schema: "dbo",
                table: "GroupDetail",
                column: "PartyId",
                principalSchema: "dbo",
                principalTable: "Party",
                principalColumn: "PartyId");

            migrationBuilder.AddForeignKey(
                name: "FK_GroupDetail_StaffUser_CreatedByUserId",
                schema: "dbo",
                table: "GroupDetail",
                column: "CreatedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_GroupDetail_StaffUser_ModifiedByUserId",
                schema: "dbo",
                table: "GroupDetail",
                column: "ModifiedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_GroupMember_Party_GroupPartyId",
                schema: "dbo",
                table: "GroupMember",
                column: "GroupPartyId",
                principalSchema: "dbo",
                principalTable: "Party",
                principalColumn: "PartyId");

            migrationBuilder.AddForeignKey(
                name: "FK_GroupMember_Party_MemberPartyId",
                schema: "dbo",
                table: "GroupMember",
                column: "MemberPartyId",
                principalSchema: "dbo",
                principalTable: "Party",
                principalColumn: "PartyId");

            migrationBuilder.AddForeignKey(
                name: "FK_GroupMember_StaffUser_CreatedByUserId",
                schema: "dbo",
                table: "GroupMember",
                column: "CreatedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_GroupMember_StaffUser_ModifiedByUserId",
                schema: "dbo",
                table: "GroupMember",
                column: "ModifiedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_IndividualDetail_Party_PartyId",
                schema: "dbo",
                table: "IndividualDetail",
                column: "PartyId",
                principalSchema: "dbo",
                principalTable: "Party",
                principalColumn: "PartyId");

            migrationBuilder.AddForeignKey(
                name: "FK_IndividualDetail_StaffUser_CreatedByUserId",
                schema: "dbo",
                table: "IndividualDetail",
                column: "CreatedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_IndividualDetail_StaffUser_ModifiedByUserId",
                schema: "dbo",
                table: "IndividualDetail",
                column: "ModifiedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_InstitutionProfile_StaffUser_CreatedByUserId",
                schema: "dbo",
                table: "InstitutionProfile",
                column: "CreatedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_InstitutionProfile_StaffUser_ModifiedByUserId",
                schema: "dbo",
                table: "InstitutionProfile",
                column: "ModifiedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_LedgerAccount_StaffUser_CreatedByUserId",
                schema: "dbo",
                table: "LedgerAccount",
                column: "CreatedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_LedgerAccount_StaffUser_ModifiedByUserId",
                schema: "dbo",
                table: "LedgerAccount",
                column: "ModifiedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_LedgerAccount_StaffUser_SupervisedByUserId",
                schema: "dbo",
                table: "LedgerAccount",
                column: "SupervisedByUserId",
                principalSchema: "dbo",
                principalTable: "StaffUser",
                principalColumn: "StaffUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LedgerAccount_Branch_BranchId",
                schema: "dbo",
                table: "LedgerAccount");

            migrationBuilder.DropForeignKey(
                name: "FK_StaffUser_Branch_BranchId",
                schema: "dbo",
                table: "StaffUser");

            migrationBuilder.DropForeignKey(
                name: "FK_Currency_StaffUser_CreatedByUserId",
                schema: "dbo",
                table: "Currency");

            migrationBuilder.DropForeignKey(
                name: "FK_Currency_StaffUser_ModifiedByUserId",
                schema: "dbo",
                table: "Currency");

            migrationBuilder.DropForeignKey(
                name: "FK_LedgerAccount_StaffUser_CreatedByUserId",
                schema: "dbo",
                table: "LedgerAccount");

            migrationBuilder.DropForeignKey(
                name: "FK_LedgerAccount_StaffUser_ModifiedByUserId",
                schema: "dbo",
                table: "LedgerAccount");

            migrationBuilder.DropForeignKey(
                name: "FK_LedgerAccount_StaffUser_SupervisedByUserId",
                schema: "dbo",
                table: "LedgerAccount");

            migrationBuilder.DropTable(
                name: "AccountBalance",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "AccountBalanceDailySnapshot",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "AccountClassificationAssignment",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "AccountCondition",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "AccountHolder",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "AccountReminder",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "BusinessDayStatus",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "DefinedHoliday",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "DormantAccountHistory",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "EventType",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "FreezeReleaseAccountBalance",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "GroupDetail",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "GroupMember",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "IndividualDetail",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "InstitutionProfile",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "PartyBeneficialOwner",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "PartyIdentificationDocument",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "PartyImage",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "PartyNextOfKin",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "PartyRegulatoryAttribute",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "ProductInterestRate",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "UserPermission",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "UserSession",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "AccountSubClassification",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "AccountHolderRoleType",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "OperatingInstructionType",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "BusinessDayStatusType",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Account",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "FreezeReleaseActionType",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "GroupMemberRoleType",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "GenderType",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "RegulatoryCategoryType",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "BusinessDetail",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "IdentificationDocumentType",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "RateType",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "SubModule",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "CustomerDigitalAccess",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "LogoutReasonType",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "AccountClassification",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "CloseReasonType",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "OperatingModeType",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Product",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "BusinessCategoryType",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Module",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Party",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "ProductType",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "CreditOfficer",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "KYCStatusType",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "PartyType",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Branch",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "StaffUser",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "LedgerAccount",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Currency",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "LedgerAccountType",
                schema: "dbo");

            migrationBuilder.DropSequence(
                name: "AccountNumberSequence",
                schema: "dbo");

            migrationBuilder.DropSequence(
                name: "PartyNumberSequence",
                schema: "dbo");
        }
    }
}
