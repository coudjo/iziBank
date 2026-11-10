using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Banking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class _0010_InterestAccrual : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "InterestCapitalizationFrequencyId",
                schema: "dbo",
                table: "Product",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InterestDayCountId",
                schema: "dbo",
                table: "Product",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WithholdingTaxApplies",
                schema: "dbo",
                table: "Product",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AccountInterestAccrual",
                schema: "dbo",
                columns: table => new
                {
                    AccountInterestAccrualId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    AccrualDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    AccruedOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountInterestAccrual", x => x.AccountInterestAccrualId);
                    table.CheckConstraint("CK_AccountInterestAccrual_Amounts", "[Rate] >= 0 AND [Amount] >= 0");
                    table.ForeignKey(
                        name: "FK_AccountInterestAccrual_Account_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "dbo",
                        principalTable: "Account",
                        principalColumn: "AccountId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CapitalizationFrequencyType",
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
                    table.PrimaryKey("PK_CapitalizationFrequencyType", x => x.Id);
                    table.UniqueConstraint("AK_CapitalizationFrequencyType_Code", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "DayCountType",
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
                    table.PrimaryKey("PK_DayCountType", x => x.Id);
                    table.UniqueConstraint("AK_DayCountType_Code", x => x.Code);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Product_InterestCapitalizationFrequencyId",
                schema: "dbo",
                table: "Product",
                column: "InterestCapitalizationFrequencyId");

            migrationBuilder.CreateIndex(
                name: "IX_Product_InterestDayCountId",
                schema: "dbo",
                table: "Product",
                column: "InterestDayCountId");

            migrationBuilder.CreateIndex(
                name: "UX_AccountInterestAccrual_Account_Date",
                schema: "dbo",
                table: "AccountInterestAccrual",
                columns: new[] { "AccountId", "AccrualDate" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Product_CapitalizationFrequencyType_InterestCapitalizationFrequencyId",
                schema: "dbo",
                table: "Product",
                column: "InterestCapitalizationFrequencyId",
                principalSchema: "dbo",
                principalTable: "CapitalizationFrequencyType",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Product_DayCountType_InterestDayCountId",
                schema: "dbo",
                table: "Product",
                column: "InterestDayCountId",
                principalSchema: "dbo",
                principalTable: "DayCountType",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Product_CapitalizationFrequencyType_InterestCapitalizationFrequencyId",
                schema: "dbo",
                table: "Product");

            migrationBuilder.DropForeignKey(
                name: "FK_Product_DayCountType_InterestDayCountId",
                schema: "dbo",
                table: "Product");

            migrationBuilder.DropTable(
                name: "AccountInterestAccrual",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "CapitalizationFrequencyType",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "DayCountType",
                schema: "dbo");

            migrationBuilder.DropIndex(
                name: "IX_Product_InterestCapitalizationFrequencyId",
                schema: "dbo",
                table: "Product");

            migrationBuilder.DropIndex(
                name: "IX_Product_InterestDayCountId",
                schema: "dbo",
                table: "Product");

            migrationBuilder.DropColumn(
                name: "InterestCapitalizationFrequencyId",
                schema: "dbo",
                table: "Product");

            migrationBuilder.DropColumn(
                name: "InterestDayCountId",
                schema: "dbo",
                table: "Product");

            migrationBuilder.DropColumn(
                name: "WithholdingTaxApplies",
                schema: "dbo",
                table: "Product");
        }
    }
}
