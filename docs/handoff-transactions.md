# Hand-off to the transactions phase (A6-04)

What Phase A leaves in place, and the rules posting (deposits, withdrawals, transfers, charges, interest, end of
day) must honour. Everything here exists and is tested; the transactions phase builds on it and does not change
these rules without an ADR.

## 1. Who may transact

A posting is refused unless every check below passes, in this order (the first failure is the problem returned).

| Check | Where it lives today | Problem code |
| --- | --- | --- |
| The account exists | `Account` | `not-found` |
| The account belongs to the user's working branch | `AccountGuards.ProblemFor` | `record.other-branch` |
| The account is not closed (`ClosedOn` set means read-only for ever) | `AccountGuards.ProblemFor` | `account.closed` |
| The account is approved (`SupervisionRules.CheckUsable`) | `AccountGuards.ProblemFor(requireApproved: true)` | supervision pending |
| The branch has an open business day | `IWorkingDateProvider.GetAsync` (throws) | `business-day.not-open` |
| The account is not frozen (`AccountCondition.IsFreezed`): no debit and no credit | `AccountCondition` | new code, e.g. `account.frozen` |
| Debit: `DoNotAllowDebit` is false; credit: `DoNotAllowCredit` is false | `AccountCondition` | new codes |
| Debit: amount ≤ `DebitLimit` when set; credit: amount ≤ `CreditLimit` when set | `AccountCondition` | new codes |
| Debit: `AvailableBalance − amount ≥ Product.MinimumBalance` (the minimum is enforced here, not in the formula) | `AccountBalanceCalculator`, `Product` | new code, e.g. `account.insufficient-funds` |
| Dormant account (`IsDormant`): decide in the transactions plan whether a credit reactivates it (write `DormantAccountHistory`, as `AccountServicing.SetDormantAsync` does) or needs a supervisor | `Account.IsDormant` | open decision |

Reuse `AccountGuards.ProblemFor` rather than copying it; add the posting checks beside it.

Displayed status is derived, first match wins: pending supervision, closed, frozen, dormant, active
(`AccountStatusRules.Derive`). Posting must use the underlying flags, not the derived label, because an account can be
frozen and dormant together.

## 2. The parties behind the account

- An account's holders are `AccountHolder` rows with role PRIMARY, JOINT or SIGNATORY.
- Opening already required every holder to be approved and KYC verified, and every signer to have an effective
  specimen signature (`AccountOpening`, rules 3 and 3a). These can change after opening:
  - a party's KYC can expire or be re-opened (`KycService.EffectiveStatusesAsync` gives the current state);
  - a new signature can be pending while the old one stays effective (`PartySignatures.WithEffectiveSignatureAsync`).
- **Withdrawals and transfers out are authorised by signers.** Who signs comes from `AccountOpeningRules.Check`
  (`AccountShape.Signers`): the holder alone in SINGLE mode; primary, joints and signatories in EITHER_TO_SIGN or
  ALL_TO_SIGN; only signatories for a business or group. ALL_TO_SIGN means every signer's mandate is checked.
- The teller screen shows the effective specimen of each signer to compare against the slip. Showing it is a
  sensitive read (`SensitiveRead`, fails closed), like the party image endpoints.
- Decide in the transactions plan whether a debit is refused when a signer's KYC has lapsed since opening. Phase A
  only checks at opening.

## 3. Balances

`AccountBalance` is one row per account. The formulas in `AccountBalanceCalculator` are the system's definition and
the posting phase reuses them unchanged:

```
Available = ClearBalance − FreezedAmount − UnsupervisedDebit + OverdrawLimit
Total     = ClearBalance + UnsupervisedCredit − UnsupervisedDebit
```

- Call `AccountBalanceCalculator.Recompute(balance)` after every change to a source column; never write
  `AvailableBalance` or `TotalBalance` directly.
- **Supervised transactions:** a posting that needs approval (over a teller limit, or the account has
  `SuperviseAllTransactions`) adds to `UnsupervisedCredit` or `UnsupervisedDebit` first. Approval moves the amount
  into `ClearBalance`; rejection removes it. A pending debit already reduces Available, so the money cannot be
  spent twice while it waits.
- Set `LastCreditTransactionDate` / `LastDebitTransactionDate` on each posting; dormancy rules will read them.
- `OpeningBalance` is set once at opening (zero today) and never changes.
- `DoNotApplyTransactionCharges` on `AccountCondition` exempts the account from charges.

## 4. Holds and freezes

- `FreezeReleaseAccountBalance` is the history of holds, releases, freezes and unfreezes; `AccountCondition.IsFreezed`
  and `AccountBalance.FreezedAmount` are the current state (`AccountConditions.ApplyAsync`, `FreezeActionRules`).
- A hold reduces Available, not Clear. A posting never releases a hold by itself; a teller releases it explicitly.
- Closing requires zero Clear, no holds and no unsupervised items (`AccountCloseRules.Check`). Posting must keep these
  columns accurate so close keeps working.

## 5. Concurrency

Every writer to `AccountBalance` takes an update lock on the row inside its transaction:

```sql
SELECT [AccountId] FROM [AccountBalance] WITH (UPDLOCK, ROWLOCK) WHERE [AccountId] = @id
```

then reloads the row, applies the change, recomputes and saves. `RowVersion` still guards any writer that does not
lock, with up to three retries before `record.changed` (`AccountConditions.MaxAttempts`). A transfer locks both
accounts **in ascending `AccountId` order** to avoid deadlocks. Posting and the matching ledger entries are one
transaction.

## 6. The ledger side

- Each product points at a control ledger account (`Product.LedgerControlAccountId` → `LedgerAccount`). Customer
  balances for a product roll up into that control account.
- `LedgerAccount` carries a branch, a type, a currency and a `Balance`, and is supervised like other records. Only
  approved ledger accounts may be posted to.
- `LedgerAccount.Balance` exists but nothing writes it yet. The transactions phase adds the journal (an append-only
  ledger table, per CLAUDE.md) and updates `Balance` in the same transaction as the journal row, under the same lock
  pattern as `AccountBalance`.
- The account's currency is its product's currency (`Product.CurrencyId`). Cross-currency postings are out of scope
  until an ADR says otherwise.
- Corrections are compensating entries, never updates or deletes of posted rows.

## 7. Business day and end of day

- Every posting carries the branch's open working date from `IWorkingDateProvider`, not the server clock.
- `BusinessDayStatus` has OPEN, CLOSED and EOD_IN_PROGRESS. Phase A's close (`BusinessDays.CloseAsync`) only marks the
  day closed. End of day will:
  1. set EOD_IN_PROGRESS, so new postings in the branch are refused (`IWorkingDateProvider` returns only OPEN days);
  2. refuse to continue while unsupervised items exist for the branch, or carry them forward per the plan;
  3. write one `AccountBalanceDailySnapshot` per account in the branch (`SnapshotDate` = the working date,
     `ClosingBalance` = `ClearBalance`), which interest accrual and statements read;
  4. accrue interest from `ProductInterestRate` (tiers by `MinimumAmount`/`MaximumAmount`, effective dates);
  5. set CLOSED.
- The portal already says "End-of-day processing is not part of this phase" on close; change that text when EOD lands.

## 8. Audit

- Each posting writes a `UserActivityLog` row through `IActivityContext.Record` with an event code (add codes to
  `EventCodes`), and entity changes reach `DataChangeAuditLog` through `DataChangeAuditInterceptor`.
- Amounts are not personal data and may be logged; account numbers in activity rows follow the existing pattern
  (entity key, never in URLs or query strings).

## 9. Permissions

Add submodules under the existing module catalogue (`ModuleCatalog`) for postings, with `supervise` for approvals,
and grant them to the teller and supervisor role groups in `RoleGroupCatalog`. Four-eyes applies: the person who
posted cannot approve (`SupervisionRules.CheckApprove`).

## 10. Open questions for the transactions plan

1. Does a credit reactivate a dormant account, or does reactivation stay a separate supervised action?
2. Is a debit refused when a signer's KYC has lapsed since opening?
3. Teller cash limits: per role group, per staff member, or per branch?
4. End of day with unsupervised items: block, or carry forward?
5. Overdraw limits: who sets `OverdrawLimit`, and with which approval?
