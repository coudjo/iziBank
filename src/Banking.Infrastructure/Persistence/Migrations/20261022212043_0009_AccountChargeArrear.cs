using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Banking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class _0009_AccountChargeArrear : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountChargeArrear",
                schema: "dbo",
                columns: table => new
                {
                    AccountChargeArrearId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    ProductChargeId = table.Column<long>(type: "bigint", nullable: false),
                    RaisedTransactionId = table.Column<long>(type: "bigint", nullable: false),
                    RaisedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    OutstandingAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountChargeArrear", x => x.AccountChargeArrearId);
                    table.CheckConstraint("CK_AccountChargeArrear_Amounts", "[Amount] > 0 AND [OutstandingAmount] >= 0 AND [OutstandingAmount] <= [Amount]");
                    table.ForeignKey(
                        name: "FK_AccountChargeArrear_Account_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "dbo",
                        principalTable: "Account",
                        principalColumn: "AccountId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountChargeArrear_PostedTransaction_RaisedTransactionId",
                        column: x => x.RaisedTransactionId,
                        principalSchema: "dbo",
                        principalTable: "PostedTransaction",
                        principalColumn: "PostedTransactionId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountChargeArrear_ProductCharge_ProductChargeId",
                        column: x => x.ProductChargeId,
                        principalSchema: "dbo",
                        principalTable: "ProductCharge",
                        principalColumn: "ProductChargeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountChargeArrear_Account_Raised",
                schema: "dbo",
                table: "AccountChargeArrear",
                columns: new[] { "AccountId", "RaisedOn", "AccountChargeArrearId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountChargeArrear_ProductChargeId",
                schema: "dbo",
                table: "AccountChargeArrear",
                column: "ProductChargeId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountChargeArrear_RaisedTransactionId",
                schema: "dbo",
                table: "AccountChargeArrear",
                column: "RaisedTransactionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountChargeArrear",
                schema: "dbo");
        }
    }
}
