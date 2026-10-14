# iziBank API

Core banking system for one institution (microfinance and savings-and-loans tier), on one shared SQL Server 2022 database across its branches. New build: no legacy data, no migration, no backward compatibility.

## Read first
1. `docs/plans/iziBank_development_plan.md` — how work is done, order of stages, definition of done.
2. `docs/plans/phase_a_plan_addendum_roles_signatures.md` — wins over the plan where they disagree.
3. `docs/plans/phase_a_implementation_plan.md` — task IDs (A0 to A6) and acceptance criteria.
4. `docs/adr/` — decisions in force. `docs/progress.md` — what is done and verified.
5. `docs/plans/phase_a_ux_design.md` — behaviour the API must support (section 9 lists API requests).

## Commands
```
dotnet tool restore
dotnet build            # warnings are errors
dotnet test             # integration tests need Docker (Testcontainers SQL Server 2022)
dotnet ef migrations add <Name> -p src/Banking.Infrastructure -s src/Banking.Migrator
dotnet run --project src/Banking.Migrator   # apply schema and seed
```

## Rules that never bend
- Ledger tables (`UserActivityLog`, `DataChangeAuditLog`, later transaction ledgers) are append-only. Never update or delete. Corrections are compensating entries.
- KYC verification and supervision are ONE action (verify = approve). Four-eyes: the creator or last modifier cannot approve.
- Permissions come from role groups assigned per branch (`RoleGroup`, `RoleGroupPermission`, `StaffUserRoleGroup`). Union only, no deny rows. Every role-group change needs supervision. Never reintroduce per-user permission rows.
- Every account needs an effective (supervised) specimen signature from every person who can sign on it.
- Every endpoint has `[RequirePermission]`. Deny by default.
- No secrets in the repo. No personal data in logs or URLs.
- Every schema change = migration + DBML update (`docs/schema/core-banking.dbml`) + changelog line. Every API change = regenerated `docs/api/openapi.json`.
- Business rules live in Domain/Application. Controllers only translate HTTP.

## Working with the other Claude session
A cloud Claude session plans, designs, builds the Angular portal and writes code into this folder. This local session builds, tests and runs migrations. Before editing, check `git status` and `docs/progress.md`; do not rewrite work in progress from the other session without saying so. Record decisions in `docs/adr/open-decisions.md`.

## Commits
One logical commit per task ID, message `A2-03: short description`.
