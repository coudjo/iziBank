# ADR 0001 — Phase A default decisions

Status: accepted  
Date: 2026-10-11  
Source: Phase A implementation plan, section 12.1

These defaults are in force unless the owner changes the cited section.

| # | Decision | Change by editing |
|---|---|---|
| D1 | Maker-checker applies to creation, and to controlled-field edits (R4). Rejecting a re-pended edit is not supported. | Plan 5.4 |
| D2 | Reads are institution-wide; writes to branch-owned data need the entity's branch to equal the effective branch. | Plan 5.5 |
| D3 | KYC verification and supervision are separate steps. A party needs both before it can open an account. | Plan 6.5 |
| D4 | Products are institution-wide. `Product.BranchId` records the owning branch and does not restrict use. | Plan 6.8 |
| D5 | The `AccountBalance` formulas in plan 5.8 are the definition. Minimum balance is enforced at debit time. | Plan 5.8 |
| D6 | Group accounts and groups as `PRIMARY` holders are allowed. | Plan 6.9 |
| D7 | Fail closed on activity-log write failure for sensitive reads. | Plan 5.6 |
| D8 | An Individual can be an active member of only one group at a time. | Plan 6.4 |
| D9 | `EXPIRED` KYC status is computed, not stored, in this phase. | Plan 6.5 |
| D10 | Direct account opening is limited to `SAVINGS` and `CURRENT`. | Plan 4, 6.9 |

Related: [open-decisions.md](open-decisions.md).
