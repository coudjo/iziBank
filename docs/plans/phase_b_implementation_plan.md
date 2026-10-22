# Phase B Implementation Plan
## Transactions: teller cash, transfers, reversals, journals, charges, interest and end of day

**Audience:** a coding agent, and the owner for review. Follow this document top to bottom. Every task has an ID and
acceptance criteria.
**Builds on:** Phase A as delivered, and `docs/handoff-transactions.md` (the rules posting must honour). Phase A
rules stay in force; where this plan changes one, it says so and an ADR records it.
**Owner decisions (2026-10-21):** scope is cash deposit and withdrawal, transfers (same branch and across branches),
reversals and manual journals, and end of day with interest and charges. Teller cash runs through a till per teller
and a vault per branch. Approval limits are set per role group. End of day is blocked until nothing is waiting for
approval.
**Status:** reviewed by the owner on 2026-10-21 (answers in section 12.2); Q6 still open with a default.

---

## 0. Working agreement

Unchanged from Phase A section 0. In addition:

1. **Money is never created or lost.** Every posting is balanced per branch (sum of debits = sum of credits in each
   branch it touches), checked in the domain before saving and again by the database (section 2.4), and once more by
   end of day (trial balance).
2. **Posted rows are never changed.** Corrections are reversals or journals. Ledger tables are SQL Server append-only
   ledger tables, like `UserActivityLog`.
3. **Every posting endpoint takes an `Idempotency-Key`.** A retry after a timeout returns the first result, never a
   second posting.
4. **Amounts are decimals, never floats,** in C#, SQL and TypeScript (the portal sends and shows strings or fixed
   2-dp numbers; section 5.6).

---

## 1. Scope

### 1.1 In scope

| Area | Work |
|---|---|
| Posting engine | One engine that every money movement goes through: checks, locks, customer entries, GL entries, balances, approval path |
| Teller cash | Till sessions, cash bought from and sold to the vault, cash deposit, cash withdrawal, till count and close |
| Transfers | Account to account, same branch and across branches |
| Corrections | Reversal of a posted transaction; manual journals (GL lines, and account lines for corrections) |
| Charges | Per-transaction charges and periodic (monthly) fees from product charge rules |
| Interest | Daily accrual on credit-interest products; periodic capitalization with withholding tax |
| End of day | Per branch: checks, charges and interest, dormancy, daily balance snapshots, trial balance, close |
| Inquiry | Account history and statement, GL account inquiry, trial balance, teller and branch cash reports |

### 1.2 Out of scope (do not create)

Loans, overdrafts (the `OverdrawLimit` column stays 0 and gets no endpoint), fixed deposits, cheques and clearing,
mobile money and external transfers, notifications (SMS or email), customer-facing channels, printed forms other than
the on-screen receipt and statement, foreign-currency postings (every posting is in the local currency), multi-day
backdated value dates.

### 1.3 Tables from the DBML that this plan replaces or drops

The DBML's "later phases" block sketched these tables before the Phase A design was settled. This plan amends them
(section 2) instead of creating them as drawn:

| DBML table | Decision |
|---|---|
| `AccountLedgerEntry`, `LedgerEntry` | Kept, amended: grouped under a transaction header; no `EntryStatusId` (a row exists only once posted, so it never changes state) |
| `PendingInterBranchEntry`, `SavedTransferEntry` | Dropped. One database serves all branches, so a cross-branch transfer posts in one database transaction through inter-branch accounts (section 6.6). Pending postings live in `PostingRequest`. |
| `EntryStatusType` | Dropped (see above) |
| `ProductGLSetting`, `ProductLedgerSettingDetail` | Replaced by `ProductLedgerSetting` keyed by a coded setting (section 2.2) |
| `SystemLedgerSetting` | Kept, amended: unique per (branch, setting code), not per setting code |
| `ChargeCode`, `ProductCharge`, `ProductChargeTier`, `TransactionCode`, `TransactionCodeChargeTier` | Kept, with the supervision footer on the rule tables |

---

## 2. Schema

Every table gets the Phase A audit footer (`CreatedOn/By`, `ModifiedOn/By`) unless marked append-only, and the
supervision columns where it says "supervised".

### 2.1 Postings

**`PostingRequest`** (mutable; the maker-checker record for every money movement)

| Column | Notes |
|---|---|
| `PostingRequestId` bigint PK | |
| `Reference` varchar(20) unique | Section 5.4. Printed on the receipt. |
| `BranchId` int | Branch where the request was made (the teller's working branch) |
| `WorkingDate` date | The branch's open business day at creation |
| `TransactionCodeId` int | What it is: `CASH_DEP`, `CASH_WDL`, `TRANSFER`, `REVERSAL`, `JOURNAL`, `TILL_BUY`, … (section 7) |
| `Amount` decimal(19,4) | Principal amount; for a journal, the total of the debit lines |
| `PayloadJson` nvarchar(max) | The validated request (accounts, lines, depositor details, narration). Shown to the approver field by field. |
| `Status` varchar(20) | `PENDING`, `POSTED`, `REJECTED`, `CANCELLED`, `EXPIRED` |
| `PostedTransactionId` bigint null | Set when posted |
| `RequestedByUserId`, `RequestedOn` | |
| `SupervisedByUserId`, `SupervisedOn`, `DecisionReason` | Approver; reason required on reject |
| `IdempotencyKeyId` bigint null | Links the Phase A idempotency row |
| `RowVersion` | |

Index: `(BranchId, Status, WorkingDate)` for the approvals inbox and the end-of-day check.

**`PostedTransaction`** (append-only ledger table; one row per posting, the journal header)

| Column | Notes |
|---|---|
| `PostedTransactionId` bigint PK | |
| `Reference` varchar(20) unique | Same as the request's |
| `PostingRequestId` bigint null | Null for system postings (end of day) |
| `BranchId` int | Originating branch |
| `EntryDate` date | Working date it was posted on |
| `TransactionCodeId` int | |
| `Narration` nvarchar(200) | |
| `ReversesTransactionId` bigint null | Set on a reversal; unique filtered index, so a transaction is reversed at most once |
| `OperatorStaffUserId`, `SupervisorStaffUserId` bigint null | Maker and checker |
| `PostedOn` datetime2 | UTC |

**`AccountLedgerEntry`** (append-only ledger table; the customer sub-ledger)

| Column | Notes |
|---|---|
| `AccountLedgerEntryId` bigint PK | |
| `PostedTransactionId` bigint | |
| `AccountId` bigint | |
| `BranchId` int | The account's branch |
| `EntryDate` date | |
| `CrDb` char(1) | `C` or `D` |
| `Amount` decimal(19,4) | Always positive (check constraint) |
| `BalanceAfter` decimal(19,4) | `ClearBalance` after this entry, written under the balance lock; statements read it directly |
| `TransactionCodeId` int | Copied from the header for statement queries |

Indexes: `(AccountId, AccountLedgerEntryId)` for history and statements; `(BranchId, EntryDate)`.

**`LedgerEntry`** (append-only ledger table; the general ledger)

| Column | Notes |
|---|---|
| `LedgerEntryId` bigint PK | |
| `PostedTransactionId` bigint | |
| `LedgerAccountId` bigint | |
| `BranchId` int | The branch the line belongs to (see 6.6). Every branch balances on its own. |
| `EntryDate` date | |
| `CrDb` char(1), `Amount` decimal(19,4) | Positive |

Indexes: `(LedgerAccountId, BranchId, EntryDate)`; `(BranchId, EntryDate)`.

**`LedgerAccountDailyBalance`** (written by end of day only)

`(LedgerAccountId, BranchId, BalanceDate)` unique, `ClosingBalance` decimal(19,4). `LedgerAccount.Balance` becomes
"institution total as at the last end of day" and is set by end of day only (ADR B-2).

### 2.2 Settings

**`SystemLedgerSetting`** (supervised): `(BranchId, SettingCode)` unique → `LedgerAccountId`. Codes: `VAULT`,
`INTER_BRANCH`, `CASH_SHORT`, `CASH_OVER`, `WHT_PAYABLE`, `SUSPENSE`. A branch cannot post cash until `VAULT`,
`CASH_SHORT` and `CASH_OVER` are set; cross-branch postings need `INTER_BRANCH` in both branches.

**`ProductLedgerSetting`** (supervised): `(ProductId, SettingCode)` unique → `LedgerAccountId`. Codes:
`INTEREST_EXPENSE`, `INTEREST_PAYABLE`, `CHARGE_INCOME`. The control account stays on `Product.LedgerControlAccountId`.

**`Product`** gains `InterestCapitalizationFrequencyId` (lookup: `MONTHLY`, `QUARTERLY`, `HALF_YEARLY`, `YEARLY`),
`InterestDayCountId` (lookup: `ACT_365`, `ACT_360`; default `ACT_365`), `WithholdingTaxApplies` bool. Changes to an
approved product follow the Phase A `PRODUCT_UPDATE` change request.

**`RoleGroupTransactionLimit`**: `(RoleGroupId, LimitCode)` unique, `MaxAmount` decimal(19,4). Codes:
`CASH_DEPOSIT`, `CASH_WITHDRAWAL`, `TRANSFER`. No row means no authority to post without approval. A staff member's
limit is the largest across their approved role groups in the working branch (union, like permissions). Changes are
role-group changes, so they need supervision (Phase A A2-09 `ROLE_GROUP_UPDATE`).

**`StaffUser.TellerGLAccountId`** (exists): the till's ledger account. Required to open a till.

### 2.3 Teller cash

**`TillSession`**: `TillSessionId`, `StaffUserId`, `BranchId`, `WorkingDate`, `TellerLedgerAccountId`, `Status`
(`OPEN`, `CLOSING`, `CLOSED`), `OpenedOn`, `ClosedOn`, `CountedAmount` decimal null, `ExpectedAmount` decimal null,
`DifferencePostingRequestId` null. Unique filtered index: one `OPEN` or `CLOSING` session per staff member.

The till's balance is not stored: it is the till ledger account's entries for the session (section 6.4).

### 2.4 Charges and interest

`ChargeCode`, `ProductCharge` (supervised), `ProductChargeTier` as in the DBML, with lookups `ChargeCategoryType`
(`TRANSACTION`, `PERIODIC`), `ChargeConditionType` (`PER_CASH_WITHDRAWAL`, `PER_TRANSFER_OUT`, `MONTHLY_MAINTENANCE`),
`ChargeBasisType` (`FLAT`, `PERCENT`), `ChargeShortfallPolicyType` (`COLLECT_AVAILABLE`, `CHARGE_FULL`, `DEFER`).
`ProductCharge.ChargeConditionId` says when it applies; `FreeTransactions` per calendar month;
`ShortfallPolicyId` null means the institution setting.

**`AccountChargeArrear`**: `AccountChargeArrearId`, `AccountId`, `ProductChargeId`, `RaisedTransactionId`,
`RaisedOn` (working date), `Amount`, `OutstandingAmount`, `RowVersion`. Updated only under the account's balance lock;
each collection is its own posting, so the ledger keeps the full history.

**`AccountInterestAccrual`** (append-only): `(AccountId, AccrualDate)` unique, `Balance`, `Rate`, `Amount`
decimal(19,6). **`AccountInterestCapitalization`** (append-only): `AccountId`, `FromDate`, `ToDate`, `Amount`,
`TaxAmount`, `PostedTransactionId`. Accruals in a capitalized period are found by date range, so accrual rows never
change. (Alternative considered: one mutable "accrued so far" column on `AccountBalance`; rejected because daily rows
are the audit trail an examiner asks for.)

### 2.5 End of day

**`EndOfDayRun`**: `EndOfDayRunId`, `BranchId`, `WorkingDate` (unique together), `Status` (`RUNNING`, `FAILED`,
`COMPLETED`), `StartedByUserId`, `StartedOn`, `CompletedOn`. **`EndOfDayStep`**: run, `StepCode`, `Status`, `Started`,
`Completed`, `RowsAffected`, `Message`. Steps are idempotent, so a failed run resumes at the failed step.

### 2.6 Database constraints

- Check constraints: `Amount > 0`; `CrDb IN ('C','D')`.
- Ledger tables: `LEDGER = ON (APPEND_ONLY = ON)`, created in raw SQL in the migration, like Phase A.
- Partitioning `AccountLedgerEntry` and `LedgerEntry` by month is **not** in this phase. Task B8-01 measures at volume
  and records whether it is needed; check SQL Server's ledger-table restrictions before adopting it.

---

## 3. Permissions

New module `TXN` and sub-modules. Seeded into the catalog and the role groups below.

| Module / sub-module | View | Add | Supervise | Seeded to |
|---|---|---|---|---|
| `TXN/CASH` (deposit, withdrawal) | history | post | approve over limit | Teller: V A; Supervisor: V A S |
| `TXN/TRANSFER` | | post | approve | Teller: V A; Supervisor: V A S |
| `TXN/TILL` (own till) | own till | open, buy, sell, close | approve count differences | Teller: V A; Supervisor: V A S |
| `TXN/VAULT` | vault | | approve till buys and sells (custodian) | Supervisor: V S |
| `TXN/REVERSAL` | | request | approve | Supervisor: V A S |
| `LED/JOURNAL` | journals | create | approve | Supervisor: V A S; new role group `ACCOUNTANT`: V A |
| `LED/SETTINGS` (system and product ledger settings) | | edit | approve | Administrator: V E; Accountant: V E S |
| `LED/REPORTS` (GL inquiry, trial balance) | | | | Accountant, Supervisor, Auditor: V |
| `ACC/CHARGES` (charge rules) | | edit | approve | Administrator: V E; Accountant: V E S |
| `REF/EOD` (end of day) | status | run | | Supervisor: V A |

The approver can never be the requester (four-eyes, Phase A R2). Self-approval stays a Development-only switch.

---

## 4. Configuration

| Key | Default | Notes |
|---|---|---|
| `Transactions:CashTransactionReportThreshold` | none (owner to set) | At or above it, a cash deposit or withdrawal requires the depositor's or recipient's ID type and number, and is flagged for the large cash report (Q3) |
| `Transactions:PendingExpiryAtEndOfDay` | `false` | End of day is blocked while anything is pending (owner decision). Setting it later lets EOD expire them instead. |
| `Interest:WithholdingTaxRate` | `0` (owner to set) | Percent of capitalized interest withheld when the product says so (Q2) |
| `Dormancy:InactiveMonths` | `12` | Months without a customer-initiated transaction before end of day marks the account dormant (Q1) |
| `Charges:MonthEndOnLastWorkingDay` | `true` | Periodic fees and capitalization run at the first end of day on or after the period end |
| `Transactions:ReversalDays` | `30` | How far back a posted transaction can be reversed |
| `Interest:Rounding` | `HALF_AWAY_FROM_ZERO` | Or `HALF_EVEN`, `DOWN` (Q6) |
| `Charges:ShortfallPolicy` | `DEFER` | `COLLECT_AVAILABLE`, `CHARGE_FULL` or `DEFER`; a product charge can override it (6.8) |

---

## 5. Cross-cutting rules

### 5.1 One engine

Every movement is a `PostingPlan`: header + customer lines + GL lines, built by a domain service per transaction type,
then handed to `PostingEngine.PostAsync`. Nothing else writes ledger rows or balances.

### 5.2 Order of operations (one database transaction)

1. Resolve the branch's open working date (`IWorkingDateProvider`); refuse if none (`business-day.not-open`).
2. Lock every `AccountBalance` row involved, in ascending `AccountId` (UPDLOCK, ROWLOCK) — handoff section 5.
3. Reload accounts, conditions and balances; run the checks in 5.3.
4. Build lines; check that each branch balances; compute charges (6.8).
5. If the requester's limit covers it and no account needs supervision: write `PostedTransaction`, entries, update
   `ClearBalance` and `BalanceAfter`, `Recompute`, set `LastCredit/DebitTransactionDate`; request `POSTED`.
   Otherwise: write the request `PENDING`, add the amounts to `UnsupervisedCredit` / `UnsupervisedDebit`, `Recompute`.
6. Write the activity row and commit.

Approval repeats steps 1 to 5 with the request's payload: the pending amounts are removed from the unsupervised
columns and the posting is made. The checks run again at approval, because the account may have changed while the
request waited. Rejection and cancellation only remove the unsupervised amounts.

### 5.3 Checks (first failure is returned)

Handoff section 1 in full (branch, closed, approved, business day, frozen, `DoNotAllowCredit/Debit`,
`CreditLimit/DebitLimit`, available balance and product minimum), plus:

| Check | Code |
|---|---|
| A debit from a dormant account | `account.dormant` (credits are allowed and do not reactivate; reactivation stays the Phase A supervised action) |
| A debit when a required signer's effective KYC is not `VERIFIED` (Q8) | `account.signer-kyc-lapsed` |
| A debit without the signers' confirmation (6.3) | `transaction.mandate-not-confirmed` |
| The requester's till is not open (cash) | `till.not-open` |
| The till would go below zero (cash withdrawal, till sale) | `till.insufficient-cash` |
| Amount has more than 2 decimal places, or is not positive | `validation` |
| Branch ledger settings missing | `ledger.setting-missing` |
| Customer account and GL control account currencies differ | `transaction.currency-mismatch` |

The Phase A same-branch rule (`record.other-branch`) does not apply to postings: a teller may post cash to or from an
account of any branch, and a transfer may credit an account of any branch (Q4). Both branches' business days must be
open; the entry date is the teller's working date. Servicing changes (holds, freezes, close) stay branch-scoped.

New problem codes go into `docs/api/problem-types.md` and the portal error catalog (its build check fails otherwise).

### 5.4 References

`Reference` = branch code + `YYMMDD` of the working date + a 6-digit value of the global `TransactionReferenceSequence`,
for example `001-261021-000123`. Unique; printed on receipts and statements; searchable.

### 5.5 Approvals inbox

Posting requests join the Phase A approvals inbox as kinds `cash`, `transfer`, `reversal`, `journal`, `till`
(count differences and vault movements). The approver sees the payload field by field, the account's current available
balance and status, and for withdrawals the signers' specimen signatures (sensitive read, logged).

### 5.6 Money in the API

Amounts are JSON numbers with at most 2 decimal places in requests (more is `validation`), and returned with 2 decimal
places (4 for accrual figures). The portal never does floating-point arithmetic on money; totals it shows come from
the API.

### 5.7 Audit

Each request, approval, rejection and posting writes a `UserActivityLog` event (new `EventCodes`: `POSTING_REQUEST`,
`POSTING_APPROVE`, `POSTING_REJECT`, `POSTING_CANCEL`, `TILL_OPEN`, `TILL_CLOSE`, `EOD_RUN`). `PostingRequest` and
settings tables are audited by `DataChangeAuditInterceptor`; ledger tables are their own audit.

---

## 6. Domain rules

### 6.1 Cash deposit

Dr till ledger account (teller's branch) / Cr product control account (account's branch), customer entry Cr. The
account may belong to any branch (Q4): when it is not the teller's, the lines go through `INTER_BRANCH` as in 6.6
(teller's branch Dr till / Cr inter-branch; account's branch Dr inter-branch / Cr control). Depositor: "account holder" or "someone else" with name and mobile; at or above the cash report
threshold, also ID type and number (stored in the payload, masked in lists). Over the role-group limit → pending.

### 6.2 Cash withdrawal

Dr control / Cr till, customer entry Dr, through `INTER_BRANCH` when the account is in another branch (Q4). Checks
in 5.3. Charges per product (6.8) are posted in the same transaction
and count against the available balance.

### 6.3 Mandate and signatures

The teller sees each signer's effective specimen (sensitive read) and confirms which signers authorised the
withdrawal or transfer. The API checks the confirmation against the operating mode: `SINGLE` or `EITHER_TO_SIGN` needs
one signer; `ALL_TO_SIGN` needs every signer (handoff 2). The confirmed signer ids are stored in the payload.

### 6.4 Tills and vault

- **Open:** a teller with `TellerGLAccountId` opens one session per working date; the till ledger account balance at
  that moment must be zero (cash went back to the vault at the last close).
- **Buy from vault:** Dr till / Cr vault. **Sell to vault:** Dr vault / Cr till. Both always need a `TXN/VAULT`
  supervisor (two people handle the cash).
- **Till balance** = sum of the till account's `LedgerEntry` rows for the session; shown on the teller's screen.
- **Close:** the teller enters the counted cash. Difference against the expected balance posts to `CASH_SHORT`
  (shortage: Dr cash short / Cr till) or `CASH_OVER` (overage), always approved by a supervisor (Q9). Then the
  remaining cash is sold to the vault and the session closes with a zero balance.
- The vault ledger account must never go below zero.

### 6.5 Transfers, same branch

Dr control (source product) / Cr control (destination product); customer entries Dr source, Cr destination. Same
product nets to zero on the GL, but both lines are still written so the GL inquiry shows the movement. Mandate as
6.3. Charges as 6.8.

### 6.6 Transfers across branches

The source branch writes Dr control (source branch) / Cr `INTER_BRANCH` (source branch); the destination writes
Dr `INTER_BRANCH` (destination branch) / Cr control (destination branch). One database transaction, one
`PostedTransaction`. The two inter-branch balances mirror each other; end of day checks the institution-wide
inter-branch total is zero. The teller works in the source account's branch; the destination may be any branch.

**Ledger accounts and branches (ADR B-3):** Phase A gives each `LedgerAccount` a `BranchId`, and products point at one
control account. In Phase B every `LedgerEntry` carries the branch the line belongs to, and GL balances are reported
per (ledger account, branch). `LedgerAccount.BranchId` stays as the branch that maintains the account. This keeps one
chart of accounts and gives every branch its own balanced books.

### 6.7 Reversals and journals

- **Reversal:** any posted transaction from the last `Transactions:ReversalDays` (default 30) can be reversed once,
  by a request that always needs approval. The reversal mirrors every line (C ↔ D) with today's date and runs the
  normal checks: reversing a deposit is refused when the account's available balance no longer covers it, and nothing
  is reversed on a closed account. A reversal cannot be reversed; post a new transaction instead.
- **Journal:** balanced lines on GL accounts, optionally with customer account lines (for corrections), always
  approved. Customer lines run the normal account checks. Journals cannot touch till or vault accounts (those move
  only through 6.4).

### 6.8 Charges

- **Per transaction:** when a product has a `TRANSACTION` charge for the condition (cash withdrawal, transfer out),
  after the month's free transactions are used, the charge is computed from the tiers (flat or percent, min and max)
  and posted in the same `PostedTransaction` (Dr customer / Cr `CHARGE_INCOME`). The quote shows it before the
  teller confirms. `DoNotApplyTransactionCharges` exempts the account.
- **Periodic:** monthly maintenance fees post at month end (4, `Charges:MonthEndOnLastWorkingDay`).
- **When the balance is short (Q7, owner decision):** a shortfall policy decides, set institution-wide in
  `Charges:ShortfallPolicy` and overridable per product charge (`ProductCharge.ShortfallPolicyId`):

  | Policy | Effect |
  | --- | --- |
  | `COLLECT_AVAILABLE` | Charge what the available balance allows; the rest is written off and listed in the uncollected-fees report |
  | `CHARGE_FULL` | Charge the full amount; the clear balance may go below zero. A charge is the only thing that can make it negative. Debits are refused while the available balance is below zero; the next credits cover it first. |
  | `DEFER` | Charge what is available now and record the rest as an arrear (`AccountChargeArrear`); later credits collect arrears oldest first, in the same posting as the credit |

  For a transaction charge, the principal must always fit the available balance; only the charge follows the policy.
  Interest does not accrue on a negative balance. An account with a negative balance or an open arrear cannot be
  closed (the close check gains these items).

### 6.9 Interest

- **Accrual (each end of day):** for each account of a product with a credit rate, for each calendar day since the
  last accrual up to the working date: balance = that day's closing clear balance (for non-working days, the last
  closing before it); rate = the product rate band for that balance on that day (`ProductInterestRate`); amount =
  balance × rate / 100 / days in year (day count), kept to 6 decimals. GL: Dr `INTEREST_EXPENSE` / Cr
  `INTEREST_PAYABLE` for the branch total, rounded to 2 dp, with the rounding difference carried to the next day.
- **Capitalization:** at the first end of day on or after the period end, the accruals since the last capitalization
  are summed and rounded half away from zero to 2 dp (Q6). Dr `INTEREST_PAYABLE` / Cr customer; withholding tax (when
  the product says so) Dr customer / Cr `WHT_PAYABLE`.
- Frozen and dormant accounts still accrue and capitalize; closed accounts do not. Closing an account with uncapitalized
  interest capitalizes it first (the close check gains an item).

### 6.10 End of day (per branch)

`POST /branches/{id}/business-days/{date}/end-of-day` replaces the Phase A plain close, which is removed (the portal's
"Close day" becomes "Run end of day"). A background worker runs the steps; the portal polls the run.

| Step | What | Blocks the run when |
|---|---|---|
| `PRECHECK` | Nothing `PENDING` in the branch; every till session `CLOSED`; vault ≥ 0 | Any fails (owner decision: block until cleared) |
| `LOCK` | Day status → `EOD_IN_PROGRESS` (postings refused from here) | |
| `CHARGES` | Periodic fees on period end | |
| `INTEREST_ACCRUAL` | 6.9 | |
| `INTEREST_CAPITALIZATION` | 6.9 on period end | |
| `DORMANCY` | Mark dormant after `Dormancy:InactiveMonths` without a customer-initiated transaction | |
| `SNAPSHOTS` | One `AccountBalanceDailySnapshot` per branch account (set-based insert); `LedgerAccountDailyBalance` per (account, branch) | |
| `CHECKS` | Branch trial balance is zero; customer balances per product equal the control account's branch balance; inter-branch nets to zero across branches | Any is off: run stops `FAILED`, day stays `EOD_IN_PROGRESS`, supervisor sees the difference. The fix is a journal, which is allowed in `EOD_IN_PROGRESS` for this purpose only. |
| `CLOSE` | Update `LedgerAccount.Balance`, day → `CLOSED` | |

Steps are set-based SQL over the branch's accounts in batches, not row-by-row EF. Target: 300,000 accounts in under
10 minutes on the owner's hardware (B8-01).

### 6.11 Statements and history

`GET /accounts/{id}/entries` (paged, newest first) and `GET /accounts/{id}/statement?from&to` (opening balance from
the last entry before `from`, entries, closing balance). Both are sensitive reads like the account record. The
portal's "No transactions yet" becomes the history tab.

---

## 7. Seed catalog

- `TransactionCode` (system codes, `IsSystemCode = 1`): `CASH_DEP`, `CASH_WDL`, `TRF`, `TRF_XB` (cross-branch),
  `REV`, `JNL`, `TILL_BUY`, `TILL_SELL`, `TILL_SHORT`, `TILL_OVER`, `CHG_TXN`, `CHG_MONTHLY`, `INT_ACCR`, `INT_CAP`,
  `WHT`.
- Lookups in 2.2 and 2.4. Module `TXN` and the sub-modules in section 3, with the role-group grants.
- Role group `ACCOUNTANT` (system group).
- `ChargeCode` rows are not seeded; the institution sets its own.
- Demo data (Phase A A6-05) gains: branch ledger settings, a vault and till ledger accounts, teller limits on the
  `TELLER` group, a product charge, and a handful of posted transactions.

---

## 8. API surface (under `/api/v1`)

| Group | Endpoints |
|---|---|
| Settings | `GET/PUT /branches/{id}/ledger-settings`, `GET/PUT /products/{id}/ledger-settings`, `GET/PUT /role-groups/{id}/transaction-limits` (change request), `GET/POST/PUT /charge-codes`, `GET/POST/PUT /products/{id}/charges` (+ tiers, supervise, reject) |
| Tills | `GET /tills/mine`, `POST /tills/open`, `POST /tills/{id}/buy`, `/sell`, `/close` (with count), `GET /tills/{id}/entries`, `GET /branches/{id}/tills` (supervisor view) |
| Postings | `POST /transactions/cash-deposits`, `/cash-withdrawals`, `/transfers`, `/reversals`, `/journals` (all with `Idempotency-Key`); `GET /transactions/{reference}`; `GET /transaction-requests` (filters: status, kind, branch, date); `POST /transaction-requests/{id}/approve`, `/reject`, `/cancel` (requester only) |
| Quote | `POST /transactions/quote` — returns the charge and the resulting available balance before the teller confirms; posts nothing |
| Accounts | `GET /accounts/{id}/entries`, `GET /accounts/{id}/statement`, `GET /accounts/{id}/signers` (mandate and specimens for the withdrawal screen) |
| Ledger | `GET /ledger-accounts/{id}/entries?branchId&from&to`, `GET /reports/trial-balance?branchId&date`, `GET /reports/teller-cash?branchId&date`, `GET /reports/uncollected-fees?branchId&date` |
| End of day | `POST /branches/{id}/business-days/{date}/end-of-day`, `GET /end-of-day-runs/{id}`, `POST /end-of-day-runs/{id}/resume` |

Search filters and account numbers go in request bodies or paths, never query strings with personal data (Phase A
rule). Report query strings carry only ids and dates.

---

## 9. Work breakdown

### B0 Schema and settings

| ID | Task | Acceptance |
|---|---|---|
| B0-01 | Ledger schema: `PostingRequest`, `PostedTransaction`, `AccountLedgerEntry`, `LedgerEntry`, `LedgerAccountDailyBalance`, sequence, constraints, ledger tables; DBML and changelog | Migration applies; an update or delete on a ledger table fails in a test |
| B0-02 | Settings schema and API: system and product ledger settings, role-group limits (change request), `TXN` module and grants, `ACCOUNTANT` group (transaction codes moved to B0-01; product interest settings moved to B5-03, where they are used) | Settings supervised; four-eyes enforced; limits resolve as the max over approved groups |
| B0-03 | Charges schema and admin API | Rules supervised; tiers validated (ordered, min ≤ max) |

### B1 Posting engine

| ID | Task | Acceptance |
|---|---|---|
| B1-01 | `PostingEngine` and `PostingPlan`: checks (5.3), lock order, entries, `BalanceAfter`, GL lines, per-branch balance | Unit tests per check; property test: any accepted plan balances per branch; parallel postings on one account never lose an update |
| B1-02 | Approval path: pending amounts, approve (re-check), reject, cancel; approvals inbox kinds | Pending debit reduces available; rejection restores it; requester cannot approve |
| B1-03 | References, idempotency on every posting endpoint, activity events, problem codes | Retry with the same key returns the same reference and posts once |

### B2 Teller cash

| ID | Task | Acceptance |
|---|---|---|
| B2-01 | Till sessions; buy and sell with vault approval; till balance | One open session per teller; vault never negative |
| B2-02 | Cash deposit, depositor details, threshold capture, `quote` | Over-limit deposit waits; under-limit posts; receipt data returned |
| B2-03 | Cash withdrawal with mandate, dormant, KYC lapse, minimum balance, charges | Each refusal has its own problem code; `ALL_TO_SIGN` needs every signer |
| B2-04 | Till close: count, difference posting with approval, sale to vault | Session cannot close with a non-zero till or an unapproved difference |

### B3 Transfers

| ID | Task | Acceptance |
|---|---|---|
| B3-01 | Same-branch transfer | Both balances and both control lines correct; charges applied |
| B3-02 | Cross-branch transfer through inter-branch accounts | Each branch balances; inter-branch nets to zero |

### B4 Corrections

| ID | Task | Acceptance |
|---|---|---|
| B4-01 | Reversal | Reversed once only; mirror lines; normal checks apply |
| B4-02 | Manual journals | Must balance per branch; till and vault accounts refused; always approved |

### B5 Charges and interest

| ID | Task | Acceptance |
|---|---|---|
| B5-01 | Per-transaction charges and free transactions | Charge appears in the quote and in the same posting; exemption honoured |
| B5-02 | Periodic fees and the shortfall policies, with arrears collected from later credits | Each policy behaves as 6.8 for both transaction and periodic charges; arrears collected oldest first; negative balance only through a charge |
| B5-03 | Interest accrual, with the product interest settings of 2.2 (capitalization frequency, day count, withholding tax flag) | Weekend and holiday days accrue on the last closing balance; bands by balance; rounding carried |
| B5-04 | Capitalization and withholding tax | Sum of accruals matches the posting within rounding; close capitalizes first |

### B6 End of day

| ID | Task | Acceptance |
|---|---|---|
| B6-01 | Run orchestration, steps, resume, polling API | A failed step resumes without repeating finished steps |
| B6-02 | Snapshots and GL daily balances (set-based) | One snapshot per account per day; `LedgerAccount.Balance` updated |
| B6-03 | Checks and dormancy | An injected imbalance stops the run with the difference shown; a journal fixes it and resume completes |

### B7 Inquiry and reports

| ID | Task | Acceptance |
|---|---|---|
| B7-01 | Account history and statement | Opening + entries = closing; `BalanceAfter` matches |
| B7-02 | GL account inquiry, trial balance | Trial balance sums to zero per branch |
| B7-03 | Teller cash and uncollected fees reports | Teller report matches till sessions |

### B8 Hardening

| ID | Task | Acceptance |
|---|---|---|
| B8-01 | Performance: extend the A6-01 generator with 6 months of postings (about 2,000 a day per branch); measure posting p95 < 300 ms, statement p95 < 300 ms, end of day < 10 min for 300,000 accounts; decide on partitioning | Results in `docs/performance.md` |
| B8-02 | Security review additions: mandate bypass, limit bypass, double posting, approval by requester, ledger tamper (digest verification after a full EOD) | Each with a test or a ticket |
| B8-03 | Concurrency soak: many parallel postings on few accounts, then the checks in B6-03 | Trial balance zero; no lost update |

### Frontend (portal) tasks

A Phase B UX design (`phase_b_ux_design.md`) follows owner review of this plan; screens in short:

| ID | Screen |
|---|---|
| BF1 | Teller home: till status and balance, today's totals, open/close till, buy and sell cash |
| BF2 | Cash deposit and cash withdrawal (account lookup, mandate and specimens, quote with charge, confirm, on-screen receipt with print) |
| BF3 | Transfer (same and cross branch) |
| BF4 | Approvals inbox: posting kinds with field-by-field view, signatures, current balance |
| BF5 | Account history and statement tabs (replace "No transactions yet"); balances live |
| BF6 | Reversal (from a transaction) and journal entry (balanced lines, running difference) |
| BF7 | Settings: branch and product ledger settings, role-group limits, charge rules |
| BF8 | End of day: checklist (pending items, open tills), run progress, failure with the difference and the journal shortcut; reports (trial balance, GL inquiry, teller cash, uncollected fees) |

---

## 10. Testing

- Domain unit tests for every check, charge tier, accrual day and rounding case.
- Integration tests (Testcontainers) per endpoint, including denial, four-eyes, idempotent retry and concurrency.
- An invariant suite run after every integration test class that posts: per-branch trial balance zero; sub-ledger
  equals control per product and branch; every `AccountBalance.ClearBalance` equals the last `BalanceAfter`.
- Portal e2e at 3 viewports with axe, against MSW mocks first, then the real API.

---

## 11. Definition of done (Phase B)

All tasks' acceptance passes; the invariant suite is green; a full simulated week (postings, a weekend, a month end
with capitalization and fees, a reversal, a cross-branch transfer, a failed and resumed end of day) runs end to end in
the demo data; performance targets met or recorded with a decision; owner build and tests green.

---

## 12. Decisions and open questions

### 12.1 Decisions (ADR to be written as `docs/adr/0003-transactions.md`)

| # | Decision |
|---|---|
| B-1 | Pending postings live in `PostingRequest`; ledger rows exist only once posted, so ledger rows never change state |
| B-2 | GL balances are not updated on every posting (one control account per product would serialize every teller in the institution). Live GL figures come from the last daily balance plus today's entries; `LedgerAccount.Balance` is set at end of day |
| B-3 | GL entries carry the branch; balances are per (ledger account, branch); each branch balances on its own |
| B-4 | No inter-branch pending tables: one database, one transaction, inter-branch accounts |
| B-5 | Customer balances are maintained (`AccountBalance`), never computed from history; `BalanceAfter` makes statements cheap |
| B-6 | Overdrafts stay out: `OverdrawLimit` remains 0 |

### 12.2 Owner answers (2026-10-21)

| # | Question | Answer |
|---|---|---|
| Q1 | Dormancy period, and credits into a dormant account | 12 months without a customer-initiated transaction; credits allowed, debits refused until the supervised reactivation |
| Q2 | Withholding tax on savings interest | Rate 0 until the owner sets it; the product flag decides who it applies to |
| Q3 | Large cash threshold for depositor or recipient ID | The owner sets the amount (`Transactions:CashTransactionReportThreshold`); until then, name and mobile for third-party deposits only |
| Q4 | Cash at a branch other than the account's | Allowed, through inter-branch accounts (6.1, 6.2) |
| Q5 | Receipts | On-screen receipt with browser print; no PDF, no receipt printer |
| Q7 | Charges when the balance is short | A setting: collect what is available, charge in full (balance may go negative), or defer and collect from later credits (6.8). Default `DEFER` |
| Q8 | Withdrawals while a signer's KYC has lapsed | Refused; deposits still allowed |
| Q9 | Till shortages | Posted to `CASH_SHORT` with supervisor approval; recovery from the teller happens outside the system |
| Q10 | Interest capitalization | Monthly by default, set per product |

### 12.3 Still open

| # | Question | Default until answered |
|---|---|---|
| Q6 | Rounding of interest and percent charges | Interest accrues daily to 6 decimals and is rounded only when credited; rounding is half away from zero to 2 dp (GHS 0.125 → 0.13). Kept configurable (`Interest:Rounding`: `HALF_AWAY_FROM_ZERO`, `HALF_EVEN`, `DOWN`) so it can change before go-live |
