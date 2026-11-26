# Security review: posting and ledger (B8-02) and concurrency (B8-03)

Each item in B8-02 is either covered by a named integration test or has a ticket. Tests marked "new" were added in
B8 and have not yet been run.

| Item | Covered by | Status |
|---|---|---|
| Mandate bypass: a withdrawal needs the account mandate | `TillAndCashTests.Cash_rules_tills_vault_and_third_parties`; `TransferTests.All_to_sign_needs_every_signer` | covered |
| Limit bypass: an amount above the teller limit is held, not posted | `TransferTests.Above_the_limit_the_amount_is_held_until_a_different_supervisor_approves` (includes a teller's own approval being refused) | covered |
| Double posting: a retry with the same `Idempotency-Key` does not post twice | `TransferTests.A_transfer_within_the_limit_posts_at_once_and_a_retry_does_not_post_twice` | covered |
| Approval by the requester | `TransferTests.A_supervisor_cannot_approve_their_own_request`; `RoleGroupTests.The_requester_cannot_approve_their_own_change` | covered |
| Ledger tamper: posted rows cannot be changed or deleted with raw SQL | `PostingLedgerTests.Posted_rows_cannot_be_updated_or_deleted_even_with_raw_sql` (`PostedTransaction`, `LedgerEntry`); `LedgerTamperTests.Account_ledger_lines_cannot_be_updated_or_deleted_with_raw_sql` (`AccountLedgerEntry`, new) | covered |
| Ledger tamper: digest verification after a full end of day | Ticket SEC-B8-02-1 | open |

## Ticket SEC-B8-02-1: digest verification after end of day

Decide how the ledger is verified end to end after a full end-of-day run.

- Option A: SQL Server database ledger digests (`sys.sp_verify_database_ledger`). This needs a database created with
  `LEDGER = ON` and digest storage. The append-only tables exist already, but the database may not be ledger-enabled.
- Option B: an application check after end of day that recomputes each branch's ledger totals, compares them with
  `LedgerAccountDailyBalance`, and checks every account's `BalanceAfter` chain against its `ClearBalance`.

Decide before go-live, and record the choice as an ADR.

## Concurrency (B8-03)

- `ConcurrencySoakTests.Parallel_transfers_on_few_accounts_keep_every_balance_and_the_ledger_consistent` (new): 60
  parallel transfers across three accounts, a fixed seed, and checks that no money is lost, every statement reconciles
  to its balance, and the branch trial balance is zero.
- `TransferTests.Parallel_transfers_both_ways_never_lose_an_update` (existing): parallel transfers in both directions.

Not yet done in B8-03: the end-of-day checks run after the soak, and the soak at production volume belongs to B8-01.
