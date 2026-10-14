# Phase A UX Design
## Staff back-office: customers, KYC, accounts, approvals, administration

**Scope:** the staff web app (Angular) that sits on the Phase A API. No customer-facing screens in this phase.
**Brand:** iziBank (logo and colours pending)
**Status:** UX only (users, flows, structure, behavior). Visual design (colors, type, components) is the next step, once this is agreed.
**Source of truth for behavior:** `phase_a_implementation_plan.md`. Where this document asks for something the plan does not have, it is listed in section 9.

---

## 1. Design principles

1. **Speed at the counter.** Tellers and customer service officers repeat the same tasks all day. Everything common is reachable from the keyboard, and search is always one keystroke away.
2. **Make the rules visible.** Maker-checker, KYC and account state are never hidden. Every record shows what state it is in, what blocks it, and who can move it forward.
3. **Never lose work, never double-submit.** Forms survive session expiry. Buttons lock while a request is in flight. After a network failure the screen checks what was saved before offering a retry.
4. **Prevent errors before reporting them.** Ineligible choices are shown disabled with a reason, instead of failing after submit.
5. **Sensitive by default.** Identification numbers are masked until revealed. Viewing customer records is logged, and the interface says so.
6. **Plain business language.** The interface says "customer", "approve", "hold". It never shows "Party", "supervise" or internal codes.

---

## 2. Users and roles

Access is granted through **role groups**. Each role group holds permissions over the system's modules and sub-modules. A staff member belongs to one or more role groups, assigned per branch, and their effective permissions for that branch are the combined permissions of those groups. The six groups below are the seeded starting point. Administrators can edit them and create more.

| Role group (seeded) | Main jobs in Phase A | Typical permissions |
|---|---|---|
| Teller / customer service officer | Find customers, onboard customers, open accounts, add reminders, view balances | PTY/PARTY add+edit, ACC/ACCOUNT add+edit |
| Field / credit officer | Onboard individuals and groups, manage group members, collect documents and photos, often on a tablet | PTY/PARTY, PTY/DOCUMENTS, PTY/GROUPS |
| Compliance / KYC officer | Review documents, verify or reject KYC, watch expiries and PEP flags | PTY/KYC verify, PTY/PARTY and PTY/DOCUMENTS supervise |
| Branch supervisor / manager | Approve new and changed records, close accounts, hold and freeze, open and close the business day | CanSupervise on PTY, ACC, LED; ACC/CONDITION; REF/CALENDAR |
| System administrator | Staff and permissions, branches, currencies, holidays, lookups, products, chart of accounts | SEC, REF, ACC/PRODUCT, LED/CHART |
| Auditor | Read activity and data-change logs and sessions. No editing anywhere. | SEC/AUDIT view only |

A staff member can belong to several role groups. The interface is built from effective permissions, never from group names: if a user has no view access to a module, its menu entry does not exist for them. Supervising works the same way. Holding the supervise permission on a module, through the supervisor group, the KYC group or any custom group, is what allows approving records in that module.

---

## 3. Context of use and constraints

| Factor | Design response |
|---|---|
| Desktop at a counter, 1366x768 is common | Desktop first. Primary layout is 1280 wide, must work at 1024. Dense but readable tables. |
| Tablets and phones in the field | Customer onboarding, documents, photo, specimen signature, group members and approval review must work on tablets and phones. See section 3.2. Touch targets at least 44px. Camera capture for photos and documents. Signature capture by finger or stylus. |
| Intermittent connectivity | No offline writes (this is banking). Clear connection status, skeleton loaders, safe retry behavior (3.1). |
| Shared screens, shoulder surfing | Masked ID numbers by default. Auto sign-out. No sensitive data in page titles or URLs. |
| Four-eyes rules | Creator cannot approve their own work. The interface explains this instead of just disabling the button. |
| Ghana locale | English (Ghana). Dates `dd/MM/yyyy`. Currency shown as `GH₵ 1,234.50`. Phone entry accepts `024...` and `+233...` and normalizes. Time zone is UTC+0 with no daylight saving. |
| Accessibility | WCAG 2.2 AA. Status never relies on color alone (icon plus text). Full keyboard operation. |

### 3.1 Network failure behavior
- Any create or action button is disabled and shows a spinner while the request is running.
- On timeout or lost connection the dialog says: "We could not confirm this was saved. Check before trying again." It then offers **Check now**, which re-queries (for a customer: the duplicate ID check; for an account: the customer's account list) and shows the result. A blind **Retry** is only offered after the check finds nothing.
- Reads retry automatically twice, then show an inline error with **Try again**.

---

### 3.2 Field and mobile layouts
- **Breakpoints:** desktop 1280 and up, tablet 768 to 1279, phone below 768.
- **Tablet:** the full shell, with the sidebar collapsed to icons. Tables become cards where a row would need horizontal scrolling. Forms use one column.
- **Phone:** the sidebar becomes a bottom bar with four destinations: Customers, Accounts, Approvals (with badge) and More. Global search sits at the top of each page. Multi-column screens stack.
- **Field tasks, designed mobile-first:** customer search and profile, onboarding for individuals and groups, document and photo capture, specimen signature, adding group members, viewing the KYC checklist.
- **Desktop-preferred tasks:** account opening with several holders, product and ledger setup, staff administration, role groups, audit review. These work on a tablet. On a phone they show a notice with the option to continue anyway.
- **Capture:** photos and documents come from the device camera and are compressed on the device to under 2 MB before upload. The server limit stays at 5 MB. Captured images stay in memory until saved. Nothing is written to device storage.
- **Signature:** a full-width canvas with Clear and Accept. Accept requires the officer to tick "The customer has signed in front of me". The signature is stored as PNG.
- **Upload on mobile data:** uploads show progress and can be cancelled. A failed upload keeps the captured image in memory, so the user can retry without capturing again, until the screen is closed. Retries are idempotent (section 9).
- **Shared field devices:** the automatic sign-out in 5.8 applies. On phones, the sign-out control sits in the bottom bar.

## 4. Information architecture

### 4.1 App shell (every screen after login)

```
+----------------------------------------------------------------------------------+
| iziBank  [ Search customers or accounts ( / )         ]   Branch: Head Office       |
|                                                          Working date: 12/10/2026  |
|                                                          [Approvals 7]  [User v]   |
+--------------+-------------------------------------------------------------------+
| Dashboard    |  Breadcrumb > Page title                       [Primary action]    |
| Customers    |  ---------------------------------------------------------------- |
| Accounts     |                                                                   |
| Approvals    |                      Page content                                 |
| Products     |                                                                   |
| Chart of     |                                                                   |
|  accounts    |                                                                   |
| Administration|                                                                  |
|  Security    |                                                                   |
|  Reference   |                                                                   |
|  Audit       |                                                                   |
+--------------+-------------------------------------------------------------------+
```

Shell rules:
- **Branch and working date** are always visible. If the business day is not open, the date area shows a warning chip "Business day not open" and account opening is disabled with the reason.
- **Global search** (`/` or `Ctrl+K`) searches customers and accounts together and groups results. Matches on customer number, account number, name, mobile, ID number.
- **Approvals badge** shows the count of items waiting for this user to approve. Only users with approve rights see it.
- **User menu:** profile, change password, switch branch (only if allowed), sign out.
- The sidebar collapses to icons below 1280 wide. Below 768 it becomes the bottom bar in 3.2.

### 4.2 Sitemap and routes

| Area | Route | Notes |
|---|---|---|
| Sign in | `/login`, `/change-password` | Change password is also the forced first-login screen. |
| Dashboard | `/dashboard` | Role-based widgets (7.1). |
| Customers | `/customers` | Search and list. |
| | `/customers/new/individual`, `/new/business`, `/new/group` | Onboarding. |
| | `/customers/:id` | Customer record with tabs (7.3). |
| Accounts | `/accounts`, `/accounts/new`, `/accounts/:id` | |
| Approvals | `/approvals`, `/approvals/:type/:id` | Inbox and review page. |
| Products | `/products`, `/products/new`, `/products/:id` | Rates tab inside. |
| Chart of accounts | `/ledger-accounts`, `/ledger-accounts/new`, `/ledger-accounts/:id` | |
| Administration > Security | `/admin/staff`, `/admin/staff/:id`, `/admin/role-groups`, `/admin/role-groups/:id`, `/admin/sessions` | |
| Administration > Reference | `/admin/institution`, `/admin/branches`, `/admin/currencies`, `/admin/holidays`, `/admin/business-day`, `/admin/lookups/:name`, `/admin/credit-officers` | |
| Audit | `/audit/activity`, `/audit/data-changes` | |

URLs carry only IDs, never names, ID numbers or other personal data.

---

## 5. Cross-cutting interaction patterns

### 5.1 Status chips
One consistent set, always icon plus text.

| Domain | Labels |
|---|---|
| Approval | Awaiting approval, Approved |
| KYC | KYC pending, Verified, Rejected, Expired |
| Account | Awaiting approval, Active, Frozen, Dormant, Closed |
| Business day | Open, Closed, End of day running |

A record can show several chips (a customer: "Awaiting approval" + "KYC pending"). Each chip is clickable and opens a short explanation of what it means and what moves it forward.

### 5.2 Maker-checker behavior
- Creating a record ends on its detail page with a banner: "Saved. This customer is awaiting approval by a supervisor."
- **Approve** is shown only to users with approve rights. If the viewer created or last edited the record, the button is disabled and the tooltip reads: "A different supervisor must approve this, because you created or changed it."
- **Reject** is offered only on records never approved before. It requires a reason and a typed confirmation ("Reject and delete this record?"). For a re-pended edit the screen explains: "This record was changed after approval. Correct it with Edit, then approve."
- Editing an approved record, on a protected field, warns before saving: "Changing this will send the customer back for approval and reset KYC to pending." The user must confirm.

### 5.3 Forms
- Labels above fields, required fields marked, helper text for formats.
- Validate on blur and on submit. An **error summary** at the top lists every problem as a link that moves focus to the field.
- Server errors (problem details with an `errors` map) are mapped onto the same fields. Entered data is never cleared by an error.
- Long forms use sections with a sticky section index. A sticky action bar holds **Save** and **Cancel**.
- Leaving a form with unsaved changes asks for confirmation.
- Phone, date and money inputs normalize on blur.
- No autosave and no drafts in browser storage. Customer data is personal data and is kept in memory only.

### 5.4 Tables and lists
- Server-side paging (default 25), sortable columns, filters above the table, the filters are remembered per screen for the session.
- Row click opens the record. A visible primary link per row for keyboard users.
- ID numbers show the last four characters only. Money is right-aligned.
- Empty state says why it is empty and offers the next action ("No customers match. Clear filters or add a customer").

### 5.5 Sensitive data
- **Masked by default** on detail screens too: ID numbers, tax numbers, mobile numbers show partially, with a **Show** control per field. The control hides the value again after 30 seconds.
- A discreet notice on customer, account, document and audit screens: "Viewing this record is logged."
- Document, photo and signature images open in a viewer overlay, never as a direct link. No right-click download hint. Images are fetched only when opened.

### 5.6 Feedback
- **Toast** for success of low-risk actions (auto-dismiss 5 seconds).
- **Banner** on the page for states that persist (awaiting approval, frozen, closed).
- **Dialog** for confirmations and for anything that needs a reason.
- **Inline** for field problems.

### 5.7 Concurrency conflicts
When the API returns a concurrency conflict (409) the user sees: "This record was changed by someone else while you were working." Actions: **Reload** (discard my changes) or **Review changes** (shows the new values beside theirs). Hold and freeze actions retry automatically (the API retries up to 3 times), and only show the message if that fails.

### 5.8 Session handling
- The access token lasts 15 minutes. While the user is active, the app renews it silently when under 5 minutes remain.
- If the user is idle, a dialog appears 2 minutes before expiry: "You will be signed out in 2:00." with **Stay signed in** and **Sign out**.
- When the session ends mid-task, the page does not navigate away. A **re-sign-in dialog** appears over the page, so in-memory form data is preserved. If a different user signs in, the data is discarded and the user is told why.
- Maximum session length (12 hours): a notice appears 10 minutes before: "Your session reaches its maximum length at 18:30. Save your work."
- If the session was ended elsewhere, the sign-in page explains the reason: signed in on another device, ended by an administrator, or timed out.

### 5.9 Keyboard
| Key | Action |
|---|---|
| `/` or `Ctrl+K` | Focus global search |
| `Alt+N` | New (customer on the customer screens, account on the account screens) |
| `Ctrl+S` | Save the current form |
| `Esc` | Close dialog or overlay |
| `Enter` | Submit the focused form or confirm the default dialog action |

Shortcuts are listed in a help overlay (`?`).

---

## 6. Key flows

Each flow lists the steps, the decisions, and the failure paths. API behavior is from the Phase A plan.

### 6.1 Sign in and first sign-in
1. Enter staff code and password. One generic error for unknown code, wrong password and disabled user: "Check your staff code and password."
2. After repeated failures: "Too many attempts. Try again in 12 minutes." This message is safe to show for any code, because lockout is counted from attempts, not from known users.
3. If a password change is required (first sign-in, reset, or expiry), the user goes to **Change password** and cannot reach anything else. The screen shows the password rules as a live checklist.
4. A user with several branches may pick the working branch after sign-in.
5. Landing page is the dashboard.

### 6.2 Onboard an individual customer
Goal: a customer record that can reach "Verified" and "Approved".

```
Step 1 Identity          Step 2 Contact              Step 3 Source of funds
First, last name         Mobile (required)           Source of funds
Title, gender            Phone, email                Occupation or sector
Date of birth            Address line 1, 2           PEP status (yes/no)
Nationality              
ID type + number
```

1. **Create** after step 3. The form is one transaction, so there is no half-saved customer.
2. **Duplicate check on ID.** If the ID type and number already exist the user sees an inline panel: "A customer with this ID already exists: [Name, customer number]" with **Open that customer**. Saving is blocked.
3. The user lands on the new customer's page with a **Complete the profile** card, because KYC needs more than the creation form:
   - Photo (take or upload)
   - ID document image
   - Next of kin
   - Specimen signature (required before any account can be opened for this customer)
4. The checklist (from the KYC checklist endpoint) shows each item as done or missing, with a button that goes straight to the right tab.
5. When the checklist is complete, **Send for KYC verification** is not needed: the compliance officer sees the customer in their queue (6.5).
6. A supervisor approves the record separately (6.6).

Failure paths: invalid ID type for individuals, under-age date of birth (message states the minimum age), upload too large or wrong type (message states allowed types and size).

### 6.3 Onboard a business customer
1. Step 1 Business: name, registration number, legal form, tax number, incorporation date.
2. Step 2 Contact and funds: phone or mobile, address, source of funds, sector.
3. Create, then the **Complete the profile** card: certificate image, tax certificate, **beneficial owners**.
4. Adding a beneficial owner opens a customer picker restricted to individuals, with "Add new individual" inline if the person is not yet a customer. Percentage input shows the running total and blocks more than 100.

### 6.4 Onboard a group
1. Step 1 Group: name, formation date, meeting frequency.
2. Step 2 **Chairperson**: pick an existing individual customer, or create one inline. The chairperson becomes the first member.
3. Create, then the **Members** tab opens with an **Add member** action (individuals only).
4. A person who is already in another active group is shown in the picker as disabled with "Already in group [name]".
5. The checklist shows the minimum member count and which members are not yet verified, each as a link.

### 6.5 Verify KYC (compliance officer)
1. Work queue: customers with KYC pending whose checklist is complete, oldest first.
2. The verification screen is split: **left**, the customer's details and the checklist; **right**, the document and photo viewer with next/previous.
3. PEP is shown as a prominent flag.
4. **Verify and approve** and **Reject** sit in a sticky bar. Verify and approve confirms the details and approves the record in one action. Reject requires a reason.
5. If the checklist is incomplete, Verify is disabled and the missing items are listed.
6. A verifier who created or last changed the record sees the four-eyes explanation instead of an active button.

### 6.6 Approve a record (supervisor)
1. **Approvals inbox:** tabs for Customers, Photos, Accounts, Products, Chart of accounts. Columns: what, who created it, when, age. Sorted oldest first.
2. Opening an item shows the full record read-only with an approval bar.
3. For a record that was edited after approval, a **Changes since last approval** panel lists each field with old and new value (needs the diff API, section 9).
4. **Approve** records the approval. **Reject** follows 5.2.
5. After approving, the screen moves to the next item in the inbox.

### 6.7 Open an account
Single page, three sections, not a wizard.

```
1 Product and type            2 Holders                         3 Review
Product (search)              Primary holder (search)           Account name (editable)
Shows: type, currency,        [ Ama Mensah   KYC verified ]     Operating mode
 minimum balance              Add joint holder                  Summary of holders
                              Add signatory                     [Open account]
                              Add beneficial owner (info)
```

1. The business day must be open, or the whole page is disabled with the reason.
2. Product picker shows only products that can be opened directly (savings, current), approved, active, not frozen.
3. Holder picker results carry eligibility. A customer who is not verified, awaiting approval, or rejected appears disabled with the reason and an **Open customer** link. Every person who can sign on the account needs a supervised specimen signature on file. Without one they appear disabled with "Capture specimen signature first".
4. The section adapts to the primary holder:
   - Individual alone: operating mode is "Single" and hidden.
   - Individual with joint holders: choose "Either to sign" or "All to sign".
   - Business: at least one signatory (individual), with the same mode choice.
   - Group: signatories are chosen from the group's active members.
5. The account name is prefilled from the holders and editable.
6. **Open account** shows the generated account number on success, with the banner "Awaiting approval. The account cannot be used until a supervisor approves it." and **Done** or **Open another account**.
7. Opening deposits are not part of this phase. The success page does not offer one.

### 6.8 Service an account
Actions sit in an **Actions** menu on the account page, filtered by permission and state.

| Action | Dialog content | Confirmation |
|---|---|---|
| Hold amount | Amount, remarks (required in the UI) | Shows the new available balance before confirming |
| Release hold | Amount (not more than held), remarks | Same |
| Freeze account / Unfreeze | Remarks | Plain confirm |
| Mark dormant / Reactivate | Remarks | Plain confirm |
| Close account | Reason (required), checks list | User types the account number to confirm |
| Add or remove reminder, classification | Inline | None |

**Close account** first shows a check list: balance is zero, no holds, no pending items. Failed checks are listed and the confirm button stays disabled. Closed accounts become read-only, with a banner showing who closed it and why.

### 6.9 Define a product (administrator)
1. Form: code, name, type, account prefix, currency, control ledger account, minimum balance, accounts per customer, cheque book allowed.
2. The ledger account picker is filtered to the right type (liability for deposits). If none exists it explains why and links to the chart of accounts.
3. **Rates tab:** a timeline of rates. Adding a rate shows the dates it will close the previous rate. Started rates are not editable, only closable. Overlaps are prevented in the form and explained.
4. A new product awaits approval like any other record.

### 6.10 Set up a staff member
1. Create staff: code, full name, branch, initial password. The screen warns the user must change it on first sign-in.
2. **Role groups tab:** choose one or more role groups per branch. Below the choice, the effective permissions matrix (modules and sub-modules against **View, Add, Edit, Delete, Approve**) shows the result and which group grants each permission.
3. Role groups are managed under **Administration > Security > Role groups**. Editing a group changes every member, so the screen shows how many people a change affects before saving.
4. Saving shows a summary of what changed before confirming ("Adds Approve on Customers for Head Office").
5. Teller flag requires choosing the teller's cash ledger account.
6. **Sessions tab:** open sessions with **Force sign-out**. **Disable** and **Reset password** are in the actions menu, each with a reason.

### 6.11 Open and close the business day
1. Under Administration > Reference > Business day, a supervisor sees the current state per branch.
2. **Open day** proposes the next valid working day and states why other dates are not allowed (weekend, holiday, previous day still open).
3. **Close day** shows a warning that end-of-day processing is not part of this phase.

### 6.12 Review audit logs
1. Activity log filters: staff member, date range, event type, record type, outcome.
2. Row detail shows a readable summary, with the raw JSON in an expandable block.
3. Data-change log shows old and new values side by side for each changed field. Sensitive values never appear (the API redacts them).
4. A notice states that viewing the audit log is itself recorded.

---

## 7. Screen inventory

### 7.1 Dashboard
Widgets depend on permissions. Each is a number with a link.

| Widget | Shown to |
|---|---|
| Awaiting my approval | Supervisors |
| KYC awaiting verification | KYC officers |
| KYC expiring in 30 days | KYC officers |
| Customers created today, accounts opened today | Everyone with view access |
| Business day status | Everyone |
| Open sessions, locked-out staff | Administrators |

### 7.2 Customers list (`/customers`)
Search box, filters (type, KYC status, approval state, credit officer). Columns: customer number, name, type, mobile, KYC chip, approval chip, created. Primary action: **New customer** (menu: individual, business, group).

### 7.3 Customer record (`/customers/:id`)

```
+------------------------------------------------------------------------------+
| Ama Mensah   C-00001234   [Individual]  [Verified] [Awaiting approval] [PEP]   |
| Credit officer: Kofi A.                       [Edit] [Verify KYC] [Approve]    |
+------------------------------------------------------------------------------+
| Overview | Details | KYC | Documents | Next of kin | Accounts | Regulatory | Activity
+------------------------------------------------------------------------------+
| Complete the profile (2 of 5 done)         | Accounts held                    |
|  [x] Photo   [x] ID document               |  1000123456789  Savings  Active  |
|  [ ] Next of kin   [ ] Source of funds     |  [Open account]                  |
+------------------------------------------------------------------------------+
```

Tabs vary by type:

| Tab | Individual | Business | Group |
|---|---|---|---|
| Overview | yes | yes | yes |
| Details | yes | yes | yes |
| KYC | yes | yes | yes |
| Documents, photo and signature | yes | yes | no |
| Next of kin | yes | optional | no |
| Beneficial owners | no | yes | no |
| Members | no | no | yes |
| Accounts | yes | yes | yes |
| Regulatory attributes | yes | yes | yes |
| Activity | yes | yes | yes |

- **KYC tab:** checklist, status history, verify and reject actions, reason fields.
- **Regulatory attributes tab:** current values with a history drawer (values are append-only).
- **Activity tab:** requires audit permission. Others do not see it.

### 7.4 Accounts list and record
List: account number, name, product, branch, primary holder, status chip, created. Record tabs: **Summary** (balances, status, reminders), **Holders**, **Balance and holds** (hold history), **Conditions** (freeze, limits, flags), **Classification**, **Activity**.

Balances show clear balance, held amount, available balance, total balance. Until the transactions phase all read zero, and the page says "No transactions yet" instead of showing empty figures without explanation.

### 7.5 Approvals (`/approvals`)
Tabs per record type with counts. Review page per 6.6.

### 7.6 Products, chart of accounts
List with search and status filter. Detail pages follow 6.9 and a similar simple form for ledger accounts (code, name, type, currency, branch).

### 7.7 Administration
Staff list and detail (6.10), role groups (list, create, edit the permission matrix, see the member count; a group in use cannot be deleted), sessions, institution profile (single form), branches, currencies (one local currency, enforced and explained), holidays (calendar view with add and remove), business day (6.11), lookups (one screen per lookup, no delete, deactivate only, system codes show a lock icon), credit officers.

### 7.8 Audit (`/audit/*`)
Per 6.12.

---

## 8. Content and error messages

Messages are written for staff, say what happened and what to do, and never show codes. The API sends a stable error code in the problem details `type`. The front end owns the wording.

| API error code | Message |
|---|---|
| `party.duplicate-identification` | A customer with this ID already exists: {name} ({number}). |
| `party.kyc.not-verified` | {name}'s identity checks are not complete, so an account cannot be opened yet. |
| `party.pending-approval` | {name} is waiting for supervisor approval. |
| `business-day.not-open` | The business day is not open for this branch. Ask a supervisor to open it. |
| `supervision.self-approval` | A different supervisor must approve this, because you created or changed it. |
| `account.close.balance-not-zero` | The balance must be zero before the account can be closed. |
| `account.hold.exceeds-balance` | The amount to release is more than what is currently held ({held}). |
| `account.state.frozen` | This account is frozen. |
| `concurrency.conflict` | This record was changed by someone else while you were working. |
| `auth.invalid-credentials` | Check your staff code and password. |
| `auth.locked` | Too many attempts. Try again in {minutes} minutes. |
| `auth.session.superseded` | You were signed out because your staff code signed in somewhere else. |

Rules: sentence case, no blame ("The password is too short", not "You entered a bad password"), numbers and names in the message where useful. The API publishes the full code list (section 9), and every code needs a message before release.

Terms: customer (not party), approve (not supervise), awaiting approval (not pending supervision), hold amount (not freeze balance), password (not secret key), chart of accounts (not ledger account list).

---

## 9. Requests to the Phase A plan (UX needs the API cannot yet meet)

These are additions or changes. Each one is needed for a flow above.

| # | Request | Needed by |
|---|---|---|
| 1 | `GET /approvals` returning pending items across customers, photos, accounts, products and ledger accounts, limited to what the caller can approve, with counts. Mapping assumed: customer to PTY/PARTY, photo to PTY/DOCUMENTS, account to ACC/ACCOUNT, product to ACC/PRODUCT, ledger account to LED/CHART. | 6.6, header badge |
| 2 | `GET /{entity}/{id}/pending-changes`: fields changed since the last approval, old and new. Re-pended edits are not staged in the plan, so the approver otherwise cannot see what changed. | 6.6 |
| 3 | `GET /dashboard/summary` with the counts in 7.1. | 7.1 |
| 4 | A published catalog of error codes (the `type` of every 409, and `retryAfterSeconds` on `auth.locked`), kept in `docs/api/problem-types.md`. | Section 8 |
| 5 | Eligibility hints in party search results: effective KYC status, approval state, and `canBePrimaryHolder`, `canBeJointHolder`, `canBeSignatory` flags. | 6.7 |
| 6 | Group creation accepts the chairperson (existing individual) as the first member in the same call. The plan requires the chairperson to be an active member, so creating a group otherwise has a chicken-and-egg problem. | 6.4 |
| 7 | Specimen signature as a dedicated `PartySignature` table (it is not an identification document). Endpoints: capture, supervise, read image. Required for signatories. | 6.2, 6.7 |
| 8 | Optional `Idempotency-Key` header on `POST /accounts` and `POST /accounts/{id}/freeze-actions`, so a retry after a timeout cannot duplicate. | 3.1 |
| 9 | `GET /auth/me` returns, per module and sub-module, the flags (view, add, edit, delete, approve), so the front end can show or hide navigation and buttons. (Already in the plan; confirm the shape.) | 4.1 |
| 10 | Staff-side summary `GET /customers/{id}/overview` includes the checklist result, so the "Complete the profile" card does not need a second call. | 7.3 |
| 11 | Role groups API: list and manage role groups, each with a module and sub-module permission matrix; assign role groups to a staff member per branch; list the members of a group. | 6.10, 7.7 |
| 12 | Confirm every module and sub-module is seeded, so the permissions matrix is complete. `GET /modules` (in the plan) feeds it. | 6.10 |
| 13 | Signature and photo capture accept PNG from a canvas and JPEG from a camera, with a 5 MB limit. Photo and document uploads accept an `Idempotency-Key` header. | Section 3.2 |
| 14 | Effective permissions in `GET /auth/me`, including the supervise flag per record type (customers, photos, signatures, accounts, products, ledger accounts), so buttons and the approvals badge follow the same rule. | 6.6, 4.1 |

---

## 10. Out of scope for this UX phase (noted so they are not forgotten)

- Printed forms: not in Phase A (answer 3). Account opening, KYC and signature cards are a future addition.
- Customer-facing screens (mobile app, USSD, self-service).
- Transactions: deposit, withdrawal, transfer. Account pages reserve space for balances and history that arrive then.
- Loans, overdraft, fixed deposits, cheques, notifications.

---

## 11. Usability test plan

Test with five or more real staff per role before the UI is finalized. Targets are starting points.

| # | Scenario | Success measure |
|---|---|---|
| 1 | Find a customer from a partial name, then from a mobile number | Under 10 seconds, no help |
| 2 | Onboard an individual to a complete checklist (photo included) | Under 6 minutes, no errors left unfixed |
| 3 | Open a joint account | Under 3 minutes, correct holders and mode |
| 4 | Supervisor approves 10 items from the inbox | Under 4 minutes, nothing approved by mistake |
| 5 | KYC officer verifies a customer and rejects another with a reason | Under 3 minutes each |
| 6 | Administrator creates a teller with the right permissions | Under 5 minutes, permissions match intent |
| 7 | Session expires mid-form, user signs back in | No data lost, user understands what happened |
| 8 | Network drops during account opening | User checks before retrying, no duplicate account |

Also measure: errors per task, help requests, and whether people understand the status chips without explanation.

---

## 12. Decisions and open questions

### 12.1 Decided
| # | Decision |
|---|---|
| 1 | Access is granted through role groups. Permissions are per module and sub-module, and a staff member's role groups are assigned per branch. Supervising is a permission on a module, held through the supervisor, KYC or any custom group. |
| 2 | Tablets and phones are in scope for field work (section 3.2). |
| 3 | No printed forms in Phase A. |
| 4 | Every account requires a supervised specimen signature from each person who can sign on it. |
| 5 | Approvals do not notify anyone outside the app. |
| 6 | Changes to role groups require supervision, for seeded and custom groups alike. |
| 7 | One staff member can hold different role groups in different branches. |
| 8 | The product name is iziBank. |
| 9 | The palette in section 13 is approved. |
| 10 | No logo for now. The wordmark stays text-only until one is available. |
| 11 | KYC verification and supervision are one action by a KYC officer or supervisor who did not create or last change the customer. |
| 12 | The approver sees a field-by-field view of changes since the last approval. |

### 12.2 Open
None for this phase.

## 13. Visual direction (proposed)

**Status: approved.** Tokens are in `iziBank_tokens.css`.

**Idea:** a steady, deep teal carries the brand, and a muted gold accent nods to Ghana's gold without copying national colours. Status is never shown by colour alone (UX 5.1), so the palette can stay calm.

### 13.1 Brand and neutrals

| Token | Hex | Use |
|---|---|---|
| `--ib-primary` | `#0F4C5C` | Primary buttons, active navigation, links, headings on white |
| `--ib-primary-hover` | `#0B3A47` | Hover and pressed states |
| `--ib-primary-tint` | `#E3F0F2` | Selected rows, active nav background |
| `--ib-accent` | `#E0A526` | Highlights, approvals badge fill (text on it is ink, never white) |
| `--ib-accent-tint` | `#FBF1D9` | Banners that need attention without being an error |
| `--ib-ink` | `#1B2631` | Body text |
| `--ib-ink-muted` | `#3D4B57` | Secondary text, labels |
| `--ib-ink-meta` | `#5C6975` | Timestamps, helper text (use only where not essential) |
| `--ib-line` | `#CBD3DA` | Decorative dividers and card edges only. Too light for control borders. |
| `--ib-border-control` | `#73818B` | Borders of inputs, selects, checkboxes and outlined buttons (measured at 4.0:1 on white, 3.6:1 on the page background) |
| `--ib-surface-subtle` | `#F1F4F7` | Table headers, panels |
| `--ib-surface` | `#FFFFFF` | Cards, forms |
| `--ib-page` | `#F6F8FA` | Page background |
| `--ib-focus` | `#1E4FA8` | Keyboard focus ring, 2px, offset 2px |

Focus uses blue rather than gold because gold does not have enough contrast on white.

### 13.2 Status colours (always with an icon and a text label)

| Status family | Text | Tinted background | Used for |
|---|---|---|---|
| Success | `#1E6B42` | `#E4F3EA` | Verified, Active, Open, Approved |
| Warning | `#8A5300` | `#FDF1DC` | KYC pending, Awaiting approval, Expired |
| Danger | `#A3271D` | `#FBE9E7` | Rejected, Frozen, errors |
| Info | `#1E4FA8` | `#E6EEFB` | EOD running, informational banners |
| Neutral | `#3D4B57` | `#EDF1F4` | Dormant, Closed, Business day closed |

Contrast was calculated for every text and background pairing in this palette: all pass 4.5:1, the lowest being 5.1:1 (muted-meta text on the subtle surface). The focus ring is 7.2:1 or better. The only failure was the original divider colour as a control border (1.5:1, needs 3:1), which is why `--ib-border-control` was added. The frontend build re-checks these pairs on every commit (plan task F1-04).

### 13.3 Typography
- **Interface font:** Inter, free and built for dense screens. Money and account numbers use tabular figures so columns line up.
- **Scale:** 14px body, 13px table text and labels, 20px to 24px page titles. Minimum 16px on tablet and phone inputs to avoid zoom on iOS.

### 13.4 Notes
- No dark mode in Phase A.
- Gold is used for fills and highlights only. It is never used for text or thin lines on white.
- No logo yet. "iziBank" appears as a text wordmark in the primary colour. Replace it with a logo file when one exists.
