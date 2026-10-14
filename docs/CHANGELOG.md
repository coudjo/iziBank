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
