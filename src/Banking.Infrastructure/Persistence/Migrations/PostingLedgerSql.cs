namespace Banking.Infrastructure.Persistence.Migrations;

/// <summary>
/// B0-01: the posting ledgers as SQL Server append-only ledger tables (plan B 2.1, 2.6). EF cannot create ledger tables,
/// so the migration that adds the posting schema runs <see cref="Create"/> after its generated statements, and
/// <see cref="Drop"/> before them on the way down. Column types match the EF configuration exactly.
/// </summary>
internal static class PostingLedgerSql
{
    public const string Create =
        """
        -- Same cache as the Phase A sequences (EF creates sequences without one).
        ALTER SEQUENCE [dbo].[TransactionReferenceSequence] CACHE 50;

        CREATE TABLE [dbo].[PostedTransaction]
        (
            [PostedTransactionId] bigint NOT NULL IDENTITY(1, 1),
            [Reference] varchar(30) NOT NULL,
            [PostingRequestId] bigint NULL,
            [BranchId] int NOT NULL,
            [EntryDate] date NOT NULL,
            [TransactionCodeId] int NOT NULL,
            [Narration] nvarchar(200) NULL,
            [ReversesTransactionId] bigint NULL,
            [OperatorStaffUserId] bigint NULL,
            [SupervisorStaffUserId] bigint NULL,
            [PostedOn] datetime2 NOT NULL,
            CONSTRAINT [PK_PostedTransaction] PRIMARY KEY CLUSTERED ([PostedTransactionId]),
            CONSTRAINT [FK_PostedTransaction_PostingRequest_PostingRequestId]
                FOREIGN KEY ([PostingRequestId]) REFERENCES [dbo].[PostingRequest] ([PostingRequestId]),
            CONSTRAINT [FK_PostedTransaction_Branch_BranchId]
                FOREIGN KEY ([BranchId]) REFERENCES [dbo].[Branch] ([BranchId]),
            CONSTRAINT [FK_PostedTransaction_TransactionCode_TransactionCodeId]
                FOREIGN KEY ([TransactionCodeId]) REFERENCES [dbo].[TransactionCode] ([TransactionCodeId]),
            CONSTRAINT [FK_PostedTransaction_PostedTransaction_ReversesTransactionId]
                FOREIGN KEY ([ReversesTransactionId]) REFERENCES [dbo].[PostedTransaction] ([PostedTransactionId]),
            CONSTRAINT [FK_PostedTransaction_StaffUser_OperatorStaffUserId]
                FOREIGN KEY ([OperatorStaffUserId]) REFERENCES [dbo].[StaffUser] ([StaffUserId]),
            CONSTRAINT [FK_PostedTransaction_StaffUser_SupervisorStaffUserId]
                FOREIGN KEY ([SupervisorStaffUserId]) REFERENCES [dbo].[StaffUser] ([StaffUserId])
        )
        WITH (LEDGER = ON (APPEND_ONLY = ON));

        CREATE UNIQUE INDEX [IX_PostedTransaction_Reference]
            ON [dbo].[PostedTransaction] ([Reference]);
        CREATE INDEX [IX_PostedTransaction_BranchId_EntryDate]
            ON [dbo].[PostedTransaction] ([BranchId], [EntryDate]);
        CREATE UNIQUE INDEX [UX_PostedTransaction_ReversesTransactionId]
            ON [dbo].[PostedTransaction] ([ReversesTransactionId])
            WHERE [ReversesTransactionId] IS NOT NULL;

        CREATE TABLE [dbo].[AccountLedgerEntry]
        (
            [AccountLedgerEntryId] bigint NOT NULL IDENTITY(1, 1),
            [PostedTransactionId] bigint NOT NULL,
            [AccountId] bigint NOT NULL,
            [BranchId] int NOT NULL,
            [EntryDate] date NOT NULL,
            [CrDb] char(1) NOT NULL,
            [Amount] decimal(19, 4) NOT NULL,
            [BalanceAfter] decimal(19, 4) NOT NULL,
            [TransactionCodeId] int NOT NULL,
            CONSTRAINT [PK_AccountLedgerEntry] PRIMARY KEY CLUSTERED ([AccountLedgerEntryId]),
            CONSTRAINT [CK_AccountLedgerEntry_Amount] CHECK ([Amount] > 0),
            CONSTRAINT [CK_AccountLedgerEntry_CrDb] CHECK ([CrDb] IN ('C', 'D')),
            CONSTRAINT [FK_AccountLedgerEntry_PostedTransaction_PostedTransactionId]
                FOREIGN KEY ([PostedTransactionId]) REFERENCES [dbo].[PostedTransaction] ([PostedTransactionId]),
            CONSTRAINT [FK_AccountLedgerEntry_Account_AccountId]
                FOREIGN KEY ([AccountId]) REFERENCES [dbo].[Account] ([AccountId]),
            CONSTRAINT [FK_AccountLedgerEntry_Branch_BranchId]
                FOREIGN KEY ([BranchId]) REFERENCES [dbo].[Branch] ([BranchId]),
            CONSTRAINT [FK_AccountLedgerEntry_TransactionCode_TransactionCodeId]
                FOREIGN KEY ([TransactionCodeId]) REFERENCES [dbo].[TransactionCode] ([TransactionCodeId])
        )
        WITH (LEDGER = ON (APPEND_ONLY = ON));

        CREATE INDEX [IX_AccountLedgerEntry_AccountId_EntryId]
            ON [dbo].[AccountLedgerEntry] ([AccountId], [AccountLedgerEntryId]);
        CREATE INDEX [IX_AccountLedgerEntry_BranchId_EntryDate]
            ON [dbo].[AccountLedgerEntry] ([BranchId], [EntryDate]);
        CREATE INDEX [IX_AccountLedgerEntry_PostedTransactionId]
            ON [dbo].[AccountLedgerEntry] ([PostedTransactionId]);

        CREATE TABLE [dbo].[LedgerEntry]
        (
            [LedgerEntryId] bigint NOT NULL IDENTITY(1, 1),
            [PostedTransactionId] bigint NOT NULL,
            [LedgerAccountId] bigint NOT NULL,
            [BranchId] int NOT NULL,
            [EntryDate] date NOT NULL,
            [CrDb] char(1) NOT NULL,
            [Amount] decimal(19, 4) NOT NULL,
            CONSTRAINT [PK_LedgerEntry] PRIMARY KEY CLUSTERED ([LedgerEntryId]),
            CONSTRAINT [CK_LedgerEntry_Amount] CHECK ([Amount] > 0),
            CONSTRAINT [CK_LedgerEntry_CrDb] CHECK ([CrDb] IN ('C', 'D')),
            CONSTRAINT [FK_LedgerEntry_PostedTransaction_PostedTransactionId]
                FOREIGN KEY ([PostedTransactionId]) REFERENCES [dbo].[PostedTransaction] ([PostedTransactionId]),
            CONSTRAINT [FK_LedgerEntry_LedgerAccount_LedgerAccountId]
                FOREIGN KEY ([LedgerAccountId]) REFERENCES [dbo].[LedgerAccount] ([LedgerAccountId]),
            CONSTRAINT [FK_LedgerEntry_Branch_BranchId]
                FOREIGN KEY ([BranchId]) REFERENCES [dbo].[Branch] ([BranchId])
        )
        WITH (LEDGER = ON (APPEND_ONLY = ON));

        CREATE INDEX [IX_LedgerEntry_Account_Branch_EntryDate]
            ON [dbo].[LedgerEntry] ([LedgerAccountId], [BranchId], [EntryDate]);
        CREATE INDEX [IX_LedgerEntry_BranchId_EntryDate]
            ON [dbo].[LedgerEntry] ([BranchId], [EntryDate]);
        CREATE INDEX [IX_LedgerEntry_PostedTransactionId]
            ON [dbo].[LedgerEntry] ([PostedTransactionId]);

        ALTER TABLE [dbo].[PostingRequest]
            ADD CONSTRAINT [FK_PostingRequest_PostedTransaction_PostedTransactionId]
            FOREIGN KEY ([PostedTransactionId]) REFERENCES [dbo].[PostedTransaction] ([PostedTransactionId]);
        """;

    public const string Drop =
        """
        ALTER TABLE [dbo].[PostingRequest] DROP CONSTRAINT IF EXISTS [FK_PostingRequest_PostedTransaction_PostedTransactionId];
        DROP TABLE IF EXISTS [dbo].[LedgerEntry];
        DROP TABLE IF EXISTS [dbo].[AccountLedgerEntry];
        DROP TABLE IF EXISTS [dbo].[PostedTransaction];
        """;
}
