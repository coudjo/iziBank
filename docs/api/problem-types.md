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
| `supervision.permission-denied` | 403 | No supervise permission for the record. |
| `supervision.self-approval` | 409 | The approver created or last changed the record. |
| `supervision.not-pending` | 409 | The record is not awaiting approval. |
| `supervision.pending` | 409 | The record is awaiting approval and cannot be used yet. |
| `supervision.reject-not-allowed` | 409 | The record was approved before; it cannot be rejected. |
| `supervision.has-dependents` | 409 | The record has dependents; it cannot be rejected. |
