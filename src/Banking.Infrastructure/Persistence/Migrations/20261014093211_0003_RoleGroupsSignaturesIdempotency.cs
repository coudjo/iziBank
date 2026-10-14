using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Banking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class _0003_RoleGroupsSignaturesIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserPermission",
                schema: "dbo");

            migrationBuilder.DropIndex(
                name: "IX_SubModule_ModuleId",
                schema: "dbo",
                table: "SubModule");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_SubModule_Module_SubModule",
                schema: "dbo",
                table: "SubModule",
                columns: new[] { "ModuleId", "SubModuleId" });

            migrationBuilder.CreateTable(
                name: "IdempotencyKey",
                schema: "dbo",
                columns: table => new
                {
                    IdempotencyKeyId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    KeyValue = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    StaffUserId = table.Column<long>(type: "bigint", nullable: false),
                    Route = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ResponseStatus = table.Column<int>(type: "int", nullable: false),
                    ResponseBody = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ExpiresOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyKey", x => x.IdempotencyKeyId);
                    table.ForeignKey(
                        name: "FK_IdempotencyKey_StaffUser_StaffUserId",
                        column: x => x.StaffUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                });

            migrationBuilder.CreateTable(
                name: "PartySignature",
                schema: "dbo",
                columns: table => new
                {
                    PartySignatureId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PartyId = table.Column<long>(type: "bigint", nullable: false),
                    ImageValue = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    CapturedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CapturedByUserId = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_PartySignature", x => x.PartySignatureId);
                    table.ForeignKey(
                        name: "FK_PartySignature_Party_PartyId",
                        column: x => x.PartyId,
                        principalSchema: "dbo",
                        principalTable: "Party",
                        principalColumn: "PartyId");
                    table.ForeignKey(
                        name: "FK_PartySignature_StaffUser_CapturedByUserId",
                        column: x => x.CapturedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_PartySignature_StaffUser_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_PartySignature_StaffUser_ModifiedByUserId",
                        column: x => x.ModifiedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_PartySignature_StaffUser_SupervisedByUserId",
                        column: x => x.SupervisedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                });

            migrationBuilder.CreateTable(
                name: "RoleGroup",
                schema: "dbo",
                columns: table => new
                {
                    RoleGroupId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleGroupCode = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    RoleGroupName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IsSystemGroup = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
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
                    table.PrimaryKey("PK_RoleGroup", x => x.RoleGroupId);
                    table.ForeignKey(
                        name: "FK_RoleGroup_StaffUser_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_RoleGroup_StaffUser_ModifiedByUserId",
                        column: x => x.ModifiedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_RoleGroup_StaffUser_SupervisedByUserId",
                        column: x => x.SupervisedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                });

            migrationBuilder.CreateTable(
                name: "RoleGroupPermission",
                schema: "dbo",
                columns: table => new
                {
                    RoleGroupPermissionId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleGroupId = table.Column<int>(type: "int", nullable: false),
                    ModuleId = table.Column<int>(type: "int", nullable: false),
                    SubModuleId = table.Column<int>(type: "int", nullable: true),
                    AllowView = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    AllowAdd = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    AllowEdit = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    AllowDelete = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    AllowSupervise = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleGroupPermission", x => x.RoleGroupPermissionId);
                    table.CheckConstraint("CK_RoleGroupPermission_ViewImplied", "[AllowView] = 1 OR ([AllowAdd] = 0 AND [AllowEdit] = 0 AND [AllowDelete] = 0 AND [AllowSupervise] = 0)");
                    table.ForeignKey(
                        name: "FK_RoleGroupPermission_Module_ModuleId",
                        column: x => x.ModuleId,
                        principalSchema: "dbo",
                        principalTable: "Module",
                        principalColumn: "ModuleId");
                    table.ForeignKey(
                        name: "FK_RoleGroupPermission_RoleGroup_RoleGroupId",
                        column: x => x.RoleGroupId,
                        principalSchema: "dbo",
                        principalTable: "RoleGroup",
                        principalColumn: "RoleGroupId");
                    table.ForeignKey(
                        name: "FK_RoleGroupPermission_StaffUser_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_RoleGroupPermission_StaffUser_ModifiedByUserId",
                        column: x => x.ModifiedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_RoleGroupPermission_SubModule_ModuleId_SubModuleId",
                        columns: x => new { x.ModuleId, x.SubModuleId },
                        principalSchema: "dbo",
                        principalTable: "SubModule",
                        principalColumns: new[] { "ModuleId", "SubModuleId" });
                });

            migrationBuilder.CreateTable(
                name: "StaffUserRoleGroup",
                schema: "dbo",
                columns: table => new
                {
                    StaffUserRoleGroupId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StaffUserId = table.Column<long>(type: "bigint", nullable: false),
                    RoleGroupId = table.Column<int>(type: "int", nullable: false),
                    BranchId = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_StaffUserRoleGroup", x => x.StaffUserRoleGroupId);
                    table.ForeignKey(
                        name: "FK_StaffUserRoleGroup_Branch_BranchId",
                        column: x => x.BranchId,
                        principalSchema: "dbo",
                        principalTable: "Branch",
                        principalColumn: "BranchId");
                    table.ForeignKey(
                        name: "FK_StaffUserRoleGroup_RoleGroup_RoleGroupId",
                        column: x => x.RoleGroupId,
                        principalSchema: "dbo",
                        principalTable: "RoleGroup",
                        principalColumn: "RoleGroupId");
                    table.ForeignKey(
                        name: "FK_StaffUserRoleGroup_StaffUser_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_StaffUserRoleGroup_StaffUser_ModifiedByUserId",
                        column: x => x.ModifiedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_StaffUserRoleGroup_StaffUser_StaffUserId",
                        column: x => x.StaffUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                    table.ForeignKey(
                        name: "FK_StaffUserRoleGroup_StaffUser_SupervisedByUserId",
                        column: x => x.SupervisedByUserId,
                        principalSchema: "dbo",
                        principalTable: "StaffUser",
                        principalColumn: "StaffUserId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyKey_ExpiresOn",
                schema: "dbo",
                table: "IdempotencyKey",
                column: "ExpiresOn");

            migrationBuilder.CreateIndex(
                name: "UX_IdempotencyKey_Staff_Key",
                schema: "dbo",
                table: "IdempotencyKey",
                columns: new[] { "StaffUserId", "KeyValue" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartySignature_CapturedByUserId",
                schema: "dbo",
                table: "PartySignature",
                column: "CapturedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PartySignature_CreatedByUserId",
                schema: "dbo",
                table: "PartySignature",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PartySignature_ModifiedByUserId",
                schema: "dbo",
                table: "PartySignature",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PartySignature_PartyId_SupervisedOn",
                schema: "dbo",
                table: "PartySignature",
                columns: new[] { "PartyId", "SupervisedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_PartySignature_SupervisedByUserId",
                schema: "dbo",
                table: "PartySignature",
                column: "SupervisedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RoleGroup_CreatedByUserId",
                schema: "dbo",
                table: "RoleGroup",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RoleGroup_ModifiedByUserId",
                schema: "dbo",
                table: "RoleGroup",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RoleGroup_RoleGroupCode",
                schema: "dbo",
                table: "RoleGroup",
                column: "RoleGroupCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoleGroup_SupervisedByUserId",
                schema: "dbo",
                table: "RoleGroup",
                column: "SupervisedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RoleGroupPermission_CreatedByUserId",
                schema: "dbo",
                table: "RoleGroupPermission",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RoleGroupPermission_ModifiedByUserId",
                schema: "dbo",
                table: "RoleGroupPermission",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RoleGroupPermission_ModuleId_SubModuleId",
                schema: "dbo",
                table: "RoleGroupPermission",
                columns: new[] { "ModuleId", "SubModuleId" });

            migrationBuilder.CreateIndex(
                name: "UX_RoleGroupPermission_Group_Module_SubModule",
                schema: "dbo",
                table: "RoleGroupPermission",
                columns: new[] { "RoleGroupId", "ModuleId", "SubModuleId" },
                unique: true,
                filter: "[SubModuleId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StaffUserRoleGroup_Branch_Group",
                schema: "dbo",
                table: "StaffUserRoleGroup",
                columns: new[] { "BranchId", "RoleGroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffUserRoleGroup_CreatedByUserId",
                schema: "dbo",
                table: "StaffUserRoleGroup",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffUserRoleGroup_ModifiedByUserId",
                schema: "dbo",
                table: "StaffUserRoleGroup",
                column: "ModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffUserRoleGroup_RoleGroupId",
                schema: "dbo",
                table: "StaffUserRoleGroup",
                column: "RoleGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffUserRoleGroup_SupervisedByUserId",
                schema: "dbo",
                table: "StaffUserRoleGroup",
                column: "SupervisedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_StaffUserRoleGroup_Staff_Group_Branch",
                schema: "dbo",
                table: "StaffUserRoleGroup",
                columns: new[] { "StaffUserId", "RoleGroupId", "BranchId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IdempotencyKey",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "PartySignature",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "RoleGroupPermission",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "StaffUserRoleGroup",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "RoleGroup",
                schema: "dbo");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_SubModule_Module_SubModule",
                schema: "dbo",
                table: "SubModule");

            migrationBuilder.CreateTable(
                name: "UserPermission",
                schema: "dbo",
                columns: table => new
                {
                    UserPermissionId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BranchId = table.Column<int>(type: "int", nullable: false),
                    ModuleId = table.Column<int>(type: "int", nullable: false),
                    StaffUserId = table.Column<long>(type: "bigint", nullable: false),
                    SubModuleId = table.Column<int>(type: "int", nullable: true),
                    AllowAdd = table.Column<bool>(type: "bit", nullable: true),
                    AllowDelete = table.Column<bool>(type: "bit", nullable: true),
                    AllowEdit = table.Column<bool>(type: "bit", nullable: true),
                    CanSupervise = table.Column<bool>(type: "bit", nullable: true),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreditLimit = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    CreditSupervisionLimit = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    DebitLimit = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    DebitSupervisionLimit = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
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
                name: "IX_SubModule_ModuleId",
                schema: "dbo",
                table: "SubModule",
                column: "ModuleId");

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
        }
    }
}
