# Phase A Plan Addendum
## Role-group permissions, specimen signatures, field and mobile needs

**Applies to:** `phase_a_implementation_plan.md`. Where this addendum and the plan disagree, this addendum wins.
**Triggered by:** the owner's answers to the UX questions. Those answers are listed in section 1.

---

## 1. Decisions

| # | Decision |
|---|---|
| R1 | Permissions are granted through **role groups**. Each group holds permissions over modules and sub-modules. |
| R2 | A staff member is assigned one or more role groups **per branch**. Effective permissions for a branch = union of that branch's role groups. |
| R3 | Supervising is a permission (`AllowSupervise`) on a module or sub-module. It is held through the supervisor group, the KYC group or any custom group. The four-eyes rule still applies: a creator or last modifier cannot supervise their own work. |
| R4 | Every module and sub-module is defined in the seed catalog. Role groups are edited against that catalog. |
| R5 | Field and mobile use is in scope. Customer onboarding, documents, photo, specimen signature, group members and approval review work on tablets and phones. |
| R6 | A specimen signature is captured at onboarding and is required before a customer can be a signatory. |
| R7 | No printed forms in Phase A. |
| R8 | Approvals do not notify anyone outside the app. |
| R9 | Product name: iziBank. Logo and colours are pending the visual design step. |
| R10 | Every change to a role group (create, edit the permission matrix, deactivate, change assignments) requires supervision. It takes effect only once approved. Applies to seeded and custom groups alike. |
| R11 | A staff member's role groups are assigned per branch. Example: one person can be a teller in branch 001 and a loan credit officer in branch 002. Confirmed. |
| R13 | KYC verification and supervision are one action (plan D3). A KYC officer verifies a customer and approves the record in the same step. Four-eyes still applies: the verifier must not be the creator or last modifier. |
| R14 | The approver sees a field-by-field view of changes since the last approval. This is required, not optional. |
| R12 | Every account requires specimen signatures from every person who can sign on it. A business or group party does not sign. Its individual signatories do. |

---

## 2. Replacing the per-user permission table

The plan's `UserPermission` table (one row per user, branch, module) is replaced. Per-user permission rows are not kept, because the same grant would have to be repeated for every staff member. Role groups carry the rules, and the assignment table says who holds them where.

### 2.1 New tables

**`RoleGroup`** (lookup-style master, audited)

| Column | Definition |
|---|---|
| `RoleGroupId` | `int IDENTITY` PK |
| `RoleGroupCode` | `varchar(30)` NOT NULL, UNIQUE, immutable after creation |
| `RoleGroupName` | `nvarchar(100)` NOT NULL |
| `Description` | `nvarchar(300)` NULL |
| `IsSystemGroup` | `bit` NOT NULL DEFAULT 0. Seeded groups. They can be edited but not deleted. |
| `IsActive` | `bit` NOT NULL DEFAULT 1 |
| Audit footer | Per plan section 2.3 |

**`RoleGroupPermission`** (the permission matrix)

| Column | Definition |
|---|---|
| `RoleGroupPermissionId` | `bigint IDENTITY` PK |
| `RoleGroupId` | `int` NOT NULL, FK `RoleGroup` |
| `ModuleId` | `int` NOT NULL, FK `Module` |
| `SubModuleId` | `int` NULL, FK `SubModule`. NULL means the whole module. |
| `AllowView` | `bit` NOT NULL DEFAULT 0 |
| `AllowAdd` | `bit` NOT NULL DEFAULT 0 |
| `AllowEdit` | `bit` NOT NULL DEFAULT 0 |
| `AllowDelete` | `bit` NOT NULL DEFAULT 0 |
| `AllowSupervise` | `bit` NOT NULL DEFAULT 0 |

Constraints:
- UNIQUE `(RoleGroupId, ModuleId, SubModuleId)`. SQL Server treats NULLs as equal in a unique constraint, so this allows one whole-module row per group, which is the intended rule.
- CHECK: `SubModuleId` belongs to `ModuleId`. Enforce in the service and by a composite FK `(ModuleId, SubModuleId)` referencing `SubModule (ModuleId, SubModuleId)` with a unique key on that pair. Add the unique key to `SubModule`.
- CHECK: if any of Add, Edit, Delete or Supervise is 1, `AllowView` must be 1.

**`StaffUserRoleGroup`** (assignment, per branch)

| Column | Definition |
|---|---|
| `StaffUserRoleGroupId` | `bigint IDENTITY` PK |
| `StaffUserId` | `bigint` NOT NULL, FK `StaffUser` |
| `RoleGroupId` | `int` NOT NULL, FK `RoleGroup` |
| `BranchId` | `int` NOT NULL, FK `Branch` |
| Audit footer | Per plan section 2.3 |

Constraints: UNIQUE `(StaffUserId, RoleGroupId, BranchId)`. Index `(BranchId, RoleGroupId)` for "who is in this group" queries.

### 2.2 Effective permission rule

For a staff member, branch and module/sub-module pair, each flag is the OR across:
1. the module-level row (`SubModuleId IS NULL`) of every role group the staff member holds in that branch, and
2. the sub-module row of the same role groups, when the request targets a sub-module.

This is a union only. There are no deny rows, so a permission cannot be taken away by a more specific row. Removing access means removing a role group or editing the group. The plan's earlier "most specific row wins" rule (section 5.5) is withdrawn.

### 2.3 Resolution and caching
- `IPermissionResolver.ResolveAsync(staffUserId, branchId)` returns the flat set of `(moduleCode, subModuleCode, flags)`.
- The result is cached per session for at most 30 seconds, and the cache is invalidated when a role group or an assignment changes. Changes take effect on the next request, not at next sign-in.
- The `[RequirePermission]` attribute uses the resolver. The reflection test from section 10.3 still applies: every endpoint must have the attribute.

### 2.4 Safety rules for role groups
1. A role group that is assigned to anyone cannot be deleted. It can be deactivated.
2. Deactivating a role group removes its permissions from all members immediately. The UI shows the member count before this happens.
3. The institution must always have at least one active staff member holding `SEC/USERS` edit, `SEC/PERMISSIONS` edit and `PTY/KYC` supervise. The service refuses any change that would remove the last one. This prevents lockout.
4. A staff member cannot change their own role group assignments. Another administrator must do it.
5. All changes to role groups, the permission matrix and assignments are audited through `DataChangeAuditLog` (the audit trigger in section 5.3 of the plan covers the three new tables).
6. Every change to a role group, including assignment changes, is subject to supervision under plan 5.4 (decision R10). A pending change does not take effect until a different supervisor approves it. The resolver uses approved state only.

### 2.5 Seeded role groups

These replace the presets in the UX document. Each seeded group is a starting matrix. The administrator edits it after install.

| Role group code | Summary of permissions |
|---|---|
| `TELLER` | View and add on `PTY/PARTY`; view and add on `ACC/ACCOUNT`; view on `ACC/CONDITION`; view on `LED/CHART` |
| `FIELD_OFFICER` | View, add and edit on `PTY/PARTY`, `PTY/DOCUMENTS`, `PTY/GROUPS`, `PTY/CREDIT_OFFICERS` (view) |
| `KYC_OFFICER` | View on `PTY`; edit on `PTY/DOCUMENTS`; supervise on `PTY/PARTY`; `PTY/KYC` view and supervise |
| `SUPERVISOR` | View, add, edit and supervise on `PTY` and `ACC`; `ACC/CONDITION` view, edit and supervise; `REF/CALENDAR` view and edit; `LED/CHART` view, add and supervise |
| `ADMINISTRATOR` | View, add, edit and delete on `SEC`, `REF`, `ACC/PRODUCT`, `LED/CHART`; supervise on `ACC/PRODUCT` and `LED/CHART` |
| `AUDITOR` | View on `SEC/AUDIT`, `SEC/SESSIONS`, `SEC/USERS` (read only) |

Note on `KYC_OFFICER`: it supervises party records (answer to question 1 of the UX round). A KYC officer verifies and supervises a customer in one action, unless they created or last changed that customer (four-eyes rule, R13).

---

## 3. Specimen signature

A specimen signature is not an identification document, so it does not go in the `IdentificationDocumentType` lookup. It has its own table.

**`PartySignature`**

| Column | Definition |
|---|---|
| `PartySignatureId` | `bigint IDENTITY` PK |
| `PartyId` | `bigint` NOT NULL, FK `Party` |
| `ImageValue` | `varbinary(max)` NOT NULL (PNG) |
| `CapturedOn` | `datetime2` NOT NULL |
| `CapturedByUserId` | `bigint` NOT NULL, FK `StaffUser` |
| `IsSupervisionRequired` | `bit` NOT NULL DEFAULT 1 |
| `SupervisedByUserId` | `bigint` NULL, FK `StaffUser` |
| `SupervisedOn` | `datetime2` NULL |

Rules:
- Maker-checker applies (plan 5.4). A new signature is pending until a different supervisor approves it.
- **Effective specimen** = the most recent supervised row for the party (highest `SupervisedOn`). A recapture does not replace the effective specimen until the new one is supervised.
- Index `(PartyId, SupervisedOn)`.
- Account opening rule (plan 6.9, rule 3a, added by R12): every person with signing rights on the account must have an effective specimen. A party with only a pending signature is not eligible. Signing rights are defined in 6.9a below.
- Images are never returned in lists. Read only through `GET /parties/{id}/signature/image`.
- Images are redacted from `DataChangeAuditLog` (plan 5.3), the same as `ImageValue` on other tables.

Endpoints:
- `PUT /parties/{id}/signature` (capture, PNG, max 1 MB, `Idempotency-Key` supported)
- `GET /parties/{id}/signature` (metadata and status, with effective flag)
- `GET /parties/{id}/signature/image`
- `POST /parties/{id}/signature/supervise` and `/reject` (follow plan 5.4)

Controlled field: recapturing a signature for a party that already has an effective specimen does not change the party's supervision state. It only changes the signature's own state.

Relationship to KYC:
- A specimen signature is **not** part of Individual KYC verification. It is shown on the customer checklist as required before any account can be opened for that customer.
- Account opening requires it (rule below).

## 4. Field and mobile API needs

| Need | Plan change |
|---|---|
| Camera photos and documents arrive compressed (under 2 MB) | Server limit stays at 5 MB (`Uploads:MaxImageBytes`). Content detection by leading bytes stays as in plan 6.6. |
| Signature PNG | Max 1 MB. Content check: PNG signature bytes and dimensions within 2000 x 1000 pixels. |
| Retries after a dropped mobile connection | `Idempotency-Key` on: `PUT /parties/{id}/photo`, `PUT /parties/{id}/signature`, `POST /parties/{id}/documents`, `POST /accounts`, `POST /accounts/{id}/freeze-actions`. The server stores the key with the result for 24 hours and returns the original result on a repeat. The key table is a small `IdempotencyKey` table (`KeyValue`, `StaffUserId`, `Route`, `ResponseStatus`, `ResponseBody`, `CreatedOn`, `ExpiresOn`). This table is not audited (it holds responses only) and is purged by a job. |
| Tokens during field work | The 15-minute access token is refreshed on app resume. The 12-hour maximum session stays. A device that was asleep for longer than the idle window is signed out, and the sign-in dialog from UX 5.8 appears. |
| Payload size on slow networks | Customer detail and list endpoints return no images. Images load on demand. |
| Shared devices | No change to the server. The UI rule (auto sign-out) is in the UX document. |

The mobile client does not store data on the device. Pending uploads live in memory only. This matches the plan's position on personal data and the UX rule in 5.3.

---

## 5. Changes to the Phase A plan, by section

| Plan section | Change |
|---|---|
| 1.1 In scope | Replace `UserPermission` with `RoleGroup`, `RoleGroupPermission`, `StaffUserRoleGroup`. Add `PartySignature`, `IdempotencyKey`. |
| 2.4 Indexes | Add the constraints in 2.1 of this addendum. |
| 5.5 Permission model | Replace the paragraphs "A `UserPermission` row..." and "A row with `SubModuleId = NULL`..." with section 2.2 of this addendum. Keep the action-to-flag mapping, updated: create = `AllowAdd`, update = `AllowEdit`, delete or reject-pending = `AllowDelete`, approve or verify = `AllowSupervise` (renamed from `CanSupervise`). Keep the branch rule. Remove the sentence on `Limit` columns and replace it with: limits are defined in the transaction phase. |
| 5.4 Maker-checker | Supervision is checked against `AllowSupervise` on the module or sub-module of the record. |
| 6.5 KYC | Verification and supervision are one action (R13). The verifier needs `AllowSupervise` on `PTY/KYC` and must not be the creator or last modifier. If the record is already supervised, the action verifies KYC only. |
| 6.9 Account opening | New rule 3a and definition 6.9a. Rule 3a: every person with signing rights must have an effective, supervised specimen signature, or the account cannot open. 6.9a: signing rights are held by the primary holder of a single-holder individual account; the primary holder, JOINT and SIGNATORY holders of an account with an operating mode; SIGNATORY holders of a business account; and SIGNATORY holders (active group members) of a group account. Business and group parties do not sign. |
| 6.10 Servicing | No change. |
| 8 API | Add: `GET/POST /role-groups`, `GET/PUT /role-groups/{id}`, `PUT /role-groups/{id}/permissions` (replace the matrix), `POST /role-groups/{id}/deactivate`, `GET /role-groups/{id}/members`, `PUT /staff-users/{id}/role-groups` (replace assignments for a branch, body includes `branchId`). Remove `GET/PUT /staff-users/{id}/permissions`. Add the signature endpoints in section 3. Add the Idempotency-Key header where listed in section 4. |
| 9 Work breakdown | Add tasks below. |
| 10.2 Must-have tests | Add: union of role groups across branches; deactivating a group removes access on the next request; last-administrator guard; a group in use cannot be deleted; signatory blocked without a supervised signature; idempotent replay returns the original response. |

### 5.1 New tasks

| ID | Task | Acceptance criteria |
|---|---|---|
| A2-09 | Role groups: entities, migration, seed of the six groups (2.5), resolver with cache, `[RequirePermission]` switched to the resolver, role group API (section 5 of this addendum table above). | Permission union test across two groups and two branches. Cache invalidation test. Seed is idempotent. |
| A2-10 | Safety rules 2.4 (1 to 6), including the last-administrator guard. | Each rule has a test. Lockout test: the guard refuses the change that would leave no administrator. |
| A4-09 | `PartySignature`: capture, supervise, reject, effective-specimen query, image endpoint, audit redaction. | Pending signature does not satisfy a signatory. Recapture keeps the prior effective specimen until supervision. Images never in lists or audit. |
| A4-10 | Idempotency: `IdempotencyKey` table, middleware for the listed routes, 24-hour retention, purge job. | Same key and body returns the same response; same key and different body returns 409 `idempotency.key-reused`. |
| A4-11 | Image and upload validation for signature PNG (size, signature bytes, dimensions). | Oversized, wrong-type and mis-sized files rejected with 400. |
| A6-06 | Mobile API check: run the field flows (customer search, onboarding, photo, signature, group member) against the API using a simulated 3G profile, and record the payload sizes. | Each field flow completes; no list response carries image bytes. Results in `docs/performance.md`. |

---

## 6. Schema and seed changes to make in the DBML

1. Remove `UserPermission`.
2. Add `RoleGroup`, `RoleGroupPermission`, `StaffUserRoleGroup` as in section 2.1, with the audit footer.
3. Add `PartySignature` as in section 3.
4. Add `IdempotencyKey` (not audited, not in the footer).
5. Add to `SubModule`: a unique key on `(ModuleId, SubModuleId)`, so the composite FK in `RoleGroupPermission` works.
6. Add lookup `RoleGroupCode` values to the seed catalog (section 2.5) and the module and sub-module list in plan section 7 stays as it is.
7. Remove `UserPermission` from the table groups in the DBML and place the three role-group tables under B_Security_Audit.

---

## 7. Decisions on the open points

| # | Question | Decision |
|---|---|---|
| A1 | Do changes to seeded role groups need supervision? | Yes. In fact all role group changes do (R10). |
| A2 | Can a KYC officer also be a teller at the same branch? | Yes. Permissions are a union. Maker-checker still blocks self-approval. |
| A3 | Is a specimen signature needed for non-signatory individuals? | No. Every account requires one from each signing person (R12). |
| A4 | Re-require specimen signatures periodically? | No, not in Phase A. |
