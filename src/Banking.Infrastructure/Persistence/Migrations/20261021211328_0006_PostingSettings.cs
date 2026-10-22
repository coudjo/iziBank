using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Banking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class _0006_PostingSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductLedgerSetting",
                schema: "dbo",
                columns: table => new
                {
                    ProductLedgerSettingId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    SettingCode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    LedgerAccountId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductLedgerSetting", x => x.ProductLedgerSettingId);
                    table.ForeignKey(
                        name: "FK_ProductLedgerSetting_LedgerAccount_LedgerAccountId",
                        column: x => x.LedgerAccountId,
                        principalSchema: "dbo",
                        principalTable: "LedgerAccount",
                        principalColumn: "LedgerAccountId");
                    table.ForeignKey(
                        name: "FK_ProductLedgerSetting_Product_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "dbo",
                        principalTable: "Product",
                        principalColumn: "ProductId");
                    table.ForeignKey(
                        name: "FK_ProductLedgerSetting_StaffUser_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_ProductLedgerSetting_StaffUser_ModifiedByUserId",
                        column: x => x.ModifiedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                });

            migrationBuilder.CreateTable(
                name: "RoleGroupTransactionLimit",
                schema: "dbo",
                columns: table => new
                {
                    RoleGroupTransactionLimitId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleGroupId = table.Column<int>(type: "int", nullable: false),
                    LimitCode = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    MaxAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleGroupTransactionLimit", x => x.RoleGroupTransactionLimitId);
                    table.CheckConstraint("CK_RoleGroupTransactionLimit_MaxAmount", "[MaxAmount] > 0");
                    table.ForeignKey(
                        name: "FK_RoleGroupTransactionLimit_RoleGroup_RoleGroupId",
                        column: x => x.RoleGroupId,
                        principalSchema: "dbo",
                        principalTable: "RoleGroup",
                        principalColumn: "RoleGroupId");
                    table.ForeignKey(
                        name: "FK_RoleGroupTransactionLimit_StaffUser_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_RoleGroupTransactionLimit_StaffUser_ModifiedByUserId",
                        column: x => x.ModifiedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                });

            migrationBuilder.CreateTable(
                name: "SystemLedgerSetting",
                schema: "dbo",
                columns: table => new
                {
                    SystemLedgerSettingId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BranchId = table.Column<int>(type: "int", nullable: false),
                    SettingCode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    LedgerAccountId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemLedgerSetting", x => x.SystemLedgerSettingId);
                    table.ForeignKey(
                        name: "FK_SystemLedgerSetting_Branch_BranchId",
                        column: x => x.BranchId,
                        principalSchema: "dbo",
                        principalTable: "Branch",
                        principalColumn: "BranchId");
                    table.ForeignKey(
                        name: "FK_SystemLedgerSetting_LedgerAccount_LedgerAccountId",
                        column: x => x.LedgerAccountId,
                        principalSchema: "dbo",
                        principalTable: "LedgerAccount",
                        principalColumn: "LedgerAccountId");
                    table.ForeignKey(
                        name: "FK_SystemLedgerSetting_StaffUser_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_SystemLedgerSetting_StaffUser_ModifiedByUserId",
                        column: x => x.ModifiedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductLedgerSetting_CreatedByUserId",
                schema: "dbo",
                table: "ProductLedgerSetting",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductLedgerSetting_LedgerAccountId",
                schema: "dbo",
                table: "ProductLedgerSetting",
                column: "LedgerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductLedgerSetting_ModifiedByUserId",
                schema: "dbo",
                table: "ProductLedgerSetting",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_ProductLedgerSetting_Product_Code",
                schema: "dbo",
                table: "ProductLedgerSetting",
                columns: new[] { "ProductId", "SettingCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoleGroupTransactionLimit_CreatedByUserId",
                schema: "dbo",
                table: "RoleGroupTransactionLimit",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RoleGroupTransactionLimit_ModifiedByUserId",
                schema: "dbo",
                table: "RoleGroupTransactionLimit",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_RoleGroupTransactionLimit_Group_Code",
                schema: "dbo",
                table: "RoleGroupTransactionLimit",
                columns: new[] { "RoleGroupId", "LimitCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SystemLedgerSetting_CreatedByUserId",
                schema: "dbo",
                table: "SystemLedgerSetting",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemLedgerSetting_LedgerAccountId",
                schema: "dbo",
                table: "SystemLedgerSetting",
                column: "LedgerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemLedgerSetting_ModifiedByUserId",
                schema: "dbo",
                table: "SystemLedgerSetting",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_SystemLedgerSetting_Branch_Code",
                schema: "dbo",
                table: "SystemLedgerSetting",
                columns: new[] { "BranchId", "SettingCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductLedgerSetting",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "RoleGroupTransactionLimit",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "SystemLedgerSetting",
                schema: "dbo");
        }
    }
}
