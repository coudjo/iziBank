# iziBank

Core banking system for a single institution on one shared SQL Server 2022 database. New build: the database goes live empty.

Phase A covers foundation, security, Party / KYC, products, and accounts. The Angular UI is a later phase.

## Layout

| Path | Role |
|---|---|
| `src/Banking.Domain` | Domain model and rules. No EF or ASP.NET. |
| `src/Banking.Application` | Use cases. Depends on Domain. |
| `src/Banking.Infrastructure` | EF Core and adapters. |
| `src/Banking.Api` | ASP.NET Core Web API (`/api/v1`). |
| `src/Banking.Migrator` | Applies migrations and seeds reference data. |
| `tests/` | Domain, application, and Testcontainers integration tests. |
| `docs/schema` | Schema source of truth. |
| `docs/adr` | Architecture decisions. |
| `docs/api` | Committed OpenAPI document. |
| `docker/` | SQL Server 2022 for local development. |

## Documents

- [Schema (DBML)](docs/schema/core-banking.dbml) — source of truth, plus Phase A section 2 amendments
- [Schema (SQL snapshot)](docs/schema/core-banking.sql) — the supplied DDL
- [Phase A decisions](docs/adr/0001-phase-a-decisions.md)
- [Open decisions](docs/adr/open-decisions.md)
- [Changelog](docs/CHANGELOG.md)
- [CI steps](docs/ci.md)
- [OpenAPI](docs/api/openapi.json)

## Prerequisites

- .NET SDK pinned in `global.json` (current LTS, 10.0). If `dotnet --version` is not 10.x, install it from https://dot.net and put that SDK first on `PATH`.
- Docker
- Copy `.env.example` to `.env` and replace placeholders

## Local database

```bash
cp .env.example .env
docker compose -f docker/docker-compose.yml --env-file .env up -d
```

Wait until the `sqlserver` service is healthy, then connect with the connection string in `.env`. The default host port is `14334` so it does not collide with other local SQL Server containers.

## Build and test

```bash
dotnet tool restore
dotnet build
dotnet test
```

Schema migrations live in `src/Banking.Infrastructure/Persistence/Migrations`. `0001` is the EF baseline. `0002` creates the append-only ledger tables with hand-written SQL.

## Working agreement

Task IDs follow the Phase A plan. Business rules live in Domain / Application. Controllers only translate HTTP. Every schema change is a migration, a DBML update, and a changelog line.
