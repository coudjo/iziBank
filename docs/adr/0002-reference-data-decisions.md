# ADR 0002: Reference data, business days and chart of accounts (A3)

Status: accepted (2026-10-14). Decisions made while building A3; the owner can overturn any of them.

| # | Decision | Why |
|---|---|---|
| A3-a | An approved ledger account never returns to Pending. A new name is a `ChangeRequest` (`LEDGER_ACCOUNT_RENAME`) that applies when a different supervisor approves it; type, currency, code and branch never change after approval. A Pending account is edited in place and can be rejected (deleted, R5). | Plan 5.4 R4 would put an account in use back to Pending, and R3 would then block postings to it over a name change. With this rule "rejected" only ever applies to an account that was never approved, so R5 needs no history lookup. |
| A3-b | Ledger change requests are approved at `POST /ledger-accounts/{id}/approve` (`LED/CHART` supervise), not at `/change-requests`, which stays limited to role groups and assignments (`SEC/PERMISSIONS`). | Approval rights follow the module. |
| A3-c | Closing a business day needs `REF/CALENDAR` supervise (plan 6.12). The seeded `SUPERVISOR` group now has it. Opening needs edit. Both need the effective branch to be the day's branch (`record.other-branch` otherwise). | No seeded group could close a day. Existing databases keep their old matrix: add supervise on `REF/CALENDAR` to `SUPERVISOR` through the role-group screen, or recreate the dev database. |
| A3-d | Weekend days come from `Calendar:WeekendDays` (default Saturday and Sunday). Days may be skipped; a branch's first day can be any working day. | Plan 6.12 only says "after the previous day". |
| A3-e | A holiday cannot be added or removed on a date any branch has already opened. | Keeps the calendar consistent with the days actually worked. |
| A3-f | Making a currency local moves the flag from the current local currency in one transaction, and sets its rate to 1. The local currency cannot be switched off on its own. | "Exactly one local currency" at every moment. Changing the local currency once products or accounts exist will be blocked when A5 lands. |
| A3-g | Reference reads (`/institution-profile`, `/branches`, `/currencies`, `/holidays`, `/business-day/current`) need only a signed-in user. Writes need the `REF` permissions. The business-day history needs `REF/CALENDAR` view. | Every screen needs branch, currency and working-date data. |
| A3-h | A teller's till account (`TellerGLAccountId`) must be an approved `ASSET` ledger account in the teller's home branch. Clearing `isTeller` clears the account. | Plan 6.11, plus R3 (a Pending account cannot be used). |

## A4 parties (2026-10-15)

| # | Decision | Why |
|---|---|---|
| A4-a | The group chairperson is the active member with role `CHAIRPERSON`, mirrored in `GroupDetail.ChairpersonPartyId`. One column means a group can never record two; the service refuses a second (`group.chairperson-exists`). A group may exist without a chairperson; KYC will require one. | Plan 6.4 asks for one chairperson enforced in the database; a filtered index cannot name a lookup code. |
| A4-b | Business owners and group members can be referred to while still Pending; KYC verification (A4-07) checks that they are verified. | Field officers register the people and the group in one visit. |
| A4-c | Replacing beneficial owners and every member change (join, leave, role) is a material change: KYC back to `PENDING` and, with supervision on, the record back to Pending (R4). | Plan 6.5. |
| A4-d | Business registration numbers and document numbers are stored upper case without spaces, like individual ID numbers, so duplicates are found whatever the typing. | Consistent duplicate checks. |
