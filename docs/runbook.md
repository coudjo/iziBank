# Runbook (A6-03)

How to start, migrate, seed, back up and check the iziBank API and database. Commands assume the repository root
and SQL Server 2022. Replace `<…>` placeholders; never put real secrets in the repository.

## Infrastructure requirements

- **Encryption at rest:** SQL Server Transparent Data Encryption (TDE) on the `iziBank` database, or full-disk
  encryption on every volume that holds data files, log files and backups. Backups must be encrypted too (TDE
  encrypts them; otherwise use `WITH ENCRYPTION`). See "Turning on TDE" below.
- **TLS:** the portal and API are served over HTTPS by the reverse proxy; the API connection string uses
  `Encrypt=True`.
- **Secrets:** the JWT signing key, the connection string and the bootstrap administrator password come from the
  environment or a secret store, never from files in the repository.
- **Time:** servers keep UTC. Ghana is UTC+0, and business dates are dates only.

## Configuration

| Setting | Where | Notes |
| --- | --- | --- |
| `ConnectionStrings__Banking` | API, Migrator | `Server=…;Database=iziBank;…;Encrypt=True` |
| `Jwt__SigningKey` | API | At least 32 random characters (`openssl rand -base64 48`), different per environment. The API refuses to start without it. Changing it signs everyone out. |
| `ASPNETCORE_ENVIRONMENT` | API | `Production` outside development. Production refuses to start with `Supervision:AllowSelfApproval=true`. |
| `Network__TrustedProxies__0` | API | CIDR of the reverse proxy (for example the Docker network `172.18.0.0/16`), so session and activity rows record the real client address. Leave unset when nothing sits in front of the API. |
| `BOOTSTRAP_ADMIN_CODE`, `BOOTSTRAP_ADMIN_SECRET`, `BOOTSTRAP_BRANCH_CODE`, `BOOTSTRAP_BRANCH_NAME` | Migrator | First administrator and head-office branch. The administrator must change the password at first sign-in. |
| `Calendar:WeekendDays` | API | Saturday and Sunday by default. |

## Start a fresh environment

1. Create the database server and an empty `iziBank` database (or let the Migrator create it).
2. Migrate and seed (idempotent; running it again changes nothing that exists):
   ```bash
   dotnet run --project src/Banking.Migrator
   ```
3. Turn on snapshot isolation, which ledger verification needs:
   ```sql
   ALTER DATABASE [iziBank] SET ALLOW_SNAPSHOT_ISOLATION ON;
   ```
4. Start the API (published build):
   ```bash
   dotnet publish src/Banking.Api -c Release -o out/api
   ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_HTTP_PORTS=8080 dotnet out/api/Banking.Api.dll
   ```
5. Start the portal image (`iziBank-portal/docker`), which serves the app and proxies `/api/` to the API.
6. Sign in as the bootstrap administrator, change the password, then create staff and role groups. Before going
   live: complete the institution profile, branches, currencies, holidays, products and chart of accounts, and
   open the first business day in each branch.

## Demo data (development only)

For trying the portal on a development database. Refused unless `DOTNET_ENVIRONMENT=Development`, and does nothing
if the database already has customers.

```bash
DOTNET_ENVIRONMENT=Development DEMO_STAFF_SECRET='<a password for demo staff>' \
  dotnet run --project src/Banking.Migrator -- demo
```

It adds staff TELLER1, FIELD1, KYC1, SUP1 and AUDIT1 (one role group each, all signing in with `DEMO_STAFF_SECRET`),
savings (SAV) and current (CUR) products with their control ledger accounts, eight individuals, a business with a
signatory, a group with three members, nine accounts (single, joint, business, group, one frozen, one dormant, one
waiting for approval), a customer waiting for KYC verification, one with an expired ID, and an open business day.
Bulk data for measuring is `-- perf`; see `docs/performance.md`. Neither command exists in a Production run: both
stop before connecting.

## Upgrade

1. Back up (below).
2. Stop the API.
3. `dotnet run --project src/Banking.Migrator` with the new release. Migrations only move forward; there is no
   automatic down-migration. To roll back, restore the backup taken in step 1.
4. Start the new API, then the new portal.

## Backup and restore

Use the full recovery model with log backups, so a restore can reach a point in time.

```sql
ALTER DATABASE [iziBank] SET RECOVERY FULL;

-- Nightly full backup
BACKUP DATABASE [iziBank] TO DISK = N'/var/opt/mssql/backup/iziBank_full.bak'
  WITH COMPRESSION, CHECKSUM, INIT;

-- Every 15 minutes
BACKUP LOG [iziBank] TO DISK = N'/var/opt/mssql/backup/iziBank_log.trn'
  WITH COMPRESSION, CHECKSUM;

-- Check a backup can be read
RESTORE VERIFYONLY FROM DISK = N'/var/opt/mssql/backup/iziBank_full.bak' WITH CHECKSUM;
```

- Copy backups off the database server the same day, to storage the database server cannot delete from.
- With TDE, back up the TDE certificate and its private key (below) and store them apart from the backups. A backup
  cannot be restored on another server without them.
- Rehearse a restore to a separate server every quarter, then run ledger verification (below) on the restored copy.

## Ledger digest

`UserActivityLog` and `DataChangeAuditLog` are SQL Server append-only ledger tables. SQL Server chains every change
into blocks; a digest is the hash of the latest block. Keeping digests where the database cannot change them proves
later that no ledger row was altered or removed, even by someone with full database rights.

Generate a digest at least daily (after the nightly backup) and store the JSON output in write-once storage (for
example an object-store bucket with retention lock), named by date:

```sql
EXECUTE sp_generate_database_ledger_digest;
```

The result is one JSON object, for example
`{"database_name":"iziBank","block_id":1234,"hash":"0x…","last_transaction_commit_time":"…","digest_time":"…"}`.

## Ledger verification

Run monthly, after every restore, and whenever tampering is suspected. Pass every stored digest since the last
verification as a JSON array:

```sql
EXECUTE sp_verify_database_ledger N'[
  {"database_name":"iziBank","block_id":1200,"hash":"0x…","last_transaction_commit_time":"…","digest_time":"…"},
  {"database_name":"iziBank","block_id":1234,"hash":"0x…","last_transaction_commit_time":"…","digest_time":"…"}
]';
```

It completes without error when the ledger matches the digests, and fails with an error naming the table and block
when it does not. A failure is a security incident: keep the database as it is, take a copy, and investigate before any repair.
To check one table only, add `@table_name = N'dbo.UserActivityLog'`.

## Turning on TDE

```sql
USE master;
CREATE MASTER KEY ENCRYPTION BY PASSWORD = '<strong password>';
CREATE CERTIFICATE iziBankTde WITH SUBJECT = 'iziBank TDE';
BACKUP CERTIFICATE iziBankTde TO FILE = '/var/opt/mssql/backup/iziBankTde.cer'
  WITH PRIVATE KEY (FILE = '/var/opt/mssql/backup/iziBankTde.pvk',
                    ENCRYPTION BY PASSWORD = '<another strong password>');

USE [iziBank];
CREATE DATABASE ENCRYPTION KEY WITH ALGORITHM = AES_256 ENCRYPTION BY SERVER CERTIFICATE iziBankTde;
ALTER DATABASE [iziBank] SET ENCRYPTION ON;

-- Progress: encryption_state 3 means encrypted
SELECT DB_NAME(database_id) AS db, encryption_state, percent_complete FROM sys.dm_database_encryption_keys;
```

Move the certificate and key files off the server immediately, into the secret store.

## Day-to-day operations

- **Business day:** a supervisor opens each branch's day from the portal (Business day). Closing only marks the day
  closed in this phase; end-of-day processing arrives with transactions.
- **Signing someone out:** Administration > Sessions > Force sign-out. Disabling a staff member also ends their
  sessions.
- **Locked staff code:** lockout lifts on its own after the lockout period (`Security:LockoutDurationMinutes`). An
  administrator can also reset the password, which the person changes at next sign-in.
- **Rotating the JWT signing key:** set the new key and restart the API. Everyone is signed out.
- **Health:** the portal shows "iziBank could not start" when it cannot load its configuration. API logs go to
  standard output (Serilog); they hold no request bodies, query strings or secrets.
