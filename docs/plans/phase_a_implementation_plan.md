# Phase A Implementation Plan
## Foundation, Security, Party / KYC, Product and Accounts

**Audience:** a coding agent. Follow this document top to bottom. Every task has an ID and acceptance criteria.
**Schema source of truth:** `docs/schema/core-banking.dbml` (the current DBML) plus the amendments in section 2. Where this plan and the DBML disagree, section 2 wins.
**Stack:** C# / .NET (current LTS, pinned in `global.json`), ASP.NET Core Web API, EF Core, SQL Server 2022 in Docker. The Angular UI is a later phase, but every API here is designed for it.
**New build:** nothing here imports, imitates or stays compatible with any older system. The system goes live empty, so every format and rule below is a design decision, not a compatibility constraint.

---

## 0. Working agreement

1. Work in task order (section 9). One logical commit group per task ID, with its tests.
2. Business rules live in Domain/Application code. Controllers only translate HTTP to service calls. EF configurations only describe mapping.
3. Every endpoint is authorized and permission-checked (section 5.5). The only anonymous endpoint is `POST /api/v1/auth/login`.
4. Every schema change = a migration + an update to `docs/schema/core-banking.dbml` + a line in `docs/CHANGELOG.md`.
5. Do not create tables, endpoints or logic for anything listed as out of scope (section 1.3).
6. Never log or return secrets (secret key hashes, tokens, OTPs, image bytes). Redaction list is in 5.6.
7. When something is ambiguous, use the default documented in section 12. If no default exists, pick the most restrictive behavior and record it in `docs/adr/open-decisions.md`.
8. A task is done only when its acceptance criteria pass in CI.

---

## 1. Scope

### 1.1 In scope (schema + API + tests)

| Area | Tables |
|---|---|
| Institution and branch | `InstitutionProfile`, `Branch`, `Currency`, `BusinessDayStatus`, `DefinedHoliday` |
| Security and audit | `Module`, `SubModule`, `StaffUser`, `UserPermission`, `UserSession`, `EventType`, `UserActivityLog`, `DataChangeAuditLog` |
| Party and KYC | `Party`, `IndividualDetail`, `BusinessDetail`, `GroupDetail`, `GroupMember`, `PartyBeneficialOwner`, `PartyIdentificationDocument`, `PartyImage`, `PartyNextOfKin`, `PartyRegulatoryAttribute`, `CreditOfficer` |
| Product and account | `Product`, `ProductInterestRate`, `Account`, `AccountHolder`, `AccountBalance`, `AccountBalanceDailySnapshot` (schema only, see 1.2), `AccountClassification`, `AccountSubClassification`, `AccountClassificationAssignment`, `AccountCondition`, `AccountReminder`, `DormantAccountHistory`, `FreezeReleaseAccountBalance` |
| Chart of accounts | `LedgerAccount` (create / maintain only; `Balance` is maintained by later posting) |
| Lookups | Every lookup table referenced by the tables above (catalog in section 7) |

### 1.2 Schema only (table created, no endpoints, no logic)

- `CustomerDigitalAccess` (referenced by `UserSession`).
- `AccountBalanceDailySnapshot` (populated by the end-of-day process in a later phase; here: table, unique index, no writers).

### 1.3 Out of scope (do not create)

`ChargeCode`, `ProductCharge`, `ProductChargeTier`, `TransactionCode`, `TransactionCodeChargeTier`, `AccountLedgerEntry`, `LedgerEntry`, `PendingInterBranchEntry`, `SavedTransferEntry`, all Loan tables, Overdraft, FixedDeposit, Clearing and Cheque tables, Notification tables, `USSDSession`, `VerificationCode`, `VerifiedDevice`, `ApiMerchant`, Reporting and config tables (`ReportDefinition`, `ReportPermission`, `InterestProcedure`, `ProductGLSetting`, `ProductLedgerSettingDetail`, `SystemLedgerSetting`), and the lookups that only those tables use.

### 1.4 Hand-off points to the next phase

- Account opening takes **no opening deposit**. `AccountBalance` starts at zero. The next phase adds an opening-deposit posting after `AccountOpened`.
- Services raise in-process, post-commit events through `IDomainEventPublisher`: `PartyKycVerified`, `AccountOpened`, `AccountClosed`, `AccountFrozen`, `AccountUnfrozen`. No subscribers in this phase except tests.
- `AccountBalanceCalculator` (5.8) is the single place that derives `AvailableBalance` and `TotalBalance`. Ledger posting must reuse it.

---

## 2. Schema amendments and required constraints

The DBML does not yet contain these. Treat them as part of the schema and apply them in the baseline migration (task A1-03), then update the DBML.

### 2.1 Required amendments

| # | Change | Reason |
|---|---|---|
| 1 | `Party.PartyNumber varchar(20) NOT NULL UNIQUE`, generated from `PartyNumberSequence` | Staff need a human-readable customer number to quote, search and print. |
| 2 | `UserActivityLog.SessionId` becomes **nullable** | Failed logins and denied requests happen before a session exists. For those rows, put the attempted `StaffCode` in `EntityId` and `EntityType = 'StaffUser'`. |
| 3 | `LedgerAccount.LegderAccountTypeId` renamed to `LedgerAccountTypeId` | Typo. |
| 4 | `StaffUser.FullName varchar(100) NOT NULL` | The schema has no staff name. Needed for display and audit screens. |
| 5 | `StaffUser.SecretKeyChangedOn datetime2 NULL` | `SecretKeyExpiryDays` cannot be evaluated without it. |
| 6 | `LedgerAccount`: rename `ApprovalRequired` to `IsSupervisionRequired`; add `SupervisedByUserId bigint NULL` (FK `StaffUser`) and `SupervisedOn datetime2 NULL` | Maker-checker needs the supervisor columns, and the name should match every other table. |

### 2.2 Recommended amendments (apply unless the owner objects)

| # | Change | Reason |
|---|---|---|
| 7 | `Party.DisplayName varchar(200) NOT NULL`, maintained by the application on every save of Party or its detail row | Search and list screens would otherwise join three tables. |
| 8 | Filtered unique index on `IndividualDetail (IdentificationTypeId, IdentificationNumber)` where `IdentificationNumber IS NOT NULL` | Stops duplicate customers from the first day. |
| 9 | Use `nvarchar` instead of `varchar` for person names, business names, group names and address columns | Names with characters outside the Windows-1252 code page would be corrupted. |

### 2.3 Standard audit footer

Added to every table **except**: lookup tables, `UserSession`, `UserActivityLog`, `DataChangeAuditLog`, `AccountBalance`, `AccountBalanceDailySnapshot`, `CustomerDigitalAccess` (schema only, add the footer anyway).

| Column | Definition |
|---|---|
| `CreatedByUserId` | `bigint NULL`, FK `StaffUser`. NULL means system or seed. |
| `CreatedOn` | `datetime2 NOT NULL DEFAULT SYSUTCDATETIME()` |
| `ModifiedByUserId` | `bigint NULL`, FK `StaffUser` |
| `ModifiedOn` | `datetime2 NULL` |

`Branch` and `StaffUser` reference each other (`StaffUser.BranchId`, `Branch.CreatedByUserId`). Create the tables first and add the footer FKs afterwards. Seed rows have `CreatedByUserId = NULL`.

### 2.4 Indexes and constraints beyond the DBML

| Table | Constraint / index |
|---|---|
| `Party` | Unique `PartyNumber`. Indexes on `DisplayName`, `Mobile`, `CreditOfficerId`, and `(PartyTypeId, KYCStatusId)`. |
| `BusinessDetail` | Unique `RegistrationNumber` (nullable column, so it must be a **filtered** unique index; EF Core adds the filter by default, verify it). |
| `GroupMember` | Filtered unique `(MemberPartyId)` where `LeftOn IS NULL` (one active group per member, see D8). Filtered unique `(GroupPartyId, MemberPartyId)` where `LeftOn IS NULL`. Index on `GroupPartyId`. |
| `PartyBeneficialOwner` | Unique `(BusinessPartyId, OwnerPartyId)`. Check `OwnershipPercentage > 0 AND <= 100`. |
| `PartyIdentificationDocument` | Index on `PartyId`. Check `ExpiresOn >= IssuedOn` when both are set. |
| `PartyRegulatoryAttribute` | Index `(PartyId, AttributeCode, RecordedOn DESC)`. |
| `CreditOfficer` | Filtered unique `(StaffUserId)` where `IsDisabled = 0`. |
| `Account` | Unique `AccountNumber`. Indexes on `(BranchId, ProductId)` and `ProductId`. Check: `ClosedOn`, `ClosedByUserId`, `CloseReasonId` are all NULL or all NOT NULL. |
| `AccountHolder` | Unique `(AccountId, PartyId)` (already in DBML). Index on `PartyId` (list a party's accounts). Index `(AccountId, AccountHolderRoleId)`. |
| `AccountClassificationAssignment` | Unique `(AccountId, AccountClassificationId)`. |
| `FreezeReleaseAccountBalance` | Index `(AccountId, FreezeReleaseId)`. Check `Amount IS NULL OR Amount > 0`. |
| `ProductInterestRate` | Check `EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom`. Check `MinimumAmount <= MaximumAmount`. Index `(ProductId, RateTypeId, EffectiveFrom)`. |
| `Currency` | Filtered unique index on `IsLocalCurrency` where `IsLocalCurrency = 1` (exactly one local currency). |
| `InstitutionProfile` | Check `InstitutionProfileId = 1` (singleton). |
| `UserSession` | Check: exactly one of `StaffUserId` / `CustomerDigitalAccessId` is set. Filtered index on `StaffUserId` where `LogoutAt IS NULL`. |
| `UserPermission` | Unique `(StaffUserId, BranchId, ModuleId, SubModuleId)`. |
| `UserActivityLog` | Indexes `(SessionId, EventTimestamp)`, `(EntityType, EntityId, EventTimestamp)`, `(EventTypeId, EntityId, EventTimestamp)`. |
| `DataChangeAuditLog` | Indexes `(TableName, RecordId, LoggedOn)` and `(StaffUserId, LoggedOn)`. |
| `LedgerAccount` | Unique `LedgerAccountCode`. |

Sequences: `PartyNumberSequence` and `AccountNumberSequence` (`bigint`, start 1, `CACHE 50`; gaps are acceptable).

### 2.5 Ledger tables

`UserActivityLog` and `DataChangeAuditLog` are SQL Server 2022 **append-only ledger tables**.

- Create them with hand-written SQL in a migration: `CREATE TABLE ... WITH (LEDGER = ON (APPEND_ONLY = ON))`.
- Do **not** declare `PERIOD FOR SYSTEM_TIME` or `SYSTEM_VERSIONING`. Append-only ledger tables add their own hidden ledger columns. (An earlier draft of this design mixed the two. That form is wrong.)
- Map the entities in EF with `ToTable(name, t => t.ExcludeFromMigrations())` so EF does not try to create them.
- The DbContext must throw if any ledger entity is in the `Modified` or `Deleted` state.
- Partitioning is desired (monthly) but unverified for ledger tables. Task A1-05 is a spike that decides it.

---

## 3. Technology decisions

| Topic | Decision |
|---|---|
| Layout | `src/Banking.Domain`, `Banking.Application`, `Banking.Infrastructure`, `Banking.Api`, `Banking.Migrator` (console: apply EF schema migrations and seed reference data; it never imports data). `tests/Banking.Domain.Tests`, `Banking.Application.Tests`, `Banking.Api.IntegrationTests`. `docs/schema`, `docs/adr`, `docs/api`. `docker/`. |
| Dependencies | Domain has no EF or ASP.NET references. Application depends on Domain. Infrastructure and Api depend on Application. |
| Persistence | EF Core code-first, `IEntityTypeConfiguration<T>` per entity, table and column names exactly as in the DBML (singular, no pluralization), schema `dbo`. |
| Types | Money `decimal(19,4)`. Rates as in the DBML. All timestamps `datetime2` in UTC via `TimeProvider`. Calendar values (`DateOpened`, `WorkingDate`) are `date`. Ghana is UTC+0 with no daylight saving, but still never use local time. |
| IDs | `bigint IDENTITY` or `int IDENTITY` as in the DBML. |
| Concurrency | `RowVersion` mapped with `IsRowVersion()` on `AccountBalance`, `Account`, `LedgerAccount`, `StaffUser`. |
| Validation | FluentValidation for request shape. Domain rules in Domain/Application services. |
| Mapping | Explicit mapping code. No reflection-based mappers. |
| Auth | JWT bearer. Passwords hashed with `PasswordHasher<T>` (Microsoft.Extensions.Identity.Core only, not the full Identity framework). |
| Logging | Serilog for operational logs. `UserActivityLog` is a separate, compliance-grade trail and is never replaced by Serilog. |
| API | REST, `/api/v1/...`, JSON, RFC 7807 problem details, OpenAPI document generated and committed to `docs/api/openapi.json`. |
| Tests | xUnit, Testcontainers (`Testcontainers.MsSql`), Shouldly for assertions (avoid FluentAssertions v8+ licensing). |
| Docker | `mcr.microsoft.com/mssql/server:2022-latest`, Developer edition for dev and test. Full-Text Search is **not** in the default image, so do not depend on it. |
| Secrets | None in the repo. `.env.example` lists variables. Connection strings, JWT key and bootstrap admin come from environment or user secrets. |

---

## 4. Configuration catalog

All keys live in `appsettings.json` with these defaults. Items marked **placeholder** need a business or compliance decision.

| Key | Default |
|---|---|
| `Jwt:AccessTokenMinutes` | 15 |
| `Jwt:MaxSessionHours` | 12 |
| `Security:PasswordMinLength` | 8 (plus upper, lower, digit; must differ from `StaffCode`) |
| `Security:LockoutThreshold` / `LockoutWindowMinutes` / `LockoutDurationMinutes` | 5 / 15 / 15 |
| `Supervision:Enabled` | true |
| `Supervision:AllowSelfApproval` | false. May be true only in Development and Test. Startup must fail if true in Production. |
| `PartyNumber:Length` | 8 digits, zero padded |
| `AccountNumber:Length` | 13 digits including the product prefix. Decide before the first account is created; changing it later affects only new numbers. |
| `AccountNumber:UseCheckDigit` | false |
| `Account:DirectOpeningProductTypes` | `["SAVINGS","CURRENT"]` |
| `Kyc:BeneficialOwnerThresholdPercent` | 25 (**placeholder**) |
| `Kyc:MinimumGroupMembers` | 5 (**placeholder**) |
| `Uploads:MaxImageBytes` | 5242880 |
| `Uploads:AllowedContentTypes` | `image/jpeg`, `image/png`, `application/pdf` |
| `Calendar:WeekendDays` | `["Saturday","Sunday"]` |
| `Pagination:MaxPageSize` | 100 |

Bootstrap (Migrator only): `BOOTSTRAP_ADMIN_CODE`, `BOOTSTRAP_ADMIN_SECRET`, `BOOTSTRAP_BRANCH_CODE`, `BOOTSTRAP_BRANCH_NAME`.

---

## 5. Cross-cutting standards

### 5.1 Naming and mapping
Entity class = table name. Property = column name. `varchar` columns use `IsUnicode(false)` unless amendment 9 is applied. Every string has the max length from the DBML. `bit` maps to `bool`.

### 5.2 Audit footer interceptor
An `IAuditable` interface and a `SaveChangesInterceptor` set `CreatedOn/By` on insert and `ModifiedOn/By` on update, using `ICurrentUser` and `TimeProvider`. When there is no current user (migrator, seeder), `By` stays NULL.

### 5.3 Data change audit
- Override `SaveChangesAsync` in the DbContext: open a transaction, save business changes, build `DataChangeAuditLog` rows (now that generated keys exist), save them, commit. One atomic unit.
- Audited tables: every in-scope table except lookups-that-never-change, the two ledger tables and `UserSession`. Lookups are audited because admins can edit them.
- Row content: `TableName`, `RecordId` (composite keys joined with `|`), `ActionType` (`INSERT`, `UPDATE`, `DELETE`), `OldValueJson`, `NewValueJson` (changed columns only for updates), `StaffUserId`, `BranchId`, `LoggedOn`.
- Redacted (never written): `SecretKeyHash`, `ImageValue`, any column named `*Hash` or `*Secret*`. For image columns write `"[binary, N bytes]"`.

### 5.4 Maker-checker (supervision)
Applies to: `Party`, `PartyImage`, `Account`, `Product`, `LedgerAccount`.

| Rule | Definition |
|---|---|
| States | Pending = `IsSupervisionRequired = 1` and `SupervisedByUserId IS NULL`. Supervised = `SupervisedByUserId IS NOT NULL`. Not required = `IsSupervisionRequired = 0`. |
| R1 | A new record starts Pending (when `Supervision:Enabled`). |
| R2 | Supervise requires `CanSupervise` on the right module, and the supervisor must not be the creator or the last modifier (unless `AllowSelfApproval`). It sets `SupervisedByUserId` and `SupervisedOn`. `IsSupervisionRequired` stays 1 as history. |
| R3 | A Pending record cannot be used for new dependent operations (open an account for a Pending party, open an account on a Pending product, and later: post to a Pending account). Return 409. |
| R4 | Edits to a Supervised record are allowed with `AllowEdit` and are audited. Edits to **controlled fields** (below) reset it to Pending (`SupervisedByUserId = NULL`, `SupervisedOn = NULL`). |
| R5 | Reject = hard delete, allowed only for a record that has **never** been supervised and has no dependents. The reason goes in the activity log. Rejecting a re-pended edit is not supported: the fix is another edit, then supervise. |

Controlled fields:
- Party: names, date of birth, identification type and number, registration number, TIN, group name, chairperson. `PartyTypeId` is immutable.
- Account: `AccountName`, `OperatingModeId`, the holder set. `ProductId` is immutable.
- Product: `MinimumBalance`, `AccountsPerCustomer`, interest rates, `IsFreezed`. `ProductTypeId`, `CurrencyId`, `AccountPrefix` are immutable once any account exists.
- LedgerAccount: `AccountName`. `LedgerAccountTypeId` and `CurrencyId` are immutable once the account is supervised.
- PartyImage: replacing the photo resets it to Pending.

### 5.5 Authentication, sessions and permissions

**Login** (`POST /auth/login`):
1. Look up `StaffUser` by `StaffCode`. Unknown code, wrong secret, or disabled user all return the same 401.
2. Lockout is derived, not stored: if there are `LockoutThreshold` `LOGIN_FAILED` activity rows for that `StaffCode` within `LockoutWindowMinutes`, reject until `LockoutDurationMinutes` after the latest failure.
3. Check password expiry using `SecretKeyChangedOn` and `SecretKeyExpiryDays` (NULL or 0 = never). If expired, or `ChangeKeyAtNextLogOn` is set, login succeeds but the response says `mustChangeSecretKey: true` and only `auth/change-secret-key` and `auth/logout` are allowed.
4. End any open session for this user with reason `SUPERSEDED_BY_NEW_LOGIN`, create a `UserSession`, issue a JWT with claims `sub` (StaffUserId), `sid` (SessionId), `branch` (effective branch).
5. Write a `LOGIN` or `LOGIN_FAILED` activity row.

**Per request:** middleware validates the JWT, then checks that the session is still open (cache for at most 30 seconds so forced logout takes effect quickly), and that `LoginAt + MaxSessionHours` has not passed.

**Idle timeout:** the access token lifetime is the idle window. `POST /auth/refresh` accepts a still-valid token and issues a new one while the session is open. A hosted `SessionSweeper` runs every minute and closes open sessions whose latest `UserActivityLog` row is older than the idle window, with reason `IDLE_TIMEOUT`.

**Branch switching:** effective branch = the user's `BranchId`. A user with `CanSwitchBranch` may send `X-Branch-Id`, accepted only if they have at least one `UserPermission` row for that branch.

**Permission model:**
- A `UserPermission` row for `(user, branch, module[, submodule])` grants **view** access. There is no separate view flag.
- Action to flag: create = `AllowAdd`, update = `AllowEdit`, delete or reject-pending = `AllowDelete`, approve or verify = `CanSupervise`.
- A row with `SubModuleId = NULL` covers the whole module. A row with a sub-module covers only that sub-module. The most specific row wins.
- Use `[RequirePermission(module, submodule, action)]` on every endpoint. Failure returns 403 and writes a `PERMISSION_DENIED` activity row.
- Branch rule (D7): reads are institution-wide for any user with view access. Writes to branch-owned data (`Account`, `LedgerAccount`) require the entity's `BranchId` to equal the effective branch. `Party` has no branch, so it is writable from any branch where the user holds the permission.
- `Limit` columns on `UserPermission` are not used in this phase (they apply to transactions later).

### 5.6 Activity logging (`UserActivityLog`)
- Exactly **one row per authenticated request**, plus one row for each failed login or denied request.
- Default `EventType` is `API_REQUEST` (method, route template, status code, duration in `RequestDetailsJson`). Services enrich the row through a scoped `IActivityContext.Set(eventCode, entityType, entityId, details)`. If a service sets it, that event type replaces the default.
- **Mutating requests:** the activity row is added to the same DbContext transaction as the business change, so a change cannot commit without its log row.
- **Reads and denials:** the middleware writes the row. For sensitive reads (Party detail and overview, documents, photos, Account detail and balance, audit screens) a failed log write fails the request with 500 (fail closed). For other reads, log the failure with Serilog and continue.
- `Outcome` is `Success`, `Denied` or `Error`.
- Redaction in `RequestDetailsJson`: never record request bodies for `auth/*`, any field named like `secret`, `password`, `otp`, `token`, or binary content. Record sizes only.
- Event type codes are in section 7.

### 5.7 Errors and HTTP behavior

| Status | Use |
|---|---|
| 400 | Validation failure. Problem details with a field-level `errors` map. |
| 401 | Missing or invalid token, closed session, bad login. |
| 403 | Authenticated but lacks the permission. |
| 404 | Entity not found. |
| 409 | Business-rule conflict: duplicate, wrong state, pending supervision, concurrency conflict. Problem details `type` carries a stable machine code such as `party.kyc.not-verified`. |

Lists: `page` (default 1), `pageSize` (default 25, max 100), `sort`, plus filters per endpoint. Response: `{ items, page, pageSize, totalCount }`.

### 5.8 Account balance rules
`AccountBalanceCalculator` (pure, unit-tested) defines:

- `AvailableBalance = ClearBalance - FreezedAmount - UnsupervisedDebit + OverdrawLimit`
- `TotalBalance = ClearBalance + UnsupervisedCredit - UnsupervisedDebit`

These formulas are the system's definition of balances, and the posting phase reuses them unchanged. Minimum balance is enforced separately when a debit is attempted, not folded into `AvailableBalance`.

Every change to `AccountBalance` goes through an optimistic-concurrency write: catch `DbUpdateConcurrencyException`, reload and retry up to 3 times, then return 409.

### 5.9 Number generation
- `PartyNumber`: `NEXT VALUE FOR PartyNumberSequence`, zero padded to `PartyNumber:Length`.
- `AccountNumber`: `Product.AccountPrefix` + zero-padded `NEXT VALUE FOR AccountNumberSequence`, total length `AccountNumber:Length`, optional Luhn check digit. The sequence is global, so numbers are unique regardless of prefix.
- The format is fixed before the first account is created. A later configuration change affects only new numbers.

### 5.10 Lookups
- Read endpoint for every lookup. Admin create and update (`REF/LOOKUPS`). Never delete, only set `IsActive = 0`. `Code` is immutable after creation.
- Codes used in application code are declared in a static `LookupCodes` class. The API refuses to deactivate those rows.
- Seeding is idempotent, keyed by `Code`, and runs only from `Banking.Migrator`, never on API startup.

---

## 6. Domain rules

### 6.1 Party creation
One transaction creates `Party` + the detail row + optional children. Endpoints are type-specific (`/parties/individuals`, `/parties/businesses`, `/parties/groups`). A new party gets `PartyNumber`, `DisplayName`, `KYCStatus = PENDING`, and is Pending supervision.

`DisplayName`: Individual = `FirstName LastName`. Business = `BusinessName`. Group = `GroupName`.

### 6.2 Individual
- Required to create: `FirstName`, `LastName`, `DateOfBirth`, `GenderId`, `Nationality`, `Mobile`, `Address1`, primary identification type and number.
- Age at creation must be 18 or more (placeholder, confirm with compliance).
- The primary ID is stored on `IndividualDetail` **and** as a `PartyIdentificationDocument` row. The service keeps them in sync; the document row is the source for images and expiry.
- Duplicate check: same identification type and number returns 409 with the existing `PartyNumber`.

### 6.3 Business
- Required: `BusinessName`, `RegistrationNumber`, `BusinessCategoryId` (legal form), `Mobile` or `Phone`, `Address1`, `SourceOfFunds`.
- `PartyBeneficialOwner`: the owner must be an Individual party. Percentage in (0, 100]. Total across owners may not exceed 100. Owners at or above `Kyc:BeneficialOwnerThresholdPercent` must exist for KYC to verify.
- Sector or industry: free text in `Party.OccupationOrSector` until the regulator's sector codes are confirmed.

### 6.4 Group
- `GroupDetail.ChairpersonPartyId` must be an Individual who is an active member with role `CHAIRPERSON`. Exactly one active chairperson.
- Members must be Individual parties. A member can be active in only one group at a time (D8). Leaving sets `LeftOn`; history rows stay.
- Group KYC can be verified only if the active member count is at least `Kyc:MinimumGroupMembers` and every active member is KYC-verified.
- A group is a normal Party: it can hold an account (as `PRIMARY`) and, in a later phase, borrow.

### 6.5 KYC lifecycle
- `PENDING` -> `VERIFIED` by `POST /parties/{id}/kyc/verify`. Requires supervise permission on `PTY/KYC`, and the verifier is not the creator or last modifier. Verification and supervision are one action (D3): the verifier confirms the details and approves the record together. If the record is already supervised, the action verifies KYC only. A party cannot open an account until both are complete.
- `VERIFIED` or `PENDING` -> `REJECTED` with a reason (reason goes in the activity log).
- `EXPIRED` is **computed** in this phase: a stored `VERIFIED` party whose required identification document has `ExpiresOn` in the past reads as `EXPIRED`. Account opening uses the effective status. A background job to store it is a later task.
- Material change resets KYC to `PENDING`: any change to controlled Party fields, identification documents, beneficial owners, group members or chairperson.
- `GET /parties/{id}/kyc/checklist` returns the completeness result (what is missing) so the UI can show it.

Completeness defaults (policy class `KycRequirementPolicy`, one per party type, **placeholder until the regulator's field list is confirmed**):

| Party type | Required before `VERIFIED` |
|---|---|
| Individual | All creation fields, a supervised photo (`PartyImage`), at least one identification document with an image, at least one next of kin, `SourceOfFunds`, `OccupationOrSector` |
| Business | All creation fields, at least one identification document with an image (for example certificate of incorporation), at least one beneficial owner, TIN |
| Group | Chairperson, minimum active members, all members verified |

`IsPoliticallyExposed = 1` is stored and shown. It does not block anything in this phase (Q6).

### 6.6 Documents, photo, next of kin, regulatory attributes
- Images are stored in `ImageValue`. Accept only `Uploads:AllowedContentTypes`, detect the type from the file's leading bytes (not the filename), enforce `MaxImageBytes`. Images are never embedded in JSON; they have their own `GET .../image` endpoints.
- Mask identification numbers in list responses (last 4 characters only). Full values only on the detail endpoint.
- `PartyNextOfKin`: free CRUD for users with edit rights.
- `PartyRegulatoryAttribute` is append-only history. The current value is the latest `RecordedOn` per `(PartyId, AttributeCode)`. `AttributeCode` is upper snake case, max 50 characters.

### 6.7 Credit officers
`POST /credit-officers` designates a staff user (not already an active credit officer). `PUT /parties/{id}/credit-officer` assigns one. `CreditOfficer.BranchId` is the staff user's branch.

### 6.8 Product
- Required: `ProductCode` (immutable, globally unique), `ProductName`, `ProductTypeId`, `AccountPrefix`, `CurrencyId`, `LedgerControlAccountId`.
- Control ledger account type must be `LIABILITY` for `SAVINGS`, `CURRENT`, `FIXED_DEPOSIT` and `ASSET` for `LOAN`. Its currency must match the product.
- `AccountsPerCustomer`: NULL or 0 means unlimited. Counted per party for accounts where the party is `PRIMARY` and the account is not closed.
- `Product.CreditInterestRate` / `DebitInterestRate` are the flat defaults. If `ProductInterestRate` rows exist for the product, type and date, they override.
- `ProductInterestRate` is effective-dated. A rate that has started is never edited in place. To change it, set `EffectiveTo` on the old row (the day before) and insert a new row. Rows of the same product, rate type and amount band may not overlap.
- Product-level freeze (`IsFreezed`) blocks opening new accounts on it.

### 6.9 Account opening
`POST /accounts` runs in one transaction and fails with 409 if any check fails:

1. Business day is open for the effective branch (`DateOpened` = the working date, not the system date).
2. Product exists, is Supervised, active and not frozen, and its type is in `Account:DirectOpeningProductTypes`. (Loan and fixed-deposit accounts are created by their own workflows later.)
3. Holders: at least one, exactly one `PRIMARY`. Each holder party is Supervised and has effective KYC status `VERIFIED`.
4. Shape by primary holder type:

| Primary holder | Rules |
|---|---|
| Individual | Mode `SINGLE`. Optional `SIGNATORY` holders (Individuals). |
| Individual + `JOINT` holders | At least 2 Individuals. Mode must be `EITHER_TO_SIGN` or `ALL_TO_SIGN`. |
| Business | At least one `SIGNATORY` (Individual). Mode `EITHER_TO_SIGN` or `ALL_TO_SIGN`. |
| Group | Mode `EITHER_TO_SIGN` or `ALL_TO_SIGN`. Signatories are active group members. |

5. `AccountsPerCustomer` limit is respected for the primary holder.
6. Generate `AccountNumber`. `AccountName` defaults to the primary holder's `DisplayName` (joint: names joined with " & "), editable.
7. Create: `Account` (Pending supervision), `AccountHolder` rows, `AccountBalance` (all zero), `AccountCondition` (all flags false).
8. Raise `AccountOpened` after commit.

`BENEFICIAL_OWNER` holders are informational (any party type allowed, no signing rights).
`AccountHolder.OperatingInstructionId` is nullable and unused in this phase (Q3).

### 6.10 Account servicing
- **Supervise / reject**: per 5.4.
- **Close** (`POST /accounts/{id}/close`, `AllowDelete` on `ACC/ACCOUNT`): requires `CloseReasonId`, `ClearBalance = 0`, `FreezedAmount = 0`, no unsupervised amounts. Sets `ClosedOn/By/Reason`. Closed accounts are read-only. Raise `AccountClosed`.
- **Freeze / hold** (`POST /accounts/{id}/freeze-actions`, body `{ action, amount?, remarks }`), actions `HOLD_AMOUNT`, `RELEASE_AMOUNT`, `FREEZE_ACCOUNT`, `UNFREEZE_ACCOUNT`:
  - `HOLD_AMOUNT`: amount > 0, adds to `AccountBalance.FreezedAmount`.
  - `RELEASE_AMOUNT`: amount > 0 and not more than `FreezedAmount`.
  - `FREEZE_ACCOUNT` / `UNFREEZE_ACCOUNT`: amount is NULL, flips `AccountCondition.IsFreezed`. Freezing a frozen account (or the reverse) is 409.
  - Each call inserts a `FreezeReleaseAccountBalance` row and updates the current-state row in the **same transaction**. `FreezeReleaseAccountBalance` is the history; `AccountBalance` and `AccountCondition` are the current state.
  - Recompute `AvailableBalance` and `TotalBalance` with `AccountBalanceCalculator`. Uses optimistic concurrency (5.8).
- **Dormancy**: `POST /accounts/{id}/dormancy/mark` sets `IsDormant = 1`. `POST /accounts/{id}/dormancy/reactivate` sets it to 0 and inserts a `DormantAccountHistory` row. Automatic detection is out of scope (the DBML no longer has dormancy thresholds on `Product`).
- **Classification**: assign and unassign `AccountClassificationAssignment`. A sub-classification must belong to the chosen classification. Many classifications per account are allowed.
- **Reminders**: add and remove `AccountReminder` (max 200 characters). Reminders are returned with the account detail so staff see them.
- **Account status** in responses is derived: `PENDING_SUPERVISION`, `CLOSED`, `FROZEN`, `DORMANT`, `ACTIVE` (first match in that order).

### 6.11 Ledger accounts (chart of accounts)
Create, update, list, supervise, reject. `LedgerAccountCode` unique. Type from `LedgerAccountType`. `Balance` is read-only here (maintained by posting later). A staff user with `IsTeller = 1` must have `TellerGLAccountId` pointing to an `ASSET` ledger account in the user's own branch.

### 6.12 Business day and calendar
- `POST /branches/{id}/business-days/open` with a date. Rules: the previous day for that branch is `CLOSED` (or none exists), the date is after it, and the date is not a weekend day or a `DefinedHoliday`.
- `POST /branches/{id}/business-days/{date}/close` only flips status to `CLOSED` (needs `CanSupervise` on `REF/CALENDAR`). End-of-day processing is added in the transactions phase.
- `IWorkingDateProvider.GetAsync(branchId)` returns the date of the `OPEN` row, or throws a 409 `business-day.not-open`.

---

## 7. Seed catalog (idempotent, keyed by Code)

| Lookup / table | Codes |
|---|---|
| `RegulatoryCategoryType` | `MICROFINANCE_BANK`, `SAVINGS_AND_LOANS`, `COMMUNITY_BANK`, `CREDIT_UNION`, `LAST_MILE_PROVIDER` |
| `BusinessDayStatusType` | `OPEN`, `CLOSED`, `EOD_IN_PROGRESS` |
| `LogoutReasonType` | `USER_INITIATED`, `IDLE_TIMEOUT`, `MAX_SESSION_EXCEEDED`, `SUPERSEDED_BY_NEW_LOGIN`, `FORCED_BY_ADMIN`, `SECRET_KEY_CHANGED`, `ACCOUNT_DISABLED` |
| `PartyType` | `INDIVIDUAL`, `BUSINESS`, `GROUP` |
| `KYCStatusType` | `PENDING`, `VERIFIED`, `REJECTED`, `EXPIRED` |
| `GenderType` | `MALE`, `FEMALE` |
| `IdentificationDocumentType` | Individual: `GHANA_CARD`, `PASSPORT`, `DRIVERS_LICENCE`, `VOTER_ID`. Business: `CERTIFICATE_OF_INCORPORATION`, `CERTIFICATE_TO_COMMENCE_BUSINESS`, `TIN_CERTIFICATE`, `BUSINESS_OPERATING_PERMIT`. Which types apply to which party type is a static map in code. Confirm the accepted list with compliance. |
| `BusinessCategoryType` | `SOLE_PROPRIETORSHIP`, `PARTNERSHIP`, `LIMITED_COMPANY`, `NGO`, `RELIGIOUS_BODY`, `ASSOCIATION`, `OTHER` (legal form) |
| `GroupMemberRoleType` | `CHAIRPERSON`, `SECRETARY`, `TREASURER`, `MEMBER` |
| `ProductType` | `SAVINGS`, `CURRENT`, `FIXED_DEPOSIT`, `LOAN` |
| `RateType` | `CREDIT`, `DEBIT` |
| `OperatingModeType` | `SINGLE`, `EITHER_TO_SIGN`, `ALL_TO_SIGN` |
| `OperatingInstructionType` | `EITHER_TO_SIGN`, `ALL_TO_SIGN` |
| `AccountHolderRoleType` | `PRIMARY`, `JOINT`, `SIGNATORY`, `BENEFICIAL_OWNER` |
| `CloseReasonType` | `CUSTOMER_REQUEST`, `DECEASED`, `FRAUD`, `DORMANT_CLOSURE`, `REGULATORY`, `OPENED_IN_ERROR`, `OTHER` |
| `FreezeReleaseActionType` | `HOLD_AMOUNT`, `RELEASE_AMOUNT`, `FREEZE_ACCOUNT`, `UNFREEZE_ACCOUNT` |
| `LedgerAccountType` | `ASSET`, `LIABILITY`, `EQUITY`, `INCOME`, `EXPENSE` |
| `EventType` | `API_REQUEST`, `LOGIN`, `LOGIN_FAILED`, `LOGOUT`, `SESSION_EXPIRED`, `SECRET_KEY_CHANGED`, `PERMISSION_DENIED`, `VIEW_RECORD`, `SEARCH`, `CREATE_RECORD`, `UPDATE_RECORD`, `DELETE_RECORD`, `SUPERVISE_APPROVE`, `SUPERVISE_REJECT`, `KYC_VERIFY`, `KYC_REJECT`, `ACCOUNT_OPEN`, `ACCOUNT_CLOSE`, `ACCOUNT_FREEZE`, `ACCOUNT_UNFREEZE`, `BALANCE_HOLD`, `BALANCE_RELEASE`, `DORMANCY_CHANGE`, `USER_ADMIN`, `AUDIT_VIEW`, `BUSINESS_DAY_OPEN`, `BUSINESS_DAY_CLOSE` |

**Modules and sub-modules**

| Module | Sub-modules |
|---|---|
| `SEC` Security | `USERS`, `PERMISSIONS`, `SESSIONS`, `AUDIT` |
| `REF` Reference data | `INSTITUTION`, `BRANCHES`, `CURRENCIES`, `CALENDAR`, `LOOKUPS` |
| `PTY` Party and KYC | `PARTY`, `KYC`, `DOCUMENTS`, `GROUPS`, `CREDIT_OFFICERS` |
| `ACC` Accounts | `PRODUCT`, `ACCOUNT`, `CONDITION`, `CLASSIFICATION` |
| `LED` Ledger | `CHART` |

**Bootstrap data (Migrator):** one `Branch` from `BOOTSTRAP_BRANCH_*`; one empty `InstitutionProfile` row (id 1); one local `Currency` (`GHS`) when none exists; staff user `SYSTEM` (disabled, cannot log in) used for system-generated records; the admin from `BOOTSTRAP_ADMIN_*` with `ChangeKeyAtNextLogOn = 1` and full permissions on every module and sub-module for the bootstrap branch.

---

## 8. API surface (all under `/api/v1`, all require auth except login)

| Group | Endpoints |
|---|---|
| Auth | `POST /auth/login`, `POST /auth/refresh`, `POST /auth/logout`, `POST /auth/change-secret-key`, `GET /auth/me` (profile + effective permissions) |
| Security admin | `GET/POST /staff-users`, `GET/PUT /staff-users/{id}`, `POST /staff-users/{id}/disable`, `/enable`, `/reset-secret-key`, `GET/PUT /staff-users/{id}/permissions` (replace the set), `GET /sessions`, `POST /sessions/{id}/force-logout`, `GET /modules`, `GET /audit/activity`, `GET /audit/data-changes` |
| Reference | `GET/PUT /institution-profile`, `GET/POST/PUT /branches`, `GET/POST/PUT /currencies`, `GET/POST/DELETE /holidays`, `GET/POST /branches/{id}/business-days` (+ `/{date}/close`), `GET /lookups/{name}`, `POST/PUT /lookups/{name}` |
| Ledger accounts | `GET/POST/PUT /ledger-accounts`, `POST /ledger-accounts/{id}/supervise`, `/reject` |
| Party | `POST /parties/individuals`, `/businesses`, `/groups`; `GET /parties` (search: `q`, `partyType`, `kycStatus`, `supervision`); `GET /parties/{id}`; `GET /parties/{id}/overview` (party + accounts held + KYC summary); `PUT /parties/{id}/individual`, `/business`, `/group`; `POST /parties/{id}/supervise`, `/reject` |
| KYC | `GET /parties/{id}/kyc/checklist`, `POST /parties/{id}/kyc/verify`, `/kyc/reject` |
| Party children | `/parties/{id}/documents` (list, upload, replace, delete, `/{docId}/image`), `/parties/{id}/photo` (PUT, GET, `/supervise`), `/parties/{id}/next-of-kin` (CRUD), `/parties/{id}/members` (list, add, change role, `/{id}/leave`), `/parties/{id}/beneficial-owners` (CRUD), `/parties/{id}/regulatory-attributes` (GET, `PUT /{code}` appends a version), `PUT /parties/{id}/credit-officer` |
| Credit officers | `GET/POST /credit-officers`, `POST /credit-officers/{id}/disable` |
| Product | `GET/POST/PUT /products`, `POST /products/{id}/supervise`, `/reject`, `/products/{id}/interest-rates` (GET, POST, close) |
| Account | `POST /accounts`, `GET /accounts` (search: `q`, `branchId`, `productId`, `status`), `GET /accounts/{id}`, `PUT /accounts/{id}`, `POST /accounts/{id}/supervise`, `/reject`, `/close`, `GET /accounts/{id}/balance`, `GET/POST /accounts/{id}/freeze-actions`, `POST /accounts/{id}/dormancy/mark`, `/dormancy/reactivate`, `GET/POST/DELETE /accounts/{id}/classifications`, `GET/POST/DELETE /accounts/{id}/reminders` |

Search behavior: `q` matches `PartyNumber` or `AccountNumber` (exact and prefix), `DisplayName` (prefix and contains), mobile (exact), identification number (exact). Use indexed prefix matches first. Do not use Full-Text Search.

---

## 9. Work breakdown

### A0 Scaffolding and environment

| ID | Task | Acceptance criteria |
|---|---|---|
| A0-01 | Create the solution layout (section 3), `global.json`, `Directory.Build.props` (nullable on, warnings as errors, analyzers), central package management, `.editorconfig`. | `dotnet build` is clean with zero warnings. |
| A0-02 | `docker/docker-compose.yml` for SQL Server 2022 with a healthcheck and a named volume, plus `.env.example`. | `docker compose up -d` reaches healthy; a connection from the host works. |
| A0-03 | Test infrastructure: Testcontainers MSSQL fixture, one database per test class, helper to build an authenticated `HttpClient`. Cleanup must skip ledger tables (rows cannot be deleted); use a fresh database per class instead of row cleanup. | A sample integration test runs locally and in CI. |
| A0-04 | CI definition (restore, build, test, OpenAPI export and diff check). Provider-agnostic steps in `docs/ci.md`. | CI passes on an empty feature. |
| A0-05 | Copy the DBML to `docs/schema/core-banking.dbml`; create `docs/adr/` with this plan's decision table (section 12) and `docs/CHANGELOG.md`. | Files exist and are linked from the README. |

### A1 Database baseline and reference data

| ID | Task | Acceptance criteria |
|---|---|---|
| A1-01 | Domain and infrastructure base types: `IAuditable`, lookup base class, `BankingDbContext`, global conventions (decimal 19,4; datetime2; varchar lengths). | Unit tests cover conventions. |
| A1-02 | Entities and EF configurations for every in-scope table (section 1) with the amendments (2.1, 2.2) and footer (2.3). | Model builds; `dotnet ef migrations has-pending-model-changes` is clean after A1-03. |
| A1-03 | Migration 0001 (tables, FKs, indexes, checks, sequences from 2.4) and migration 0002 (the two ledger tables, hand-written SQL, see 2.5). Update the DBML. | Applies to an empty database. A schema test asserts the exact table list, every constraint in 2.4, and that `UserActivityLog` and `DataChangeAuditLog` are ledger tables. |
| A1-04 | `Banking.Migrator` with idempotent seeding (section 7) and bootstrap data. | Running it twice changes nothing. Admin can be created from environment variables. |
| A1-05 | **Ledger spike.** In the Docker image, verify: append-only inserts work through EF (identity returned), `UPDATE` and `DELETE` fail even as `sa`, foreign keys from the ledger tables work, `sys.sp_generate_database_ledger_digest` and `sys.sp_verify_database_ledger` run, and whether a partition scheme can be applied. Write the outcome to an ADR. | ADR committed. Integration tests exist for the immutability and verify behavior. If partitioning is not possible, record the alternative (for example, archive by date range). |

### A2 Cross-cutting infrastructure and security

| ID | Task | Acceptance criteria |
|---|---|---|
| A2-01 | `ICurrentUser`, `TimeProvider`, audit footer interceptor (5.2). | Tests: insert and update set the right columns; the migrator path leaves them NULL. |
| A2-02 | `DataChangeAuditLog` capture (5.3) with redaction. | Tests: insert, update (changed columns only), delete; `SecretKeyHash` and image bytes never appear. Business change and audit rows commit or fail together. |
| A2-03 | Lookup read and admin endpoints, `LookupCodes`, deactivation guard (5.10). | Tests: cannot delete, cannot change `Code`, cannot deactivate a coded row. |
| A2-04 | Authentication: login, refresh, logout, change secret key, lockout, expiry, `SessionSweeper` (5.5). | Tests for every branch of the login flow, superseded sessions, idle timeout, forced logout taking effect within the cache window. |
| A2-05 | Permission model: `[RequirePermission]`, effective branch, `X-Branch-Id`, branch write rule (5.5). | Permission matrix test: each role/action/branch combination returns the expected 200, 403 or 409. |
| A2-06 | Activity logging (5.6): middleware, `IActivityContext`, in-transaction logging for writes, fail-closed rule. | Tests: one row per request; denied and failed-login rows have the attempted `StaffCode`; a failed log write fails a sensitive read; redaction works. |
| A2-07 | Maker-checker framework (5.4) as reusable domain service and endpoint helpers. | Unit tests for R1 to R5, including self-approval blocked and the Production startup guard. |
| A2-08 | Security admin API: staff users, permissions, sessions, audit viewers. Viewing audit logs is itself logged (`AUDIT_VIEW`). | Integration tests for each endpoint and permission. |

### A3 Reference data and chart of accounts

| ID | Task | Acceptance criteria |
|---|---|---|
| A3-01 | `InstitutionProfile`, `Branch`, `Currency`, `DefinedHoliday` endpoints. | Singleton cannot be created or deleted; exactly one local currency; branch code immutable. |
| A3-02 | Business day open and close, `IWorkingDateProvider` (6.12). | Tests for weekend, holiday, previous-day-open, and not-open cases. |
| A3-03 | `LedgerAccount` management with maker-checker (6.11); teller validation on staff users. | Tests for uniqueness, type and currency immutability, teller rule. |

### A4 Party and KYC

| ID | Task | Acceptance criteria |
|---|---|---|
| A4-01 | `PartyNumber` generation, `DisplayName` maintenance, create Individual (6.1, 6.2) with duplicate-ID check. | Concurrent creates never collide; duplicate ID returns 409 with the existing number. |
| A4-02 | Create Business and beneficial owners (6.3). | Percentage rules enforced. |
| A4-03 | Create Group, members, chairperson (6.4). | One active group per member and one chairperson enforced at the database and service level. |
| A4-04 | Documents, photo, image handling (6.6). | Wrong content type, oversize, and spoofed extension are rejected; ID numbers masked in lists; photo supervise flow works. |
| A4-05 | Next of kin, regulatory attributes (append-only history), credit officers (6.6, 6.7). | Latest-value read returns the newest version per code. |
| A4-06 | Party maker-checker and controlled-field reset (5.4); update endpoints. | Editing a controlled field returns the party to Pending and resets KYC to `PENDING`. |
| A4-07 | KYC checklist, verify, reject, computed `EXPIRED` (6.5); `KycRequirementPolicy` per party type. | Unit tests per policy; expired document makes effective status `EXPIRED`; `PartyKycVerified` raised after commit. |
| A4-08 | Search, list, 360 overview (section 8). | Search uses indexes (assert on the query plan in a test with 50,000 generated parties). |

### A5 Product and Account

| ID | Task | Acceptance criteria |
|---|---|---|
| A5-01 | `Product` management with maker-checker and control-account validation (6.8). | Immutability rules and ledger account type rules enforced. |
| A5-02 | `ProductInterestRate` effective-dating and overlap check (6.8). | Overlap rejected; started rows cannot be edited in place. |
| A5-03 | Account number generation (5.9). | Parallel creation of 1,000 accounts produces unique numbers of the configured length. |
| A5-04 | Account opening (6.9) with `AccountBalance` and `AccountCondition` creation and `AccountOpened`. | One test per rule in 6.9, including joint, business and group shapes and the pending-party block. |
| A5-05 | `AccountBalanceCalculator`; balance read endpoint (5.8). | Unit tests for both formulas including overdraw limit and holds. |
| A5-06 | Freeze, hold, release (6.10) with concurrency handling. | **Concurrency test:** 20 parallel `HOLD_AMOUNT` calls on one account end with `FreezedAmount` equal to the sum, no lost updates, history rows equal to 20. |
| A5-07 | Close, dormancy, classification, reminders (6.10). | Close blocked by balance, holds or pending items; closed accounts reject all changes. |
| A5-08 | Account search and list; account status derivation (6.10). | Status precedence tested; search uses indexes. |

### A6 Hardening and hand-off

| ID | Task | Acceptance criteria |
|---|---|---|
| A6-01 | Performance check: generate 200,000 parties and 300,000 accounts (dev tool). Measure party search, account search, account open. | Party and account search p95 under 300 ms; account open under 500 ms. Results saved in `docs/performance.md`. |
| A6-02 | Security review against the checklist (section 10.3). | Every item verified, with evidence or a ticket. |
| A6-03 | Regenerate `docs/api/openapi.json`; write `docs/runbook.md` (start, migrate, seed, backup, ledger digest, ledger verification). | CI diff check passes. |
| A6-04 | Hand-off document for the transactions phase: which tables and rules posting must honor (Pending parties and accounts, frozen accounts, holds, `AccountBalanceCalculator`, `AccountBalanceDailySnapshot` population at end of day). | Reviewed by the owner. |
| A6-05 | Dev-only demo data command (not run in Production). | Produces a realistic set: individuals, a joint account, a business, a group, products, ledger accounts. |

---

## 10. Testing strategy

### 10.1 Layers
- **Domain unit tests:** KYC policies, maker-checker rules, balance calculator, number formatting, account shape rules.
- **Application tests:** services with an in-memory fake of repositories only where a database adds nothing.
- **Integration tests (Testcontainers):** every endpoint, every constraint, every permission case, the concurrency tests, the ledger immutability tests.

### 10.2 Must-have tests
1. UPDATE and DELETE against `UserActivityLog` and `DataChangeAuditLog` fail.
2. Business change and its audit and activity rows are atomic (force a failure after the business insert and assert nothing was saved).
3. 20 concurrent holds on one account (A5-06).
4. Concurrent party and account number generation.
5. A pending party cannot open an account; a pending product cannot be used.
6. Self-approval is blocked; `AllowSelfApproval = true` makes the app fail to start in Production.
7. Material change resets KYC and supervision.
8. Permission matrix across branches, including `X-Branch-Id`.
9. Session lifecycle: supersede, idle timeout, forced logout, max session length.

### 10.3 Security checklist (A6-02)
- No secret, token or image bytes in logs, audit rows or responses.
- Login errors are indistinguishable (unknown user, wrong secret, disabled).
- Lockout works and is derived from the ledger-backed log.
- Every endpoint has a permission attribute (a reflection test fails the build otherwise).
- Identification numbers masked in lists.
- Uploads validated by content, size-limited.
- JWT signing key not in the repo; `AllowSelfApproval` guarded in Production.
- SQL Server TDE or disk encryption noted in the runbook as an infrastructure requirement.

---

## 11. Definition of done (Phase A)

- Every task in section 9 meets its acceptance criteria in CI.
- DBML, migrations and the model snapshot agree.
- OpenAPI document committed and current.
- Ledger spike ADR committed.
- Hand-off document (A6-04) accepted.

---

## 12. Decisions (defaults used by this plan) and open questions

### 12.1 Decisions

| # | Decision | Change by editing |
|---|---|---|
| D1 | Maker-checker applies to creation, and to controlled-field edits (R4). Rejecting a re-pended edit is not supported. | 5.4 |
| D2 | Reads are institution-wide; writes to branch-owned data need the entity's branch to equal the effective branch. | 5.5 |
| D3 | KYC verification and supervision are one action by a KYC officer or supervisor who did not create or last change the record. Verifying confirms the details and approves them together. | 6.5 |
| D4 | Products are institution-wide. `Product.BranchId` records the owning branch and does not restrict use. | 6.8 |
| D5 | The `AccountBalance` formulas in 5.8 are the definition. Minimum balance is enforced at debit time. | 5.8 |
| D6 | Group accounts and groups as `PRIMARY` holders are allowed. | 6.9 |
| D7 | Fail closed on activity-log write failure for sensitive reads. | 5.6 |
| D8 | An Individual can be an active member of only one group at a time. | 6.4 |
| D9 | `EXPIRED` KYC status is computed, not stored, in this phase. | 6.5 |
| D10 | Direct account opening is limited to `SAVINGS` and `CURRENT`. | 4, 6.9 |

### 12.2 Open questions for the owner

| # | Question |
|---|---|
| Q1 | Is a product defined per branch or for the whole institution? (Plan assumes institution-wide, D4. `ProductCode` is globally unique in the DBML, which only works for institution-wide products.) |
| Q2 | Resolved: one action covers both supervision and KYC verification (D3). |
| Q3 | `Account.OperatingModeId` and `AccountHolder.OperatingInstructionId` overlap. Which is authoritative? (Plan: the account-level mode; the holder-level field stays unused.) |
| Q4 | Can an individual belong to more than one group at once? (Plan: no.) |
| Q5 | Should staff see parties and accounts from all branches (plan) or only their own? |
| Q6 | The regulator's KYC field list (BoG template), accepted ID types, PEP handling, minimum group size and beneficial-owner threshold. All current values are placeholders. |

### 12.3 Noted for later phases (not blocking this one)
- `ProductGLSetting` was not renamed with the rest of the ledger naming (`ProductLedgerSettingDetail` still references it).
- `AccountLedgerEntry` uses `CreditOrDebit` while `LedgerEntry` uses `CrDb`.
- Both ledger entry tables use `LedgerEntryId` as their key name; consider `AccountLedgerEntryId`.
- `StaffUser`, `Account` and others may need `BranchId` handling revisited when inter-branch posting is designed.
