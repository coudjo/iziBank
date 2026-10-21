# Security review (A6-02)

Reviewed 2026-10-21 against the Phase A checklist (implementation plan 10.3), plus the issues found on the way.
Each item has evidence (a test or the code that enforces it) or a ticket.

## Checklist

| Item | Status | Evidence |
| --- | --- | --- |
| No secret, token or image bytes in logs, audit rows or responses | Verified | `AuditTrailTests.Secret_hashes_and_image_bytes_never_reach_the_audit_log`. `AuditRedaction` writes `[redacted]` for `*Hash`, `*Secret*`, `*Password*` and a size for binary values; `ActivityLogWriter` drops detail keys containing secret, password, otp or token. The request log row records the route template, never the query string or body (`ActivityLoggingMiddleware`). EF sensitive-data logging is off. Staff responses carry no hash. |
| Login errors are indistinguishable (unknown user, wrong secret, disabled) | Fixed, verified | Same `auth.invalid-credentials` for all three: `AuthFlowTests.Wrong_secret_unknown_code_and_disabled_user_get_the_same_401_and_are_logged`. **Found:** unknown and disabled codes skipped the password hash, so they answered in about a millisecond while a real code took the cost of PBKDF2; timing told an attacker which staff codes exist. **Fixed:** they now check one hash too (`AuthService.RejectAtEqualCost`), verified by `AuthFlowTests.Unknown_and_disabled_codes_still_check_one_password_hash_so_timing_reveals_nothing`. |
| Lockout works and is derived from the ledger-backed log | Verified | `AuthFlowTests.Five_failures_lock_the_code_even_with_the_right_secret`. `AuthService.LockoutRemainingAsync` counts `LOGIN_FAILED` rows in `UserActivityLog` (append-only) since the later of the window start and the last success; index on `(EventTypeId, EntityId, EventTimestamp)`. Unknown codes lock the same way, so lockout does not reveal which codes exist either. |
| Every endpoint has a permission attribute (a reflection test fails the build otherwise) | Verified | `PermissionTests.Every_api_endpoint_declares_its_access_rule` reads every route from `EndpointDataSource` and fails on any without `RequirePermission`, `AuthenticatedOnly` or explicit anonymous access. |
| Identification numbers masked in lists | Verified | `PartyDocumentTests.Lists_mask_numbers_and_the_primary_ID_cannot_be_removed`; `PartySearch` and `PartyDocuments` use `PartyRules.MaskIdentificationNumber`. Full numbers only on the single-record reads, which are logged as sensitive reads (fail closed, D7). |
| Uploads validated by content, size-limited | Verified | `PartyDocumentTests.Uploads_are_checked_by_content_size_and_declared_type`. `FileSignatures.Detect` reads magic bytes; `RequestBody.ReadAsync` stops reading at the limit (5 MB images, 1 MB signatures); the declared type must match the content. |
| JWT signing key not in the repo; `AllowSelfApproval` guarded in Production | Fixed, verified | `appsettings.json` has no key; the API refuses to start without one of 32+ characters (`Program.cs`). `SupervisionOptionsTests.Self_approval_refuses_to_start_outside_development_and_test`. **Found:** `.env.example` carried a real-looking random key that the API would accept, so copying the example to a server would have used a public key. **Fixed:** the example is empty, with the command to generate one; the API refuses to start until it is set. |
| SQL Server TDE or disk encryption noted in the runbook as an infrastructure requirement | Done | `docs/runbook.md`, "Infrastructure requirements". |

## Other findings

| Finding | Status | Change |
| --- | --- | --- |
| API responses had no caching rules, so browsers and proxies could keep customer details, photos and document images on disk | Fixed | Every response carries `Cache-Control: no-store` and `X-Content-Type-Options: nosniff` (`Program.cs`); `ApiHostTests.Responses_are_never_cached_and_never_sniffed`. |
| The OpenAPI description was served anonymously in Production | Fixed | Mapped only in Development, or when `OpenApi:Expose` is set (`scripts/export-openapi.sh` sets it); `ApiHostTests.The_api_description_is_not_published_outside_development`. |
| Behind nginx, every session and activity row would record nginx's address as the client IP | Fixed | Forwarded headers are read from proxies listed in `Network:TrustedProxies` (CIDR) only; with none listed the connection address is used. Set it in deployment (runbook). |
| No limit on login attempts across many staff codes from one address (password spraying) | Ticket T1 | Lockout is per staff code. Add an IP-partitioned rate limiter on `POST /auth/login` once the trusted proxy setting is in place in each environment, so the real client address is the partition key. |
| Staff search sends a name in the query string (portal P15); audit queries send account and customer numbers (portal P21) | Ticket T2 | Query strings reach proxy and server access logs. Move these filters to POST bodies, as `POST /parties/search` and `POST /accounts/search` already do. |
| The activity log has no index for "by staff member, by date", which the audit viewer uses | Ticket T3 | Add `(StaffUserId, EventTimestamp)` to `UserActivityLog` if A6-01 shows the query is slow at volume. |
