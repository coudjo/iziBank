using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Banking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class _0007_TillSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TillSession",
                schema: "dbo",
                columns: table => new
                {
                    TillSessionId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StaffUserId = table.Column<long>(type: "bigint", nullable: false),
                    BranchId = table.Column<int>(type: "int", nullable: false),
                    WorkingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TellerLedgerAccountId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    OpenedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClosedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CountedAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    ExpectedAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    ClosePostingRequestId = table.Column<long>(type: "bigint", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TillSession", x => x.TillSessionId);
                    table.CheckConstraint("CK_TillSession_Status", "[Status] IN ('OPEN', 'CLOSING', 'CLOSED')");
                    table.ForeignKey(
                        name: "FK_TillSession_Branch_BranchId",
                        column: x => x.BranchId,
                        principalSchema: "dbo",
                        principalTable: "Branch",
                        principalColumn: "BranchId");
                    table.ForeignKey(
                        name: "FK_TillSession_LedgerAccount_TellerLedgerAccountId",
                        column: x => x.TellerLedgerAccountId,
                        principalSchema: "dbo",
                        principalTable: "LedgerAccount",
                        principalColumn: "LedgerAccountId");
                    table.ForeignKey(
                        name: "FK_TillSession_StaffUser_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_TillSession_StaffUser_ModifiedByUserId",
                        column: x => x.ModifiedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_TillSession_StaffUser_StaffUserId",
                        column: x => x.StaffUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_TillSession_Branch_WorkingDate",
                schema: "dbo",
                table: "TillSession",
                columns: new[] { "BranchId", "WorkingDate" });

            migrationBuilder.CreateIndex(
                name: "IX_TillSession_CreatedByUserId",
                schema: "dbo",
                table: "TillSession",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TillSession_ModifiedByUserId",
                schema: "dbo",
                table: "TillSession",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TillSession_TellerLedgerAccountId",
                schema: "dbo",
                table: "TillSession",
                column: "TellerLedgerAccountId");

            migrationBuilder.CreateIndex(
                name: "UX_TillSession_OneActivePerStaff",
                schema: "dbo",
                table: "TillSession",
                column: "StaffUserId",
                unique: true,
                filter: "[Status] <> 'CLOSED'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TillSession",
                schema: "dbo");
        }
    }
}
