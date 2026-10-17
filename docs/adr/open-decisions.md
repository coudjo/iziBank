# Open decisions

Use this file when the plan has no default. Pick the most restrictive behaviour, then record it here.

## Owner questions (plan 12.2)

| # | Question | Current default | Owner decision |
|---|---|---|---|
| Q1 | Is a product defined per branch or for the whole institution? | Institution-wide (D4). `ProductCode` is globally unique. | Pending |
| Q2 | Should customers need both supervision and KYC verification, or should one approval cover both? | One action covers both (D3, R13). | Decided by owner |
| Q3 | Which of `Account.OperatingModeId` and `AccountHolder.OperatingInstructionId` is authoritative? | Account-level mode. Holder-level field unused in Phase A. | Pending |
| Q4 | Can an individual belong to more than one group at once? | No (D8). | Pending |
| Q5 | Should staff see parties and accounts from all branches or only their own? | All branches for reads (D2). | Pending |
| Q6 | Regulator KYC field list, accepted ID types, PEP handling, minimum group size, beneficial-owner threshold. | Placeholders in configuration (plan section 4). | Pending |
| Q7 | Do changes to product interest rates need four-eyes approval? | No: `ACC/PRODUCT` edit, logged (ADR 0002, A5-b). Started rates are only closed, never edited. | Pending |
| Q8 | How should an approved account's name or operating mode change: a change request (like ledger accounts) or back to Pending? | Not built yet (ADR 0002, A5-r). | Pending |

## Decisions recorded during implementation

None yet.
