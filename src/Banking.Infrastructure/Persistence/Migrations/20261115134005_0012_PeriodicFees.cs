using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Banking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class _0012_PeriodicFees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountChargePeriod",
                schema: "dbo",
                columns: table => new
                {
                    AccountChargePeriodId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    ProductChargeId = table.Column<long>(type: "bigint", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    FeeAmount = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    ChargedAmount = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    PostedTransactionId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountChargePeriod", x => x.AccountChargePeriodId);
                    table.CheckConstraint("CK_AccountChargePeriod_Amounts", "[PeriodStart] <= [PeriodEnd] AND [FeeAmount] > 0 AND [ChargedAmount] >= 0 AND [ChargedAmount] <= [FeeAmount]");
                    table.ForeignKey(
                        name: "FK_AccountChargePeriod_Account_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "dbo",
                        principalTable: "Account",
                        principalColumn: "AccountId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountChargePeriod_PostedTransaction_PostedTransactionId",
                        column: x => x.PostedTransactionId,
                        principalSchema: "dbo",
                        principalTable: "PostedTransaction",
                        principalColumn: "PostedTransactionId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountChargePeriod_ProductCharge_ProductChargeId",
                        column: x => x.ProductChargeId,
                        principalSchema: "dbo",
                        principalTable: "ProductCharge",
                        principalColumn: "ProductChargeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountChargePeriod_PostedTransactionId",
                schema: "dbo",
                table: "AccountChargePeriod",
                column: "PostedTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountChargePeriod_ProductChargeId",
                schema: "dbo",
                table: "AccountChargePeriod",
                column: "ProductChargeId");

            migrationBuilder.CreateIndex(
                name: "UX_AccountChargePeriod_Account_Start",
                schema: "dbo",
                table: "AccountChargePeriod",
                columns: new[] { "AccountId", "PeriodStart" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountChargePeriod",
                schema: "dbo");
        }
    }
}
