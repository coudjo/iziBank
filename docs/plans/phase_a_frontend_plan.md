# iziBank Staff App: Phase A Frontend Plan

**Audience:** a coding agent. Follow this document top to bottom. Every task has an ID and acceptance criteria.
**Inputs:** `phase_a_ux_design.md` (behavior and flows), `phase_a_implementation_plan.md` and `phase_a_plan_addendum_roles_signatures.md` (API behavior), `iziBank_tokens.css` (approved palette), the OpenAPI document at `docs/api/openapi.json`.
**Where documents disagree:** the addendum beats the plan, and the UX design beats this document on screen behavior. This document only decides how to build it.
**Sequencing:** the shell and the customer onboarding flow are specified in full (F0 to F5). Later screens are outlined (F6 to F12) and will get their own detail when F5 is accepted.

---

## 0. Working agreement

1. Work in task order. One logical commit group per task ID, with tests.
2. The app talks only to the documented API. Never invent an endpoint. If something is missing, add it to `docs/api-requests.md` (section 11 is the starting list) and build against a mock until it exists.
3. Every route has a permission rule. Every button that needs a permission uses the permission directive (6.3). Deny by default.
4. No personal data and no secrets in: URLs, page titles, logs, analytics, local storage, IndexedDB, cookies. Customer data lives in memory only.
5. No third-party scripts, fonts or analytics loaded from the internet. Everything is bundled and self-hosted.
6. Text, labels and error messages come from the UX design wording. Do not paraphrase them.
7. When something is ambiguous, use the default in section 12 and record it in `docs/adr/open-decisions.md`.
8. A task is done only when its acceptance criteria pass in CI at all three test viewports: desktop 1366x768, tablet 820x1180, phone 390x844.

---

## 1. Scope

| In this plan | Screens |
|---|---|
| Detailed (F0 to F5) | Sign in, change password, session handling, app shell, customers list and search, global search, individual onboarding, customer record header, overview and details |
| Outlined (F6 to F12) | Business and group onboarding, KYC, documents, photo, signature, next of kin, beneficial owners, members, approvals, accounts, products, chart of accounts, administration, audit, hardening |

Out of scope: customer-facing screens, printing, loans, transactions, notifications, offline writes, dark mode, a logo (the wordmark "iziBank" is text, in the primary colour).

---

## 2. Technology decisions

| # | Topic | Decision |
|---|---|---|
| T1 | Framework | Angular 22, the current stable version and the one NG-ZORRO 22 supports (NG-ZORRO keeps the same major version as Angular). Pin exact versions at kickoff in `package.json`. Standalone components, signals for state, `OnPush`, built-in control flow (`@if`, `@for`). NG-ZORRO supports zoneless and `OnPush`, so do not rely on Zone.js-specific behavior. Strict TypeScript and strict templates. |
| T2 | UI components | NG-ZORRO (`ng-zorro-antd`, Ant Design for Angular), chosen by the owner. Themed from the tokens. Every NG-ZORRO component used is wrapped in an `ib-` component, and features never import NG-ZORRO directly, so the library can be replaced later. Import components individually as standalone imports. Angular CDK (also used by NG-ZORRO) provides breakpoints and any custom overlays. Usage rules and the accessibility gate are in 4.2.1. |
| T3 | Styling | SCSS plus CSS custom properties from `iziBank_tokens.css`. No utility-class framework. |
| T4 | Fonts | Inter, self-hosted as woff2, with tabular figures enabled for numbers. |
| T5 | State | Signals inside feature services. No NgRx in Phase A. |
| T6 | HTTP | `HttpClient` with functional interceptors. Types generated from `docs/api/openapi.json` with `openapi-typescript` (types only). A thin typed client per feature wraps the calls. The generated types are committed, and CI fails if they are out of date. |
| T7 | Forms | Typed reactive forms. |
| T8 | Dates and numbers | `date` values are strings (`yyyy-MM-dd`) end to end. Never convert them through `Date` objects (time zone shifts). Timestamps are ISO UTC strings, displayed with `en-GH` formats. `date-fns` for age and range calculations. Money is displayed with `Intl.NumberFormat('en-GH')`, currency `GHS` shown as `GH₵`. |
| T9 | Phone | `libphonenumber-js` (minimal metadata), Ghana as the default country. |
| T10 | Runtime config | `assets/config.json` loaded before bootstrap (API base URL, idle and refresh timings). One build deploys anywhere. |
| T11 | Hosting | Static files from an nginx container, same origin as the API behind a reverse proxy (`/` app, `/api` API). No CORS in production. |
| T12 | Security headers | Set by nginx: strict `Content-Security-Policy` (no inline scripts, `object-src 'none'`, `frame-ancestors 'none'`, `frame-src blob:` for the document viewer), `Referrer-Policy: no-referrer`, `X-Content-Type-Options: nosniff`, `Permissions-Policy: camera=(self)`. |
| T13 | Token storage | Accepted by the owner. The access token is kept in `sessionStorage` (cleared when the tab closes) so a mobile browser reload does not force a new sign-in. Only the token, its expiry and the session id are stored, never personal data. The XSS trade-off is covered by T12. An httpOnly cookie would be a stronger option later, but needs an API change. |
| T14 | Linting | `angular-eslint` with the template accessibility rules enabled, Prettier. Warnings fail CI. |
| T15 | Unit tests | The Angular CLI's default unit test runner, with Testing Library for components. |
| T16 | End-to-end | Playwright at the three viewports, with `axe-core` checks on every page. |
| T17 | API mocks | Mock Service Worker (MSW), handlers built from the OpenAPI examples. Used for development and for tests. A second CI job runs the same end-to-end specs against the real API container once it exists. |
| T18 | Language | English only. `en-GH` locale registered for Angular pipes, and the `en_GB` pack for NG-ZORRO's own texts (date picker, pagination, empty states). Keep strings in templates and a single message catalog so translation stays possible. |

---

## 3. Architecture

```
izibank-web/
  src/app/
    core/        config, auth, session, permissions, branch context, working date,
                 http (interceptors), errors, activity tracking, formats
    shared/      ui/ (ib-* components), directives/, pipes/, forms/, capture/, utils/
    layout/      shell, top bar, sidebar, bottom bar, search overlay
    features/    dashboard, customers, accounts, approvals, products,
                 ledger-accounts, admin, audit   (each lazy loaded)
    styles/      tokens.css, theme.scss, typography.scss
  src/mocks/     MSW handlers
  e2e/           Playwright specs
  docs/          adr/, api-requests.md
  docker/        nginx.conf, Dockerfile
```

Rules:
1. `features` may import `core` and `shared`. A feature never imports from another feature, except through that feature's `index.ts` public API.
2. Components are presentational where possible. Features keep their logic in services with signals.
3. Every feature is a lazy route. Route data carries `title` and `permission`.
4. API calls live only in feature clients and `core`. Components never call `HttpClient`.

### 3.1 Routes and permissions
Route data: `{ title, permission: { module, subModule?, action } }`. Actions are `view`, `add`, `edit`, `delete`, `supervise`.
- A `canMatch` guard blocks routes without the permission, so lazy chunks are not even downloaded.
- The sidebar and the phone bottom bar are generated from the route table, filtered by permission. A section with no visible children is hidden.
- A `TitleStrategy` sets titles to "Customers - iziBank". Titles never include names or numbers.
- On every navigation, focus moves to the page `h1`, and the route change is announced to screen readers.

---

## 4. Design system

### 4.1 Tokens and theme
`iziBank_tokens.css` is the source. Map the same values onto the NG-ZORRO theme (brand and status colours, text colours, control border, border radius, control height, base font and size 14px). Do not hard-code colours or spacing in components. Required tokens beyond the file: spacing scale (4, 8, 12, 16, 24, 32), radius (4, 8), elevation (none, card, overlay).

Control borders use `--ib-border-control` (3:1 or better). `--ib-line` is for decorative dividers only. NG-ZORRO's default control colours must be overridden to these tokens, and the result checked by the contrast script (F1-04).

### 4.2 Components to build (`shared/ui`)

| Component | Notes |
|---|---|
| `ib-button` | Variants: primary, secondary, quiet, danger. Loading state locks the button and keeps its width. Min height 44px on touch. |
| `ib-status-chip` | Icon plus text, always. Map from status code to family (success, warning, danger, info, neutral) in one file, matching UX 5.1. Clickable variant opens an explanation popover. |
| `ib-form-field` | Label above, hint, error, required marker. Wires `aria-describedby` and `aria-invalid`. |
| `ib-error-summary` | List of problems at the top of a form. Each item focuses its field. |
| `ib-dialog` | Confirm, reason-required and typed-confirmation variants. Focus trap, `Esc` closes, returns focus. |
| `ib-toast` and `ib-banner` | Toast auto-dismisses after 5 seconds, announced politely. Banner persists. |
| `ib-data-table` | Server paging, sort, filter slot. Below 768 it renders each row as a card using a supplied template. |
| `ib-skeleton`, `ib-empty-state` | For loading and empty lists, per UX 5.4. |
| `ib-masked-value` | Shows a masked value with a Show control. Reveals for 30 seconds, then hides again. Announces changes. |
| `ib-page-header`, `ib-tabs` | Header with breadcrumb, title, chips and actions. Tabs are keyboard-operable and route-aware. |
| `ib-stepper` | Desktop and tablet: horizontal steps. Phone: one step per screen with a sticky back and next bar. |
| `ib-date-input`, `ib-phone-input`, `ib-lookup-select`, `ib-combobox` | Form controls with the formats in 6.7. |

A dev-only gallery at `/dev/components` shows every component in every state. It is excluded from production builds.

### 4.2.1 NG-ZORRO usage rules and accessibility gate

| `ib-` component | Built on |
|---|---|
| `ib-button` | `nz-button` |
| `ib-form-field` | `nz-form` |
| `ib-dialog` | `nz-modal` |
| `ib-toast` | `nz-message` and `nz-notification` |
| `ib-banner` | `nz-alert` |
| `ib-data-table` | `nz-table` |
| `ib-skeleton`, `ib-empty-state` | `nz-skeleton`, `nz-empty` |
| `ib-tabs` | `nz-tabs` |
| `ib-stepper` | `nz-steps` (desktop and tablet) |
| `ib-status-chip` | `nz-tag` with an icon |
| `ib-date-input` | `nz-date-picker` |
| `ib-lookup-select`, `ib-combobox` | `nz-select` |
| `ib-phone-input`, `ib-masked-value`, `ib-image-capture`, `ib-signature-pad` | Custom, using `nz-input` where applicable |

Rules:
1. Register only the icons that are used, statically in code. No dynamic icon loading, so the app makes no extra runtime requests and the bundle budget holds.
2. Set the NG-ZORRO locale to `en_GB`. Format dates with our own formats (6.7), not the picker defaults.
3. Ant Design defaults are tuned for dense desktop screens. On tablet and phone, controls must be at least 44px high. Set this through the theme, and verify it in the gallery at all three viewports.
4. Follow the theme customization guide for the pinned version (https://ng.ant.design/docs/customize-theme/en). Record the chosen mechanism in an ADR in F1-01.
5. **Accessibility gate.** The UX target is WCAG 2.2 AA. Do not assume any NG-ZORRO component meets it. For every wrapped component, F1-02 runs: a keyboard-only walk-through, a screen reader check of the name, role and state, an `axe` scan, and a 200% zoom check. A component that fails is fixed in its wrapper. If it cannot be fixed there, it is replaced with a CDK-based implementation. Remaining failures are listed in `docs/a11y-exceptions.md`, which must be empty before release.

### 4.3 Responsive rules
Use CDK `BreakpointObserver` through one `Viewport` service exposing a `size` signal (`desktop`, `tablet`, `phone`).

| Size | Layout |
|---|---|
| Desktop (1280 and up) | Sidebar with labels, multi-column forms, tables |
| Tablet (768 to 1279) | Icon sidebar, single-column forms, tables become cards when they would scroll sideways |
| Phone (below 768) | Bottom bar (Customers, Accounts, Approvals with badge, More), search at the top of each page, stacked layouts |

Desktop-preferred screens (UX 3.2) show a notice on phones with a Continue anyway option.

### 4.4 Accessibility rules
- Target WCAG 2.2 AA. Status is never colour alone. Touch targets 44px on tablet and phone, never below 24px anywhere.
- Skip link first on every page. One `h1` per page. Landmarks for header, nav, main.
- Toasts and errors use live regions. Reduced-motion preference is respected.
- Form errors follow the summary pattern in UX 5.3.

---

## 5. Core services (`core/`)

| Service | Responsibility |
|---|---|
| `ConfigService` | Loads `config.json` before bootstrap. |
| `AuthService` | Login, refresh, logout, change password. Holds the session in a signal and in `sessionStorage` (T13). |
| `SessionMonitor` | Timers and idle logic (6.1). |
| `ActivityTracker` | Notes the time of the last pointer, key or touch event. Throttled. Used by `SessionMonitor` only. |
| `PermissionService` | Loads effective permissions after login and on branch switch. Exposes `can(module, subModule, action)` and `canSupervise(recordType)` as signals. Unknown means false. |
| `BranchContext` | Current branch, available branches, switch action. Adds `X-Branch-Id` only when the user switched away from their own branch. |
| `WorkingDateService` | Reads the open business day for the branch. Refreshes at load, on branch switch and every 5 minutes. |
| `ErrorCatalog` | Maps problem types to user messages (6.2). |
| `Formats` | Phone, date, money and masking helpers (6.7). |
| `BroadcastService` | Cross-tab events (sign-out). |

---

## 6. Cross-cutting behavior

### 6.1 Session handling (UX 5.8)
- **Silent renewal:** when less than 5 minutes remain and the user was active in the last 60 seconds, call `POST /auth/refresh`.
- **Idle warning:** 2 minutes before the token expires, with no recent activity, show the dialog "You will be signed out in 2:00" with Stay signed in and Sign out. Staying signed in calls refresh.
- **Maximum length:** show the notice 10 minutes before the maximum session time (needs `sessionMaxExpiresAt`, section 11).
- **Session ended mid-task:** on a 401 with a session problem type, open the re-sign-in dialog over the page. Do not navigate. In-memory form data survives.
- **After re-sign-in:** replay the failed request only if it was a GET. Never replay a write. The user presses the button again. If the signed-in user is a different person, clear all in-memory stores, go to the dashboard and show "Your previous work was discarded because a different user signed in."
- **Reasons:** map session problem types to the sign-in page message (superseded, forced, timed out, maximum length).
- **Cross-tab:** sign-out in one tab signs out all tabs through `BroadcastChannel`.
- **Forced password change:** while `mustChangeSecretKey` is true, a guard allows only the change-password and sign-out routes.

### 6.2 HTTP and errors
Interceptors, in order: base URL and headers (token, `X-Branch-Id`), idempotency key, error mapping.

| Status | Behavior |
|---|---|
| 401 | Session handling (6.1). Bad credentials on the login screen show the generic message. |
| 403 | Toast "You do not have permission to do that." The activity is already logged by the API. |
| 404 | Not-found page for routes, inline message for lookups. |
| 409 | Look up `type` in `ErrorCatalog`. Show the message in context (inline, banner or dialog, as the screen decides). |
| 400 | Map `errors` to form fields (6.5). |
| 5xx or network | Message "Something went wrong. Reference: {traceId}" for 5xx. Network failures follow the "Check before retry" flow in UX 3.1. |

`ErrorCatalog` holds one message per problem `type` from `docs/api/problem-types.md`. A unit test fails if the API catalog has a code without a message here, or the reverse.

### 6.3 Permissions in the UI
- `*ibCan="{ module: 'PTY', subModule: 'PARTY', action: 'add' }"` removes the element when denied.
- `*ibCanSupervise="'party'"` for approve controls.
- A disabled control is used only when the user has the permission but the state forbids the action (for example, four-eyes), with the reason in a tooltip and in text for touch users.
- Permission data is never used for security. The API enforces everything. The UI only avoids offering actions that would fail.

### 6.4 Branch and working date
The shell shows the branch and the working date. If no day is open, it shows the warning chip, and the account-opening entry points are disabled with the reason (account screens come later, but the signal is built now).

### 6.5 Forms
- Typed reactive forms, validate on blur and on submit.
- `FormErrorMapper` converts a problem `errors` map to control errors. Unknown field names go to the error summary.
- `SubmitGuard`: a signal plus `exhaustMap` so a form cannot submit twice.
- `unsavedChangesGuard` on routes with forms.
- Multi-step flows keep state in a route-scoped service (in memory), so steps and the re-sign-in dialog do not lose data.

### 6.6 Uploads and capture (`shared/capture`)
- **`ib-image-capture`:** device camera through `<input type="file" accept="image/*" capture>` with a gallery option. On capture: fix orientation, downscale so the longest side is at most 2000 px, re-encode as JPEG, lowering quality from 0.85 toward 0.5 until the file is under 2 MB. PDFs pass through and must be 5 MB or less. The result stays in memory as a `Blob`. Revoke object URLs when finished.
- **`ib-signature-pad`:** full-width canvas using pointer events (finger, stylus, mouse), scaled for device pixel ratio, Clear and Accept. Accept requires ticking "The customer has signed in front of me". Output: PNG, at most 1 MB, at most 2000 x 1000 px.
- **`UploadService`:** progress, cancel, and retry with the **same** `Idempotency-Key`. The key is created when the file is captured, not when the request is sent.
- Nothing is written to device storage. If the camera is denied, fall back to the file picker with an explanation.

### 6.7 Formats
| Item | Rule |
|---|---|
| Date display | `dd/MM/yyyy` |
| Date input | Typed or picked. Sent as `yyyy-MM-dd`. |
| Phone | Accepts `024 123 4567` and `+233 24 123 4567`. Normalized to E.164 on blur. Shown in national format. |
| Money | `GH₵ 1,234.50`, tabular figures, right aligned. |
| IDs | Trimmed, uppercased on blur for identification numbers. |
| Masking | Last four characters visible. |

---

## 7. Screen specifications: sign in to individual onboarding

### 7.1 Sign in (`/login`)
- Fields: staff code, password. Show/hide password control.
- Errors: generic invalid-credentials message, locked message with the minutes from `retryAfterSeconds`, session-ended reasons from 6.1.
- After success: if `mustChangeSecretKey`, go to `/change-password`. If the user has several branches, show a branch picker, then the dashboard.

### 7.2 Change password (`/change-password`)
Current password, new password, confirm. A live checklist shows the rules (minimum length, upper, lower, digit, different from the staff code). Submit is disabled until all pass.

### 7.3 Customers list and global search
- `/customers`: search box, filters (type, KYC status, approval state, credit officer), `ib-data-table`, columns as in UX 7.2. Primary action **New customer** (menu: individual, business, group). Each menu item appears only with add permission.
- Global search overlay (`/` or `Ctrl+K`, a search icon on phones): one query across customers and accounts, results grouped, each with type, number and status chips. Debounced 250 ms, minimum 2 characters. ID numbers in results are masked. A result opens the record.

### 7.4 Individual onboarding (`/customers/new/individual`)
Permission: PTY / PARTY / add. Three steps. State is held in a route-scoped service.

**Step 1: Identity**

| Field | Control | Rules |
|---|---|---|
| Title | Select (Mr, Mrs, Miss, Ms, Dr, Prof, Rev), optional | max 10 |
| First name | Text | Required, max 60 |
| Last name | Text | Required, max 60 |
| Gender | Lookup select (`GenderType`) | Required |
| Date of birth | Date input | Required, not in the future, age 18 or more (the minimum age is a placeholder from the plan, kept in config) |
| Nationality | Searchable select (list in `shared/data/nationalities.ts`, Ghanaian first). No free text. | Required, max 50 |
| ID type | Lookup select (`IdentificationDocumentType`, individual types only) | Required |
| ID number | Text | Required, max 30, trimmed, uppercased. A hint shows the expected format for the chosen type. Format enforcement waits for the compliance decision. |

**Step 2: Contact**

| Field | Control | Rules |
|---|---|---|
| Mobile | Phone input | Required, valid Ghana or international number |
| Phone | Phone input | Optional |
| Email | Email | Optional, valid, max 100 |
| Address line 1 | Text | Required, max 200 |
| Address line 2 | Text | Optional, max 200 |

**Step 3: Funds and review**

| Field | Control | Rules |
|---|---|---|
| Source of funds | Combobox (Salary, Business income, Farming, Trading, Remittances, Savings, Pension, Other, free text allowed) | Required, max 200 |
| Occupation or sector | Text | Required, max 100 |
| Politically exposed person | Toggle, default No, with helper text | |

A review panel summarizes all steps, with an **Edit** link per section. ID number is masked in the review.

**Submit** calls `POST /parties/individuals`.

| Outcome | Behavior |
|---|---|
| 201 | Go to `/customers/:id` with the banner "Saved. This customer is awaiting approval by a supervisor." and the Complete the profile card. |
| 409 duplicate ID | Stay on step 1. Show the panel "A customer with this ID already exists: {name} ({number})" with **Open that customer**. Do not clear the form. |
| 400 | Map errors to fields. Jump to the first step that has an error. |
| 401 | Re-sign-in dialog. Form data kept. After signing in, the user presses Create again. |
| Network failure | UX 3.1: **Check now** queries the duplicate check with the same ID. If it finds a customer, offer to open it. Otherwise offer Retry. |

Phone layout: one step per screen, sticky bar with Back and Next, the step title in the header. Leaving the flow with entered data asks for confirmation.

### 7.5 Customer record: header, overview, details (`/customers/:id`)
- **Header:** display name, customer number, type chip, KYC chip, approval chip, PEP flag, credit officer, action buttons (Edit, Approve, Verify and approve as permission and state allow).
- **Tabs:** scaffold all tabs from UX 7.3. Overview and Details are built in this task. Others show a "coming" placeholder only in development builds and are hidden in production until built.
- **Overview:** the Complete the profile card from the overview endpoint (checklist items done or missing, each linking to its tab), and accounts held (empty until accounts exist).
- **Details:** read only by default. ID number, tax number and mobile use `ib-masked-value`. **Edit** switches to the form from 7.4. Saving a controlled field on an approved customer first shows the warning from UX 5.2.
- Opening this page is a sensitive read: if the API fails the request because the activity log could not be written (a fail-closed 500), show "We could not open this record safely. Try again." and nothing else.

---

## 8. Remaining screens (outline)

| Task | Screens | Reference |
|---|---|---|
| F6 | Business and group onboarding, beneficial owners, group members, inline create-individual from pickers | UX 6.3, 6.4 |
| F7 | KYC tab and verify-and-approve screen with the split document viewer, documents, photo, signature, next of kin, regulatory attributes, activity tab | UX 6.5, 7.3 |
| F8 | Approvals inbox, review page, field-by-field changes panel, approvals badge | UX 6.6 |
| F9 | Accounts: list, open, record, holds and freeze, close, dormancy, classification, reminders | UX 6.7, 6.8, 7.4 |
| F10 | Products with the rates timeline, chart of accounts | UX 6.9 |
| F11 | Administration: staff, role groups with the permissions matrix, sessions, institution, branches, currencies, holidays, business day, lookups, credit officers. Audit viewers. | UX 6.10 to 6.12 |
| F12 | Hardening: accessibility audit, performance budget, security review, usability fixes | UX 11 |

PDF documents in the viewer (F7): open in a sandboxed `iframe` using a blob URL, never a direct link. Revoke the URL when the viewer closes.

---

## 9. Work breakdown (F0 to F5 in detail)

### F0 Scaffolding
| ID | Task | Acceptance criteria |
|---|---|---|
| F0-01 | Create the workspace, strict TypeScript, ESLint with template accessibility rules, Prettier, EditorConfig. | Lint and build pass with zero warnings. |
| F0-02 | Runtime `config.json` loading before bootstrap. | App starts with a config value read from the file in a test. |
| F0-03 | MSW wired for development and tests, with handlers generated from OpenAPI examples. Generated types committed (T6). | The app runs fully on mocks. CI fails when generated types are stale. |
| F0-04 | Playwright with the three viewports and an `axe` helper. | A smoke test runs at all three viewports. |
| F0-05 | nginx image with the headers in T12, and a `docker-compose` file with the API once available. | `curl -I` shows every required header. |
| F0-06 | CI steps in `docs/ci.md`: install, lint, type check, unit tests, build, e2e, contrast check (F1-04). | All steps pass on an empty feature. |

### F1 Design system
| ID | Task | Acceptance criteria |
|---|---|---|
| F1-01 | Token import, NG-ZORRO theme from tokens (mechanism recorded in an ADR), self-hosted Inter, static icon registration, 44px touch control height, spacing and elevation scales. | No hard-coded colours outside the token files (lint rule). Controls are 44px high on tablet and phone. |
| F1-02 | Build every component in 4.2 with all states, and run the accessibility gate in 4.2.1 on each. | The `/dev/components` gallery renders all of them at three viewports, with zero axe violations. Every component has a recorded keyboard and screen reader result. `docs/a11y-exceptions.md` exists. |
| F1-03 | `Viewport` service and the table-to-card behavior. | Table becomes cards at tablet and phone widths in a test. |
| F1-04 | `check-contrast` script: reads the token pairs in `contrast-pairs.json` and fails when text pairs are below 4.5:1 or control and focus pairs below 3:1. | CI fails if a token changes and breaks a pair. The pairs and thresholds match the table in the UX design section 13. |
| F1-05 | Status mapping file for chips (UX 5.1), including icons. | Each status renders icon plus text. A test ensures no status maps to colour alone. |

### F2 Core: auth, session, permissions
| ID | Task | Acceptance criteria |
|---|---|---|
| F2-01 | Interceptors: token and branch header, idempotency key, problem details mapping. `ErrorCatalog` with a coverage test against `docs/api/problem-types.md`. | Test fails for a code with no message. |
| F2-02 | `AuthService`, login, change password, logout, `sessionStorage` use (T13). | Token never appears in `localStorage` or IndexedDB (test). Only token, expiry and session id are stored. |
| F2-03 | `SessionMonitor` and `ActivityTracker` (6.1). | With fake timers: silent renewal, idle dialog at 2 minutes, maximum-session notice at 10 minutes, all as in 6.1. |
| F2-04 | Re-sign-in dialog and replay rules. | A failed write is not replayed. A failed GET is replayed once. A different user clears all in-memory stores. |
| F2-05 | Cross-tab sign-out and forced password change guard. | Sign out in one tab signs out a second tab in the e2e test. Forced-change users cannot reach other routes. |
| F2-06 | `PermissionService`, `*ibCan`, `*ibCanSupervise`, `canMatch` guard. | Three permission sets (teller, KYC officer, administrator) produce the expected routes and buttons. Unknown permission denies. |
| F2-07 | `BranchContext` and `WorkingDateService`. | Branch switch changes the header and reloads permissions. Header sent only when the branch was switched. |

### F3 Shell and navigation
| ID | Task | Acceptance criteria |
|---|---|---|
| F3-01 | Top bar, sidebar (desktop and tablet), bottom bar (phone), skip link, user menu. | Layout matches UX 4.1 at three viewports. |
| F3-02 | Route table with permission metadata, generated navigation, `TitleStrategy`, focus management. | A user without a permission does not see the item, and typing the URL shows the not-allowed page without downloading the lazy chunk. |
| F3-03 | Branch and working-date indicator and the "Business day not open" chip. | Chip appears when no day is open. |
| F3-04 | Global search overlay (UX 4.1), keyboard shortcuts `/`, `Ctrl+K`, `?` help overlay. | Opens from the keyboard and from the phone icon. Results grouped. IDs masked. |
| F3-05 | Approvals badge (reads the count endpoint when present, hidden otherwise). | Visible only to users with a supervise permission. |

### F4 Shared form, upload and capture infrastructure
| ID | Task | Acceptance criteria |
|---|---|---|
| F4-01 | `FormErrorMapper`, `ib-error-summary`, `SubmitGuard`, `unsavedChangesGuard`. | Server errors land on fields. A second click cannot submit twice. |
| F4-02 | Formats: phone, date, money, masking helpers and the `ib-*` inputs. | Phone inputs normalize `024...` and `+233...` to the same E.164 value. Dates never pass through `Date`. |
| F4-03 | `ib-image-capture` with compression (6.6). | A 6 MB photo becomes 2 MB or less, longest side 2000 px or less, orientation correct. PDFs over 5 MB are rejected with the UX message. |
| F4-04 | `ib-signature-pad` (6.6). | Output is PNG within the size limits. Accept stays disabled until the confirmation box is ticked. Works with touch and mouse. |
| F4-05 | `UploadService` with progress, cancel, same-key retry. | Retry sends the same `Idempotency-Key`. Cancel stops the request. |
| F4-06 | `ib-masked-value`. | Reveals for 30 seconds, then masks again. State is announced. |

### F5 Customers: list, search, individual onboarding, record basics
| ID | Task | Acceptance criteria |
|---|---|---|
| F5-01 | Customers list with search, filters, paging, card mode (7.3). | Filters survive navigation within a session. IDs masked. Empty state offers the next action. |
| F5-02 | Individual onboarding steps 1 to 3 with the route-scoped state service (7.4). | Every rule in the field tables is enforced. Back and next keep entered data. Phone layout shows one step per screen. |
| F5-03 | Submit outcomes (7.4 table). | Tests for 201, duplicate 409, 400 mapping, 401 with re-sign-in, and network failure with Check now. |
| F5-04 | Customer record header, tabs scaffold, Overview with the Complete the profile card (7.5). | The card lists each missing item with a working link. |
| F5-05 | Details tab, masked values, edit with the controlled-field warning. | Editing an approved customer's name shows the warning. Cancelling changes nothing. |
| F5-06 | Accessibility and end-to-end pass for the onboarding flow at three viewports. | All scenarios in section 10 pass. Zero axe violations. |

---

## 10. Testing

### 10.1 Layers
- **Unit:** services, formatters, error catalog, permission logic, session timers (fake timers), compression and signature output.
- **Component:** each `ib-*` component and each form, using Testing Library.
- **End-to-end (Playwright, three viewports, axe on every page):**
  1. Sign in, forced password change, land on the dashboard.
  2. Teller, KYC officer and administrator see different navigation.
  3. Idle warning appears and Stay signed in renews the session.
  4. Session ends mid-form: re-sign-in dialog, form data kept, same user continues, different user is discarded.
  5. Individual onboarding happy path to a created customer.
  6. Duplicate ID path.
  7. Server validation error maps to the right field and step.
  8. Network drop during Create: Check now finds the saved customer.
  9. Phone viewport onboarding with one step per screen.
  10. Masked ID reveals and re-masks.
- **Contract:** the same e2e specs run against the real API container when it exists.

### 10.2 Quality gates
- Lint, type check, unit, e2e and contrast check green.
- Zero axe violations on every page at all three viewports.
- Initial JavaScript for the sign-in page under 250 KB gzipped, with each feature lazy loaded (budget enforced in the build).
- No `console` output in tests (fails CI).

---

## 11. API requests from the frontend

Add these to `docs/api-requests.md` and to the Phase A plan. Build against mocks until they exist. These come in addition to the ten requests in UX section 9 and the addendum.

| # | Request | Why |
|---|---|---|
| 1 | Problem details contract: `type` is a stable code. A duplicate ID error carries `existingPartyId`, `existingPartyNumber`, `existingDisplayName`. The lockout error carries `retryAfterSeconds`. Include `traceId`. | 6.2, 7.4 |
| 2 | Login and refresh responses: `accessToken`, `expiresAt`, `sessionStartedAt`, `sessionMaxExpiresAt`, `staffUserId`, `mustChangeSecretKey`, `branchId`. | 6.1 |
| 3 | `GET /auth/me`: profile, available branches, effective permission flags per module and sub-module for each branch, and which record types the user can supervise. | 6.3 |
| 4 | `GET /branches/{id}/business-days/current`: the open day, or an empty result. | 6.4 |
| 5 | Lookup responses include applicability (for example which party type an identification document type applies to). | 7.4 step 1 |
| 6 | `GET /parties/duplicate-check` by identification type and number (add permission required, logged). | 7.4 network failure |
| 7 | The API validates and normalizes phone numbers to E.164. | 6.7 |
| 8 | Resources with a concurrency token return `rowVersion`, and updates must send it. A mismatch returns `concurrency.conflict`. | UX 5.7 |
| 9 | Mutation responses return the full updated resource. | Avoid extra reads |
| 10 | OpenAPI includes examples and string-literal enums for lookup codes, so mocks and types are accurate. | T6, T17 |

---

## 12. Decisions and open questions

### 12.1 Decisions
| # | Decision |
|---|---|
| D1 | Same-origin hosting behind a reverse proxy (T11). |
| D2 | Customer data is in memory only. No drafts are saved in the browser. |
| D3 | Writes are never replayed automatically after a session loss. |
| D4 | The minimum customer age is a configuration value (default 18), pending compliance. |
| D5 | ID number format rules are hints only until compliance confirms them. |
| D6 | The component library is NG-ZORRO, wrapped in `ib-` components (owner's choice). |
| D7 | The access token is kept in `sessionStorage` (owner accepted). |
| D8 | Nationality is chosen from a list only. A missing nationality is added to the list as a data change. |
| D9 | The web app lives in its own repository, `izibank-web`. |

### 12.2 Open questions
None.
