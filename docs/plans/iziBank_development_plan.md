# iziBank Development Plan

The working agreement for building iziBank end to end. It says how work is done, in what order, and how each piece is proven. Task detail lives in the plan documents; this file does not repeat it.

## 1. Sources of truth (highest wins)

1. Owner decisions in conversation (recorded in `docs/adr/`).
2. `phase_a_plan_addendum_roles_signatures.md`
3. `phase_a_implementation_plan.md` (backend)
4. `phase_a_ux_design.md` (behaviour) and `phase_a_frontend_plan.md` (build)
5. Existing code. Where it disagrees with 1 to 4, the code is wrong and gets a correction task.

## 2. Repositories and roles

| Repo | Folder on the owner's Mac | Notes |
|---|---|---|
| `iziBank` | `Projects/iziBank` | .NET 10 API, EF Core, SQL Server 2022. Taken over from the earlier agent. |
| `iziBank-portal` | `Projects/iziBank-portal` | Angular 22 + NG-ZORRO staff app. |

## 3. How verification works

The cloud workspace has Node and npm but no .NET, NuGet or Docker. The owner's Mac has them. So:

- **Frontend:** built, linted and unit-tested in the workspace before delivery. Playwright browsers are added when F0-04 is reached.
- **Backend:** code and tests are written in the workspace, delivered to the Mac folder, then the owner runs `dotnet build` and `dotnet test` and pastes failures. Nothing backend is called done until the owner's run is green.
- Every delivery message states exactly what was verified and where, and what was not.

## 4. Working loop (per task ID)

1. Read the task and its acceptance criteria. Check the sources above for conflicts.
2. Write the failing test first where the rule is business logic.
3. Implement the smallest change that passes.
4. Run what can be run here (lint, type check, unit tests, build).
5. Deliver files to the Mac folder. For backend, ask for the build/test run.
6. Update docs in the same change: changelog line, DBML for schema changes, `openapi.json` for API changes, ADR for decisions.
7. Commit one logical group per task ID. Messages: `A2-03: short description`.
8. Mark the task done in `docs/progress.md` only when step 5 is green.

Rules that never bend: ledger is append-only; no secrets in the repo; no personal data in logs, URLs or storage; deny by default on permissions; UX wording is used verbatim.

## 5. Order of work

| Stage | Work | Exit check |
|---|---|---|
| S0 | Frontend scaffold F0 (this step) | Lint, build, unit tests pass; gallery shell runs on mocks |
| S1 | Backend corrections: one-step KYC (verify = supervise), role groups replace `UserPermission`, `PartySignature`, `IdempotencyKey`, DBML typo fixes, ADR and open-decisions updated | Owner's build and tests green |
| S2 | Backend A2 security and audit (auth, sessions, role groups, activity log, change audit, idempotency, problem details) | Green + OpenAPI updated |
| S3 | Frontend F1 to F2 (design system, shell, sign-in, session handling) against mocks, then against real API | e2e at 3 viewports + axe |
| S4 | Backend A3 to A4 Party and KYC; frontend F3 to F5 customers and individual onboarding | Flow works end to end |
| S5 | Backend A5 products, A6 accounts and the rest; frontend F6 to F11 | Per task acceptance |
| S6 | F12 hardening, consolidated plan document, final DBML | Release checklist |

Backend and frontend stages interleave: the frontend builds on mocks generated from the committed OpenAPI file until the real endpoint exists.

## 6. Definition of done

- Acceptance criteria in the plan pass.
- Tests exist for the rule, including the denial and failure paths.
- Zero warnings (warnings are errors in both repos).
- Frontend: passes at 1366x768, 820x1180 and 390x844, with no axe violations.
- Docs updated (changelog, DBML, OpenAPI, ADR as applicable).
- Delivered to the Mac folder, and for backend, confirmed green by the owner.

## 7. Open decisions (defaults in force until the owner changes them)

| # | Question | Default |
|---|---|---|
| Q1 | Products per branch or institution-wide | Institution-wide |
| Q3 | Authority: account `OperatingMode` vs holder `OperatingInstruction` | Account-level |
| Q4 | Individual in more than one group | One active group |
| Q5 | Branch visibility | Institution-wide reads, branch-scoped writes |
| Q6 | BoG KYC fields, ID types, min group size, beneficial-owner threshold | Placeholders in configuration |

Decided: KYC verify and supervise are one action. Role-group changes need supervision. Palette approved, no logo, no dark mode.

## 8. Reporting

- Progress is tracked in `docs/progress.md` in each repo (task ID, state, date, verified-by).
- Updates to the owner are short: what changed, what was verified, what is needed from them (numbered).
- Anything blocked on the owner is listed once, with the default I will use if no answer comes.
