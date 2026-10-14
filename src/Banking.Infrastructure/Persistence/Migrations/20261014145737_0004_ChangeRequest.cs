using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Banking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class _0004_ChangeRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChangeRequest",
                schema: "dbo",
                columns: table => new
                {
                    ChangeRequestId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntityType = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    EntityId = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    ChangeType = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RequestedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    RequestedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DecidedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    DecidedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChangeRequest", x => x.ChangeRequestId);
                    table.ForeignKey(
                        name: "FK_ChangeRequest_StaffUser_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_ChangeRequest_StaffUser_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_ChangeRequest_StaffUser_ModifiedByUserId",
                        column: x => x.ModifiedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_ChangeRequest_StaffUser_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequest_CreatedByUserId",
                schema: "dbo",
                table: "ChangeRequest",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequest_DecidedByUserId",
                schema: "dbo",
                table: "ChangeRequest",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequest_ModifiedByUserId",
                schema: "dbo",
                table: "ChangeRequest",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequest_RequestedByUserId",
                schema: "dbo",
                table: "ChangeRequest",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequest_Status_RequestedOn",
                schema: "dbo",
                table: "ChangeRequest",
                columns: new[] { "Status", "RequestedOn" });

            migrationBuilder.CreateIndex(
                name: "UX_ChangeRequest_OnePendingPerEntity",
                schema: "dbo",
                table: "ChangeRequest",
                columns: new[] { "EntityType", "EntityId" },
                unique: true,
                filter: "[Status] = 'PENDING'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChangeRequest",
                schema: "dbo");
        }
    }
}
