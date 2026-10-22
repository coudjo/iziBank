using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Banking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class _0005_Postings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "TransactionReferenceSequence",
                schema: "dbo");

            migrationBuilder.CreateTable(
                name: "LedgerAccountDailyBalance",
                schema: "dbo",
                columns: table => new
                {
                    LedgerAccountDailyBalanceId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LedgerAccountId = table.Column<long>(type: "bigint", nullable: false),
                    BranchId = table.Column<int>(type: "int", nullable: false),
                    BalanceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ClosingBalance = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerAccountDailyBalance", x => x.LedgerAccountDailyBalanceId);
                    table.ForeignKey(
                        name: "FK_LedgerAccountDailyBalance_Branch_BranchId",
                        column: x => x.BranchId,
                        principalSchema: "dbo",
                        principalTable: "Branch",
                        principalColumn: "BranchId");
                    table.ForeignKey(
                        name: "FK_LedgerAccountDailyBalance_LedgerAccount_LedgerAccountId",
                        column: x => x.LedgerAccountId,
                        principalSchema: "dbo",
                        principalTable: "LedgerAccount",
                        principalColumn: "LedgerAccountId");
                });

            migrationBuilder.CreateTable(
                name: "TransactionCode",
                schema: "dbo",
                columns: table => new
                {
                    TransactionCodeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    DefaultNarration = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsSystemCode = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionCode", x => x.TransactionCodeId);
                    table.ForeignKey(
                        name: "FK_TransactionCode_StaffUser_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_TransactionCode_StaffUser_ModifiedByUserId",
                        column: x => x.ModifiedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                });

            migrationBuilder.CreateTable(
                name: "PostingRequest",
                schema: "dbo",
                columns: table => new
                {
                    PostingRequestId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Reference = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    BranchId = table.Column<int>(type: "int", nullable: false),
                    WorkingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TransactionCodeId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    PostedTransactionId = table.Column<long>(type: "bigint", nullable: true),
                    RequestedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    RequestedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SupervisedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    SupervisedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecisionReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostingRequest", x => x.PostingRequestId);
                    table.CheckConstraint("CK_PostingRequest_Amount", "[Amount] > 0");
                    table.CheckConstraint("CK_PostingRequest_Status", "[Status] IN ('PENDING', 'POSTED', 'REJECTED', 'CANCELLED', 'EXPIRED')");
                    table.ForeignKey(
                        name: "FK_PostingRequest_Branch_BranchId",
                        column: x => x.BranchId,
                        principalSchema: "dbo",
                        principalTable: "Branch",
                        principalColumn: "BranchId");
                    table.ForeignKey(
                        name: "FK_PostingRequest_StaffUser_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_PostingRequest_StaffUser_ModifiedByUserId",
                        column: x => x.ModifiedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_PostingRequest_StaffUser_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_PostingRequest_StaffUser_SupervisedByUserId",
                        column: x => x.SupervisedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_PostingRequest_TransactionCode_TransactionCodeId",
                        column: x => x.TransactionCodeId,
                        principalSchema: "dbo",
                        principalTable: "TransactionCode",
                        principalColumn: "TransactionCodeId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerAccountDailyBalance_BranchId",
                schema: "dbo",
                table: "LedgerAccountDailyBalance",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "UX_LedgerAccountDailyBalance_Account_Branch_Date",
                schema: "dbo",
                table: "LedgerAccountDailyBalance",
                columns: new[] { "LedgerAccountId", "BranchId", "BalanceDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PostingRequest_Branch_Status_WorkingDate",
                schema: "dbo",
                table: "PostingRequest",
                columns: new[] { "BranchId", "Status", "WorkingDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PostingRequest_CreatedByUserId",
                schema: "dbo",
                table: "PostingRequest",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PostingRequest_ModifiedByUserId",
                schema: "dbo",
                table: "PostingRequest",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PostingRequest_Reference",
                schema: "dbo",
                table: "PostingRequest",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PostingRequest_RequestedByUserId",
                schema: "dbo",
                table: "PostingRequest",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PostingRequest_SupervisedByUserId",
                schema: "dbo",
                table: "PostingRequest",
                column: "SupervisedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PostingRequest_TransactionCodeId",
                schema: "dbo",
                table: "PostingRequest",
                column: "TransactionCodeId");

            migrationBuilder.CreateIndex(
                name: "UX_PostingRequest_PostedTransactionId",
                schema: "dbo",
                table: "PostingRequest",
                column: "PostedTransactionId",
                unique: true,
                filter: "[PostedTransactionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionCode_Code",
                schema: "dbo",
                table: "TransactionCode",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransactionCode_CreatedByUserId",
                schema: "dbo",
                table: "TransactionCode",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionCode_ModifiedByUserId",
                schema: "dbo",
                table: "TransactionCode",
                column: "ModifiedByUserId");

            migrationBuilder.Sql(PostingLedgerSql.Create);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(PostingLedgerSql.Drop);
            migrationBuilder.DropTable(
                name: "LedgerAccountDailyBalance",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "PostingRequest",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "TransactionCode",
                schema: "dbo");

            migrationBuilder.DropSequence(
                name: "TransactionReferenceSequence",
                schema: "dbo");
        }
    }
}
