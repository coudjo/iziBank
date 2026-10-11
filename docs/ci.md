# CI steps

Provider-agnostic. Map each step to your host (GitHub Actions, Azure Pipelines, or a local script).

## Required environment

- .NET SDK version from `global.json` (current LTS)
- Docker Engine (integration tests start SQL Server 2022 via Testcontainers)
- No secrets in the repository. JWT and bootstrap values come from the environment or user secrets.

## Steps

1. **Restore**
   ```bash
   dotnet restore
   ```

2. **Build** (warnings are errors)
   ```bash
   dotnet build --no-restore --configuration Release
   ```

3. **Test**
   ```bash
   dotnet test --no-build --configuration Release --verbosity normal
   ```
   Integration tests create one database per test class. Do not add row-level cleanup against ledger tables.

4. **Export OpenAPI and fail on drift**
   ```bash
   ./scripts/export-openapi.sh
   git diff --exit-code -- docs/api/openapi.json
   ```
   `docs/api/openapi.json` is the committed contract. Regenerate it in the same change that alters the API.

## Local equivalent

```bash
dotnet build
dotnet test
./scripts/export-openapi.sh
```
