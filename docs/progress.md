# Progress

| Task | State | Date | Verified by |
|---|---|---|---|
| A0-01 to A0-05 | done (earlier agent) | 2026-10-11 | not re-verified |
| A1-01 to A1-04 | done (earlier agent) | 2026-10-11 | not re-verified |
| S1 corrections (role groups, signatures, idempotency, KYC ADR) | done | 2026-10-14 | owner: dotnet test 39/39 |
| A2-01, A2-02, A2-07 | done | 2026-10-14 | owner: dotnet test 66/66 |
| A2-04, A2-05, A2-06 (+ session viewer from A2-08) | done | 2026-10-14 | owner: dotnet test 87/87 |
| A2-09, A2-10 | done | 2026-10-14 | owner: dotnet test green (94) |
| A2-03, A2-08 | done | 2026-10-14 | owner: dotnet test green |
| A3-01, A3-02, A3-03 | done | 2026-10-14 | owner: dotnet test green |
| A4-01 | done | 2026-10-15 | owner: dotnet test green |
| A4-02, A4-03 (+ audit MERGE fix) | done | 2026-10-15 | owner: dotnet test green |
| A4-04 (+ idempotency for photo and documents) | done | 2026-10-15 | owner: dotnet test green |
| A4-09, A4-11 | done | 2026-10-15 | owner: dotnet test green |
| A4-05 | done | 2026-10-15 | owner: dotnet test green |
| A4-06, A4-07 | done | 2026-10-15 | owner: dotnet test green |
| A4-08 | done | 2026-10-15 | owner: dotnet test green (191) |
| A5-01, A5-02 | done | 2026-10-17 | owner: dotnet test green |
| A5-03, A5-04, A5-05 | done | 2026-10-17 | owner: dotnet test green |
| A5-06, A5-07, A5-08 | done | 2026-10-17 | owner: dotnet test green |
| A6-02, A6-03, A6-04, A6-05, A6-01 (tooling) | done; A6-01 measurement still to run | 2026-10-21 | owner: build and test green, export-openapi with no drift |
| B0-01 posting schema | done | 2026-10-21 | owner: dotnet test green |
| B0-02 ledger settings, teller limits, TXN permissions, ACCOUNTANT group | done | 2026-10-22 | owner: dotnet test green, OpenAPI exported |
| B1-01, B1-02, B1-03 posting engine; B3-01, B3-02 transfers | done | 2026-10-22 | owner: dotnet test green (302) |
| B2-01, B2-02, B2-03, B2-04 tills, vault, cash deposit and withdrawal, till close | done | 2026-10-22 | owner: dotnet test green, migration 0007, OpenAPI exported |
| B4-01 reversals, B4-02 journals | done | 2026-10-22 | owner: build and test green |
| B0-03 charge rules | written; waiting for owner build, test and migration 0008 | 2026-10-22 | migration 0008_ChargeRules to generate |
| B5-01 charges on withdrawals and transfers | written; waiting for owner build, test and OpenAPI export | 2026-10-22 | no migration; `docs/api/openapi.json` re-export for the quote endpoint |
| B5-02 shortfall policies and arrears | written; periodic fees not yet; waiting for owner build, test, migration 0008 and OpenAPI export | 2026-10-22 | migration 0008 includes `AccountChargeArrear` |
