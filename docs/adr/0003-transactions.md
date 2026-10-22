# ADR 0003: Transactions (Phase B)

Status: accepted (2026-10-21). Owner answers to the plan's questions are in `docs/plans/phase_b_implementation_plan.md`
section 12.2; this records the design decisions behind the posting schema.

| # | Decision | Why |
|---|---|---|
| B-1 | A posting that needs approval waits in `PostingRequest` (mutable) and holds its amount in `UnsupervisedCredit` / `UnsupervisedDebit`. Ledger rows (`PostedTransaction`, `AccountLedgerEntry`, `LedgerEntry`) are written only when it posts, so they never change state. The DBML's `EntryStatusId` on the ledgers is dropped. | Ledger tables are append-only; a status column would need updates. |
| B-2 | `LedgerAccount.Balance` is set by end of day only ("as at the last close"). Live GL figures are the last `LedgerAccountDailyBalance` plus today's `LedgerEntry` rows. | A product has one control account; updating its balance on every posting would make every teller in the institution queue on one row. |
| B-3 | Every `LedgerEntry` carries the branch the line belongs to, and GL balances are per (ledger account, branch). `LedgerAccount.BranchId` stays as the maintaining branch. | One chart of accounts, and each branch still balances on its own. |
| B-4 | No `PendingInterBranchEntry` or `SavedTransferEntry`. A cross-branch posting (transfer, or cash at another branch) is one database transaction through each branch's `INTER_BRANCH` account. | One database serves every branch. |
| B-5 | Customer balances are maintained in `AccountBalance` under a row lock, never summed from history. Each customer line stores `BalanceAfter`, so statements read balances directly. | The old system's on-the-fly balance function was the main performance problem. |
| B-6 | Overdrafts stay out of Phase B: `OverdrawLimit` remains 0. A negative balance can arise only from a charge under the `CHARGE_FULL` shortfall policy. | Owner scope. |
| B-7 | The three ledgers are created in raw SQL (`PostingLedgerSql`) and excluded from EF migrations, like `UserActivityLog`. `PostingRequest.PostedTransactionId` has no EF relationship; its foreign key is created with the ledgers. | EF cannot create SQL Server ledger tables. |
| B-8 | `TransactionCode.Category` is a coded string (`TransactionCategories`) rather than a lookup table. | Categories drive code paths, not data entry; the DBML's `TransactionCategoryType` stays unused for now. |
