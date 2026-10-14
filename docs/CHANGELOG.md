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
