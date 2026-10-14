# ADR 0001 — Phase A default decisions

Status: accepted  
Date: 2026-10-11  
Source: Phase A implementation plan, section 12.1

These defaults are in force unless the owner changes the cited section.

| # | Decision | Change by editing |
|---|---|---|
| D1 | Maker-checker applies to creation, and to controlled-field edits (R4). Rejecting a re-pended edit is not supported. | Plan 5.4 |
| D2 | Reads are institution-wide; writes to branch-owned data need the entity's branch to equal the effective branch. | Plan 5.5 |
| D3 | KYC verification and supervision are one action (owner decision, addendum R13). A KYC officer verifies and approves in the same step; the four-eyes rule still applies. A party must be KYC-verified and approved before it can open an account. | Addendum R13 |
| D4 | Products are institution-wide. `Product.BranchId` records the owning branch and does not restrict use. | Plan 6.8 |
| D5 | The `AccountBalance` formulas in plan 5.8 are the definition. Minimum balance is enforced at debit time. | Plan 5.8 |
| D6 | Group accounts and groups as `PRIMARY` holders are allowed. | Plan 6.9 |
| D7 | Fail closed on activity-log write failure for sensitive reads. | Plan 5.6 |
| D8 | An Individual can be an active member of only one group at a time. | Plan 6.4 |
| D9 | `EXPIRED` KYC status is computed, not stored, in this phase. | Plan 6.5 |
| D10 | Direct account opening is limited to `SAVINGS` and `CURRENT`. | Plan 4, 6.9 |

Related: [open-decisions.md](open-decisions.md).

## Amendments (owner decisions, addendum applied 2026-10-13)

| # | Decision |
|---|---|
| R1-R4, R10, R11 | Permissions come from role groups, assigned per branch. `UserPermission` is removed. No deny rows. |
| S1-a | `RoleGroup` and `StaffUserRoleGroup` carry the supervision columns; changes take effect only once approved. Seeded rows are created already approved (`IsSupervisionRequired = 0`). |
| S1-b | The bootstrap administrator holds `ADMINISTRATOR` and `SUPERVISOR` in the bootstrap branch, so `SEC/USERS`, `SEC/PERMISSIONS` and `PTY/KYC` are covered from the first day. |
| S1-c | The addendum gives no group supervise on `SEC/PERMISSIONS`, yet R10 needs someone to approve role-group changes. `ADMINISTRATOR` gets supervise on `SEC/PERMISSIONS`. Four-eyes means a second administrator is needed to approve changes. |
| S1-d | Owner decision (2026-10-14): pending role-group changes live in a `ChangeRequest` table (proposed state + before snapshot). Live tables change only on approval, inside one transaction with the last-administrator check. One pending change per record. Party edits may reuse it later. |
| S1-e | `IdempotencyKey` is unique on `(StaffUserId, KeyValue)` and stores a request hash, so a reused key with a different body returns 409. |
