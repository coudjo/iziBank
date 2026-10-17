# Problem types

Every error response is RFC 7807 problem details. `type` is one of the stable codes below. The portal maps each code to its own message (frontend plan 6.2), and a test in each repo fails if the two lists drift. Source: `Banking.Application.Errors.ProblemCodes`.

| Code | Status | When |
|---|---|---|
| `validation` | 400 | Request fields missing or invalid. `errors` maps field names to messages. |
| `not-found` | 404 | The record does not exist. |
| `unexpected` | 500 | Unhandled error. `traceId` is included. |
| `auth.invalid-credentials` | 401 | Wrong staff code or password, unknown or disabled user. Always the same response. |
| `auth.locked` | 401 | Too many failed logins. `retryAfterSeconds` says when to try again. |
| `auth.unauthenticated` | 401 | No token, or the token is invalid. |
| `auth.session-ended` | 401 | The user signed out, or the session no longer exists. |
| `auth.session-superseded` | 401 | The user signed in elsewhere. |
| `auth.session-timed-out` | 401 | Idle timeout (token expired or the sweeper closed the session). |
| `auth.session-max-exceeded` | 401 | The 12-hour maximum session length was reached. |
| `auth.session-forced` | 401 | An administrator ended the session. |
| `auth.must-change-secret-key` | 403 | The user must change their password before using anything else. |
| `auth.secret-key-rules` | 400 | The new password fails the rules. `rules` lists the failures: `min-length`, `upper`, `lower`, `digit`, `same-as-staff-code`, `same-as-current`. |
| `auth.current-secret-key-wrong` | 400 | The current password given in a change request is wrong. |
| `auth.permission-denied` | 403 | The user's role groups in the effective branch do not grant the action. |
| `auth.branch-not-allowed` | 403 | `X-Branch-Id` (or login `branchId`) names a branch the user cannot work in. |
| `audit.activity-log-unavailable` | 500 | A sensitive read was refused because its activity row could not be written. |
| `session.not-open` | 409 | Ending a session that is already closed. |
| `rolegroup.code-taken` | 409 | A role group with that code exists (or is about to be created). |
| `rolegroup.inactive` | 409 | Deactivating a group that is already inactive. |
| `security.self-assignment` | 409 | Changing your own role groups (addendum 2.4 rule 4). |
| `security.last-administrator` | 409 | Approving the change would leave nobody holding SEC/USERS edit, SEC/PERMISSIONS edit and PTY/KYC supervise. Nothing is applied. |
| `change.pending-exists` | 409 | The record already has a change waiting for approval. |
| `change.not-pending` | 409 | Approving or rejecting a change that is no longer pending. |
| `lookup.code-taken` | 409 | A row with that code already exists in the lookup. |
| `lookup.code-immutable` | 400 | The request tried to change a lookup code. |
| `lookup.coded-row` | 409 | Deactivating a value the system relies on (listed in `LookupCodes`). |
| `staff.code-taken` | 409 | The staff code is already used. |
| `staff.self-change` | 409 | Editing, disabling or resetting your own staff record through the admin API. |
| `reference.code-taken` | 409 | A branch, currency or ledger account with that code already exists. |
| `reference.code-immutable` | 400 | The request tried to change a branch, currency, ledger account or product code. |
| `record.other-branch` | 409 | The record belongs to another branch; writes need the effective branch to match (D2). |
| `record.changed` | 409 | Someone else changed the record first (optimistic concurrency). Reload and retry. |
| `currency.local-in-use` | 409 | Making another currency local once products exist (ADR 0002, A3-f). |
| `currency.local-required` | 409 | Switching off the local currency. Make another currency local instead; the flag moves in one step. |
| `holiday.exists` | 409 | A holiday is already defined for that date. |
| `holiday.business-day-exists` | 409 | Adding or removing a holiday on a date that a branch has already opened. |
| `business-day.not-open` | 409 | No open business day for the branch (closing a day that is not open, or `IWorkingDateProvider`). |
| `business-day.previous-open` | 409 | Opening a day while the branch's latest day is not closed. |
| `business-day.date-not-after` | 409 | The date is on or before the branch's latest business day. |
| `business-day.non-working-day` | 409 | The date is a weekend day (`Calendar:WeekendDays`) or a defined holiday. |
| `ledger.field-locked` | 409 | Changing the type or currency of an approved ledger account. |
| `product.code-taken` | 409 | A product with that code already exists (codes are institution-wide). |
| `product.field-locked` | 409 | Changing the type, account prefix or currency of an approved product. |
| `product.rate-overlap` | 409 | The rate's dates and amount band overlap another rate of the same product and rate type. |
| `product.rate-started` | 409 | Changing or removing a rate whose start date has passed. Close it instead. |
| `account.product-not-openable` | 409 | The product is pending, inactive or frozen, or its type is not in `Account:DirectOpeningProductTypes`. |
| `account.holder-not-ready` | 409 | A holder is awaiting approval or is not KYC verified. `partyNumbers` lists them. |
| `account.signature-missing` | 409 | A person who signs on the account has no approved specimen signature (addendum rule 3a). `partyNumbers` lists them. |
| `account.closed` | 409 | Any change to a closed account (closed accounts are read-only). |
| `account.already-frozen` | 409 | Freezing an account that is already frozen. |
| `account.not-frozen` | 409 | Unfreezing an account that is not frozen. |
| `account.already-dormant` | 409 | Marking a dormant account dormant. |
| `account.not-dormant` | 409 | Reactivating an account that is not dormant. |
| `account.close-blocked` | 409 | Closing with a balance, a hold or unsupervised amounts. `failedChecks` lists `ZERO_BALANCE`, `NO_HOLDS`, `NO_PENDING_ITEMS`. |
| `account.classification-exists` | 409 | Assigning a classification the account already has. |
| `account.limit-reached` | 409 | The primary holder already has `AccountsPerCustomer` open accounts on this product. |
| `party.duplicate-identification` | 409 | A customer already holds this identification type and number. `existingPartyNumber` names them. |
| `party.duplicate-registration` | 409 | A business already holds this registration number. `existingPartyNumber` names it. |
| `group.member-in-other-group` | 409 | The person is an active member of another group (D8). `memberPartyNumber` and `groupPartyNumber` say who and where. |
| `group.chairperson-exists` | 409 | Adding or promoting a chairperson while the group already has one. |
| `party.primary-document` | 409 | Deleting an individual's primary identification document (the one on the individual's details). |
| `upload.empty` | 400 | The request body has no file content. |
| `upload.too-large` | 400 | The file exceeds `Uploads:MaxImageBytes` (1 MB for signatures). `maxBytes` gives the limit. |
| `upload.type-not-allowed` | 400 | The file's leading bytes are not an allowed type (JPEG or PNG for photos; JPEG, PNG or PDF for documents). |
| `upload.type-mismatch` | 400 | The `Content-Type` header names a different type from the file's content (for example a PDF sent as `image/png`). |
| `upload.dimensions` | 400 | A signature PNG is wider than 2000 or taller than 1000 pixels. `maxWidth` and `maxHeight` give the limits. |
| `credit-officer.exists` | 409 | Designating (or re-enabling) a staff member who is already an active credit officer. |
| `kyc.checklist-incomplete` | 409 | Verifying KYC while required checklist items are missing. `missing` lists their codes. |
| `kyc.not-pending` | 409 | Verifying a customer whose KYC is already verified, or rejecting one already rejected. |
| `idempotency.key-invalid` | 400 | The `Idempotency-Key` header is empty, longer than 100 characters or has spaces or control characters. |
| `idempotency.key-reused` | 409 | The same key was used earlier by this user for a different request (route or body). |
| `idempotency.in-progress` | 409 | The first request with this key has not finished yet. Retry shortly. |
| `supervision.permission-denied` | 403 | No supervise permission for the record. |
| `supervision.self-approval` | 409 | The approver created or last changed the record. |
| `supervision.not-pending` | 409 | The record is not awaiting approval. |
| `supervision.pending` | 409 | The record is awaiting approval and cannot be used yet. |
| `supervision.reject-not-allowed` | 409 | The record was approved before; it cannot be rejected. |
| `supervision.has-dependents` | 409 | The record has dependents; it cannot be rejected. |
