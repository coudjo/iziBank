# Performance check (A6-01)

Targets (implementation plan A6-01): customer search and account search p95 **under 300 ms**; account opening p95
**under 500 ms**, at 200,000 customers and 300,000 accounts.

## How to measure

Use a separate database, never one with real data. The sample data commands refuse to run unless
`DOTNET_ENVIRONMENT=Development`.

1. Point the Migrator at an empty performance database and generate the data (about two million rows; allow 10 to 20
   minutes):
   ```bash
   export ConnectionStrings__Banking='Server=localhost,1433;Database=iziBank_perf;User Id=sa;Password=<…>;TrustServerCertificate=True'
   DOTNET_ENVIRONMENT=Development dotnet run --project src/Banking.Migrator -- perf 200000 300000
   ```
   The generator also creates the SAV and CUR products and their control ledger accounts if they are missing. It
   adds to what is already there, so run it once per database.
2. Start the API against the same database in Release:
   ```bash
   ASPNETCORE_ENVIRONMENT=Development dotnet run -c Release --project src/Banking.Api
   ```
3. Sign in once in the portal as the bootstrap administrator, change the password, and open today's business day
   (Reference > Business day), so account opening is allowed.
4. Run the measurement on the same machine:
   ```bash
   MEASURE_STAFF_CODE=<admin code> MEASURE_SECRET='<password>' python3 scripts/measure-performance.py
   ```
   It warms up each scenario, runs it 50 times (`MEASURE_RUNS`), opens 30 accounts (`MEASURE_OPENS`), and prints the
   table rows below.
5. If a scenario fails, find its statement in Query Store (`ALTER DATABASE [iziBank_perf] SET QUERY_STORE = ON`
   before the run; "Top resource consuming queries"), look at the actual execution plan, and note the scan or missing
   index here with the fix. The audit viewer's "by staff member" query is a known candidate (security review ticket T3).

## Data shape

Deterministic, so runs can be compared (see `PerformanceDataGenerator`):

- 88% individuals, 8% businesses, 2% groups, 2% waiting for KYC verification (no accounts).
- Names from 40 common Ghanaian family names and 20 first names per gender, so a family name matches 2,900 to 5,700
  customers (Mensah is among the largest, which is why the script searches it) and a full name up to about 150.
- Every approved customer has one account; individuals in party order get a second, current account until the
  total is reached; one in ten second accounts is joint. Businesses and groups have a signatory.
- Every individual has a Ghana Card; every approved individual also has an approved specimen signature. Balances are zero.

## Results

Fill in after each measured run. Keep earlier runs; add a new block.

### Run 1: <date>

| Item | Value |
| --- | --- |
| Machine | <CPU, RAM, disk> |
| SQL Server | <edition, version, memory limit> |
| Data | <parties> customers, <accounts> accounts (generator: perf 200000 300000) |
| API | Release, Development environment, same machine |

| Scenario | Runs | p50 ms | p95 ms | max ms | Target p95 | Result |
| --- | --- | --- | --- | --- | --- | --- |
| Customer search: common family name | | | | | < 300 | |
| Customer search: full name | | | | | < 300 | |
| Customer search: customer number | | | | | < 300 | |
| Customer search: mobile number | | | | | < 300 | |
| Customer search: pending KYC filter | | | | | < 300 | |
| Customer search: page 200 | | | | | < 300 | |
| Account search: name | | | | | < 300 | |
| Account search: account number prefix | | | | | < 300 | |
| Account search: frozen filter | | | | | < 300 | |
| Open account (single holder) | | | | | < 500 | |

Findings and changes made:

- <none yet>
