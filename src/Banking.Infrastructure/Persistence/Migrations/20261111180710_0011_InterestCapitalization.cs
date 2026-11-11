using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Banking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class _0011_InterestCapitalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountInterestCapitalization",
                schema: "dbo",
                columns: table => new
                {
                    AccountInterestCapitalizationId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    FromDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ToDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    PostedTransactionId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountInterestCapitalization", x => x.AccountInterestCapitalizationId);
                    table.CheckConstraint("CK_AccountInterestCapitalization_Amounts", "[FromDate] <= [ToDate] AND [Amount] >= 0 AND [TaxAmount] >= 0 AND [TaxAmount] <= [Amount]");
                    table.ForeignKey(
                        name: "FK_AccountInterestCapitalization_Account_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "dbo",
                        principalTable: "Account",
                        principalColumn: "AccountId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountInterestCapitalization_PostedTransaction_PostedTransactionId",
                        column: x => x.PostedTransactionId,
                        principalSchema: "dbo",
                        principalTable: "PostedTransaction",
                        principalColumn: "PostedTransactionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountInterestCapitalization_Account_To",
                schema: "dbo",
                table: "AccountInterestCapitalization",
                columns: new[] { "AccountId", "ToDate" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountInterestCapitalization_PostedTransactionId",
                schema: "dbo",
                table: "AccountInterestCapitalization",
                column: "PostedTransactionId");

            migrationBuilder.CreateIndex(
                name: "UX_AccountInterestCapitalization_Account_From",
                schema: "dbo",
                table: "AccountInterestCapitalization",
                columns: new[] { "AccountId", "FromDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountInterestCapitalization",
                schema: "dbo");
        }
    }
}
