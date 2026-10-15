# Changelog

All notable schema and behaviour changes for this repository.

Format: task ID, then a one-line description.

## Unreleased

- A0-01: Solution layout, SDK pin, central package management, and analyzer defaults.
- A0-02: SQL Server 2022 Docker Compose stack and `.env.example`.
- A0-03: Testcontainers MSSQL fixture, one database per test class, authenticated `HttpClient` helper.
- A0-04: CI restore/build/test and OpenAPI drift check.
- A0-05: Schema snapshot, Phase A decision ADR, and this changelog.
- A1-01: Domain `IAuditable` and `Lookup` base types, `BankingDbContext`, and global EF conventions.
- A1-02: Phase A entities and EF configurations, including section 2 amendments, audit footer, and ledger append-only guard.
- A1-03: Baseline schema migration, append-only ledger tables, and DBML amendments.
- A1-04: Migrator applies schema and idempotent reference/bootstrap seed, including admin from environment variables.
- S1: `UserPermission` replaced by `RoleGroup`, `RoleGroupPermission`, `StaffUserRoleGroup`; added `PartySignature` and `IdempotencyKey`; composite key on `SubModule`; six seeded role groups; bootstrap admin assigned `ADMINISTRATOR` and `SUPERVISOR`; ADR D3 and Q2 updated (KYC verify and approve is one action). Migration `0003`.
- A2-01: `ICurrentUser`, `AuditFooterInterceptor` (Created/Modified On and By; created values immutable).
- A2-02: `DataChangeAuditInterceptor` writes `DataChangeAuditLog` in the same transaction as the change; secrets redacted, binary written as size.
- A2-07: `SupervisionRules` (maker-checker R1 to R5) and the startup guard for `Supervision:AllowSelfApproval`.
- A2-04: JWT login, refresh, logout, change secret key, derived lockout, superseded sessions, per-request session check (30 s cache), `SessionSweeper` for idle and maximum length; `/auth/me`.
- A2-05: `PermissionResolver` (union of approved role groups per branch, 30 s cache), `[RequirePermission]` via an endpoint filter with deny by default, `X-Branch-Id` switching, forced password change gate.
- A2-06: one `UserActivityLog` row per authenticated request; writes join the business transaction; denials and failed logins logged with the attempted staff code; sensitive reads fail closed. Problem codes listed in `docs/api/problem-types.md`.
- Sessions: `GET /sessions`, `POST /sessions/{id}/end` (SEC/SESSIONS).
- A2-09: `ChangeRequest` table (migration `0004`); role-group API (`/modules`, `/role-groups`, `/staff-users/{id}/role-groups`, `/change-requests` with approve and reject). Changes apply only on four-eyes approval; permission cache cleared on approval.
- A2-10: safety rules: no self-assignment, last-administrator guard in the approval transaction, one pending change per record, deactivation removes access on the next request. Role groups are deactivated, never deleted.
- A2-03: lookup API (`/lookups`, `/lookups/{type}` read for any signed-in user; add and edit with REF/LOOKUPS). Codes immutable, coded rows cannot be deactivated, no delete.
- A2-08: staff users (`/staff-users` list, detail, create with forced first change, edit, disable ends sessions, reset password), last-administrator guard on disable, no self-change; audit viewers `/audit/activity` and `/audit/changes` (sensitive reads, logged as AUDIT_VIEW).
- A3-01: Institution profile (read and update only), branches (code immutable), currencies (exactly one local, moved atomically), holidays. New problem codes in `docs/api/problem-types.md`. ADR 0002.
- A3-02: Business day open and close per branch (`/branches/{id}/business-days`), `/business-day/current`, `IWorkingDateProvider`, `Calendar:WeekendDays`. `SUPERVISOR` gets `REF/CALENDAR` supervise for closing.
- A3-03: Chart of accounts (`/ledger-accounts`) with four-eyes approval and reject; renames of approved accounts go through `ChangeRequest`. Staff users gain `isTeller` and `tellerGLAccountId` with the till-account rule. A test now keeps `problem-types.md` in step with `ProblemCodes`.
- A4-01: `POST /parties/individuals` (PTY/PARTY add) and `GET /parties/{id}` (sensitive read). Party numbers from `PartyNumberSequence`, zero padded to `PartyNumber:Length`; display name "First Last"; primary ID stored on `IndividualDetail` and as a `PartyIdentificationDocument`; duplicate ID returns 409 `party.duplicate-identification` with `existingPartyNumber`; minimum age `Kyc:MinimumAgeYears` (placeholder 18).
- A4-02: `POST /parties/businesses` with optional business document and beneficial owners (individuals only, share above 0 and at most 100 with two decimals, total at most 100); `GET/PUT /parties/{id}/beneficial-owners` (replace sends KYC back to PENDING); duplicate registration returns 409 `party.duplicate-registration`.
- A4-03: `POST /parties/groups` with members; `GET/POST /parties/{id}/members`, `POST .../members/{memberId}/leave`, `PUT .../members/{memberId}/role`. One active group per person (409 `group.member-in-other-group`), one chairperson (409 `group.chairperson-exists`), history kept on leave. `GET /parties/{id}` now includes business and group details.
- Fix: `DataChangeAuditInterceptor` writes audit rows with plain parameterised INSERTs. EF batched four or more rows into a MERGE, which the append-only ledger table rejects.
- A4-04: documents (`/parties/{id}/documents` list with masked numbers, detail, add, edit, delete; `.../image` PUT/GET), photo (`/parties/{id}/photo` GET/PUT, `/image`, `/supervise`, `/reject`), upload checks by leading bytes, size and declared type (`upload.*` problem codes), `Idempotency-Key` on document create and photo upload. Every document or photo change sends KYC back to PENDING.
- A4-09, A4-11: specimen signatures (`/parties/{id}/signature` GET/PUT, `/image?pending=`, `/supervise`, `/reject`): PNG only, at most 1 MB and 2000 x 1000 pixels (`upload.dimensions`), four-eyes approval, effective specimen = latest approved, `Idempotency-Key` on capture. `PartySignatures.WithEffectiveSignatureAsync` ready for account opening (rule 3a).
- A4-05: next of kin (`/parties/{id}/next-of-kin` CRUD, individuals only), regulatory attributes (`/parties/{id}/regulatory-attributes` current values, `PUT /{code}` appends a version, `/{code}/history`), credit officers (`/credit-officers` list, designate, disable or enable; `PUT /parties/{id}/credit-officer` assigns an active one or clears it). None of these resets KYC.
- A4-06: `PUT /parties/{id}/individual`, `/business`, `/group`. Controlled fields (names, date of birth, ID type and number, registration number, TIN, group name) send KYC back to PENDING and the record back to Pending approval; other fields change in place. The primary ID document follows the individual's details.
- A4-07: `GET /parties/{id}/kyc/checklist` (per-type `KycRequirementPolicy`), `POST .../kyc/verify` (also approves the record, D3; four-eyes), `POST .../kyc/reject` (reason required). Computed EXPIRED; `GET /parties/{id}` now carries `effectiveKycStatusCode`.
- A4-08: `POST /parties/search` (number or name prefix, exact mobile, exact ID, then name contains; masked ID numbers; eligibility hints) and `GET /parties/{id}/overview`. No schema change.
