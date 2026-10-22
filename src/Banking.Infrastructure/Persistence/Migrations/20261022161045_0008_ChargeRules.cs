using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Banking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class _0008_ChargeRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChargeBasisType",
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
                    table.PrimaryKey("PK_ChargeBasisType", x => x.Id);
                    table.UniqueConstraint("AK_ChargeBasisType_Code", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "ChargeCategoryType",
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
                    table.PrimaryKey("PK_ChargeCategoryType", x => x.Id);
                    table.UniqueConstraint("AK_ChargeCategoryType_Code", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "ChargeConditionType",
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
                    table.PrimaryKey("PK_ChargeConditionType", x => x.Id);
                    table.UniqueConstraint("AK_ChargeConditionType_Code", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "ChargeShortfallPolicyType",
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
                    table.PrimaryKey("PK_ChargeShortfallPolicyType", x => x.Id);
                    table.UniqueConstraint("AK_ChargeShortfallPolicyType_Code", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "ChargeCode",
                schema: "dbo",
                columns: table => new
                {
                    ChargeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    Description = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    ChargeCategoryId = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChargeCode", x => x.ChargeId);
                    table.ForeignKey(
                        name: "FK_ChargeCode_ChargeCategoryType_ChargeCategoryId",
                        column: x => x.ChargeCategoryId,
                        principalSchema: "dbo",
                        principalTable: "ChargeCategoryType",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ChargeCode_StaffUser_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_ChargeCode_StaffUser_ModifiedByUserId",
                        column: x => x.ModifiedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                });

            migrationBuilder.CreateTable(
                name: "ProductCharge",
                schema: "dbo",
                columns: table => new
                {
                    ProductChargeId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    ChargeId = table.Column<int>(type: "int", nullable: false),
                    ChargeConditionId = table.Column<int>(type: "int", nullable: false),
                    FreeTransactions = table.Column<short>(type: "smallint", nullable: true),
                    ShortfallPolicyId = table.Column<int>(type: "int", nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
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
                    table.PrimaryKey("PK_ProductCharge", x => x.ProductChargeId);
                    table.CheckConstraint("CK_ProductCharge_EffectiveDates", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
                    table.CheckConstraint("CK_ProductCharge_FreeTransactions", "[FreeTransactions] IS NULL OR [FreeTransactions] >= 0");
                    table.ForeignKey(
                        name: "FK_ProductCharge_ChargeCode_ChargeId",
                        column: x => x.ChargeId,
                        principalSchema: "dbo",
                        principalTable: "ChargeCode",
                        principalColumn: "ChargeId");
                    table.ForeignKey(
                        name: "FK_ProductCharge_ChargeConditionType_ChargeConditionId",
                        column: x => x.ChargeConditionId,
                        principalSchema: "dbo",
                        principalTable: "ChargeConditionType",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ProductCharge_ChargeShortfallPolicyType_ShortfallPolicyId",
                        column: x => x.ShortfallPolicyId,
                        principalSchema: "dbo",
                        principalTable: "ChargeShortfallPolicyType",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ProductCharge_Product_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "dbo",
                        principalTable: "Product",
                        principalColumn: "ProductId");
                    table.ForeignKey(
                        name: "FK_ProductCharge_StaffUser_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_ProductCharge_StaffUser_ModifiedByUserId",
                        column: x => x.ModifiedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                });

            migrationBuilder.CreateTable(
                name: "ProductChargeTier",
                schema: "dbo",
                columns: table => new
                {
                    ProductChargeTierId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductChargeId = table.Column<long>(type: "bigint", nullable: false),
                    TierNumber = table.Column<short>(type: "smallint", nullable: false),
                    ChargeBasisId = table.Column<int>(type: "int", nullable: false),
                    TransactionAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    AmountOrPercentage = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    MinimumCharge = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    MaximumCharge = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductChargeTier", x => x.ProductChargeTierId);
                    table.CheckConstraint("CK_ProductChargeTier_Amount", "[AmountOrPercentage] > 0");
                    table.CheckConstraint("CK_ProductChargeTier_MinMax", "[MinimumCharge] IS NULL OR [MaximumCharge] IS NULL OR [MaximumCharge] >= [MinimumCharge]");
                    table.ForeignKey(
                        name: "FK_ProductChargeTier_ChargeBasisType_ChargeBasisId",
                        column: x => x.ChargeBasisId,
                        principalSchema: "dbo",
                        principalTable: "ChargeBasisType",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ProductChargeTier_ProductCharge_ProductChargeId",
                        column: x => x.ProductChargeId,
                        principalSchema: "dbo",
                        principalTable: "ProductCharge",
                        principalColumn: "ProductChargeId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChargeCode_ChargeCategoryId",
                schema: "dbo",
                table: "ChargeCode",
                column: "ChargeCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_ChargeCode_CreatedByUserId",
                schema: "dbo",
                table: "ChargeCode",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChargeCode_ModifiedByUserId",
                schema: "dbo",
                table: "ChargeCode",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_ChargeCode_Code",
                schema: "dbo",
                table: "ChargeCode",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductCharge_ChargeConditionId",
                schema: "dbo",
                table: "ProductCharge",
                column: "ChargeConditionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductCharge_ChargeId",
                schema: "dbo",
                table: "ProductCharge",
                column: "ChargeId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductCharge_CreatedByUserId",
                schema: "dbo",
                table: "ProductCharge",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductCharge_ModifiedByUserId",
                schema: "dbo",
                table: "ProductCharge",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductCharge_ShortfallPolicyId",
                schema: "dbo",
                table: "ProductCharge",
                column: "ShortfallPolicyId");

            migrationBuilder.CreateIndex(
                name: "UX_ProductCharge_Product_Charge_Condition_From",
                schema: "dbo",
                table: "ProductCharge",
                columns: new[] { "ProductId", "ChargeId", "ChargeConditionId", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductChargeTier_ChargeBasisId",
                schema: "dbo",
                table: "ProductChargeTier",
                column: "ChargeBasisId");

            migrationBuilder.CreateIndex(
                name: "UX_ProductChargeTier_Charge_Tier",
                schema: "dbo",
                table: "ProductChargeTier",
                columns: new[] { "ProductChargeId", "TierNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductChargeTier",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "ChargeBasisType",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "ProductCharge",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "ChargeCode",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "ChargeConditionType",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "ChargeShortfallPolicyType",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "ChargeCategoryType",
                schema: "dbo");
        }
    }
}
