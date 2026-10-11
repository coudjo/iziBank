using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Banking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class _0002_LedgerTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE [dbo].[UserActivityLog]
                (
                    [EventId] bigint NOT NULL IDENTITY(1, 1),
                    [SessionId] bigint NULL,
                    [BranchId] int NULL,
                    [EventTypeId] int NOT NULL,
                    [EntityType] varchar(50) NULL,
                    [EntityId] varchar(50) NULL,
                    [Outcome] varchar(10) NOT NULL,
                    [EventTimestamp] datetime2 NOT NULL,
                    [RequestDetailsJson] nvarchar(max) NULL,
                    CONSTRAINT [PK_UserActivityLog] PRIMARY KEY CLUSTERED ([EventId]),
                    CONSTRAINT [FK_UserActivityLog_UserSession_SessionId]
                        FOREIGN KEY ([SessionId]) REFERENCES [dbo].[UserSession] ([SessionId]),
                    CONSTRAINT [FK_UserActivityLog_Branch_BranchId]
                        FOREIGN KEY ([BranchId]) REFERENCES [dbo].[Branch] ([BranchId]),
                    CONSTRAINT [FK_UserActivityLog_EventType_EventTypeId]
                        FOREIGN KEY ([EventTypeId]) REFERENCES [dbo].[EventType] ([EventTypeId])
                )
                WITH (LEDGER = ON (APPEND_ONLY = ON));

                CREATE INDEX [IX_UserActivityLog_SessionId_EventTimestamp]
                    ON [dbo].[UserActivityLog] ([SessionId], [EventTimestamp]);
                CREATE INDEX [IX_UserActivityLog_EntityType_EntityId_EventTimestamp]
                    ON [dbo].[UserActivityLog] ([EntityType], [EntityId], [EventTimestamp]);
                CREATE INDEX [IX_UserActivityLog_EventTypeId_EntityId_EventTimestamp]
                    ON [dbo].[UserActivityLog] ([EventTypeId], [EntityId], [EventTimestamp]);

                CREATE TABLE [dbo].[DataChangeAuditLog]
                (
                    [AuditId] bigint NOT NULL IDENTITY(1, 1),
                    [StaffUserId] bigint NULL,
                    [BranchId] int NULL,
                    [TableName] varchar(100) NOT NULL,
                    [RecordId] varchar(50) NULL,
                    [ActionType] varchar(10) NOT NULL,
                    [OldValueJson] nvarchar(max) NULL,
                    [NewValueJson] nvarchar(max) NULL,
                    [LoggedOn] datetime2 NOT NULL,
                    CONSTRAINT [PK_DataChangeAuditLog] PRIMARY KEY CLUSTERED ([AuditId]),
                    CONSTRAINT [FK_DataChangeAuditLog_StaffUser_StaffUserId]
                        FOREIGN KEY ([StaffUserId]) REFERENCES [dbo].[StaffUser] ([StaffUserId]),
                    CONSTRAINT [FK_DataChangeAuditLog_Branch_BranchId]
                        FOREIGN KEY ([BranchId]) REFERENCES [dbo].[Branch] ([BranchId])
                )
                WITH (LEDGER = ON (APPEND_ONLY = ON));

                CREATE INDEX [IX_DataChangeAuditLog_TableName_RecordId_LoggedOn]
                    ON [dbo].[DataChangeAuditLog] ([TableName], [RecordId], [LoggedOn]);
                CREATE INDEX [IX_DataChangeAuditLog_StaffUserId_LoggedOn]
                    ON [dbo].[DataChangeAuditLog] ([StaffUserId], [LoggedOn]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TABLE IF EXISTS [dbo].[DataChangeAuditLog];
                DROP TABLE IF EXISTS [dbo].[UserActivityLog];
                """);
        }
    }
}
