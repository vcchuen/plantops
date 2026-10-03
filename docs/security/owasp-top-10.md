# OWASP Top 10:2025: where PlantOps handles each risk

The categories and names are from <https://top10.owasp.org/2025/>, checked on 2026-10-03. Some earlier milestone docs used **2021** numbering (e.g. "A05 Security Misconfiguration"). In 2025:
- Security Misconfiguration is **A02**;
- Software Supply Chain Failures is new at **A03**;
- Mishandling of Exceptional Conditions is new at **A10**.

For each category below: **how it's handled** (with files), **how it's tested**, and **known gaps**. An interviewer can pick any row and you should be able to open the file and explain it.

---

## A01:2025 Broken Access Control
**How it's handled**
- **Deny by default:** the fallback authorisation policy requires an authenticated user for *every* endpoint, including "no endpoint matched" (`Identity/IdentityModule.cs`). Anonymous endpoints opt out explicitly with `.AllowAnonymous()` (health, login, `/me`, SPA files).
- **Role policies** live in `Identity.Contracts/Policies.cs`: `assets:manage`, `workorders:supervise`, `inventory:manage`, `reports:view`, `reports:rebuild`.
- **Resource-based rules:**
  - only the *assigned* technician (or an admin) may start or complete a work order (`WorkOrders/Authorization/WorkOrderAuthorizationHandler.cs`, `WorkOrderAccess.CanWork`, which *fails closed* on null/empty ids);
  - Inventory reservations reuse the rule through `IWorkOrderDirectory`.
- **Open-redirect protection** on login: `IdentityEndpoints.IsLocalUrl`.
- **Untrusted route ids** are URL-encoded in the SPA before they go into API paths (`asset-detail.page.ts`).
- **The UI never decides:** `allowedActions` comes from the server, and hidden buttons are convenience only.

**Tests**
- `IdentityHostTests`: 401 vs 403, the open-redirect cases, and an operator getting 403 on manage-assets.
- The authorisation matrix in `WorkOrdersApiTests`, the `WorkOrderAccessTests` null cases (lab 04), and E2E `permissions.spec.ts`.

**Gaps:** none known.

## A02:2025 Security Misconfiguration
**How it's handled**
- **Security headers** on every response (`Host/Security/SecurityHeadersMiddleware.cs`): CSP, `nosniff`, `Referrer-Policy`, `Permissions-Policy`, `COOP`, and `frame-ancestors 'none'`.
- **HSTS** outside Development.
- **Forwarded headers** are trusted only when explicitly configured.
- **Error responses:**
  - errors are RFC 9457 problem details with **no stack traces or exception text** (`Http/ApiExceptionHandler.cs`, `HealthResponseWriter.cs`);
  - the framework's `BadHttpRequestException` messages are replaced with a generic one.
- **OpenAPI** is mapped in Development only.
- **The container runs as non-root** (`USER $APP_UID` in the `Dockerfile`).
- **No secrets in source:** user-secrets locally, `.env` (gitignored) for compose, Key Vault in Azure (M9).

**Tests:** `SecurityHeadersTests` (exact CSP), and `HostTests` (no exception text in health or problem responses).

**Gaps:** `style-src 'unsafe-inline'` (Angular component styles) and the Google Fonts origins. See design 08, Decision 3.

## A03:2025 Software Supply Chain Failures
**How it's handled**
- **GitHub Actions are pinned to commit SHAs**, not tags (`.github/workflows/*.yml`).
- **CI fails on known-vulnerable packages:** NuGet (`dotnet list package --vulnerable --include-transitive`) and production npm advisories (`npm audit --omit=dev --audit-level=high`).
- **Dependabot** for NuGet, npm and Actions (`.github/dependabot.yml`).
- **Central Package Management:** one version per package (`Directory.Packages.props`).
- **Lockfiles:** `npm ci` uses `package-lock.json`.
- **Base images** come from Microsoft's registry (`mcr.microsoft.com/dotnet/*`).

**Gaps:** no SBOM generation or image signing yet. An SBOM is a cheap addition to the M9 pipeline.

## A04:2025 Cryptographic Failures
**How it's handled**
- **TLS everywhere outside local compose**, with HSTS, and the session cookie set to `Secure`.
- **The session cookie** is encrypted and signed by ASP.NET Core Data Protection.
- **No tokens in the browser** (BFF, ADR-0006).
- **Passwords are never handled:** the IdP owns credentials.
- **Optimistic-concurrency ETags** are row versions, not secrets, so exposing them is fine.

**Gaps:** the Data Protection key ring is container-local, so a restart logs everyone out. M9 moves it to Blob Storage, protected by Key Vault.

## A05:2025 Injection
**How it's handled**
- **SQL:** all data access goes through EF Core with parameters.
  - The single raw SQL in production code (the outbox claim, `OutboxProcessor`) interpolates only model metadata and constants, and the suppression of EF1002 is justified in a comment.
  - LIKE wildcards in search are escaped by EF (`StartsWith`/`Contains`, lab 02).
- **XSS:** Angular escapes all interpolation, there's no `bypassSecurityTrust*` in `main`, and the CSP blocks inline scripts.
- **Spreadsheet formula injection** in the Excel export (`Reporting/Export/SpreadsheetText.cs`, lab 07).
- **Deserialisation:** outbox event types resolve only through an allow-list (`IntegrationEventRegistry`), never `Type.GetType(string)`.

**Tests:** `SpreadsheetTextTests`, the wildcard search integration test, `IntegrationEventRegistryTests`, and the workbook round-trip test.

**Gaps:** none known.

## A06:2025 Insecure Design
**How it's handled**
- **Invariants live in aggregates**, so every caller gets them (M2, M4).
- **Concurrency is designed in:**
  - ETag/If-Match on work orders (ADR-0008);
  - rowversion + retry on stock;
  - unique indexes for the asset-tag and PM-generation races.
- **Rate limits** on login and the API (`Host/Security/RateLimiting.cs`).
- **Paging is capped** at 100 on every list.
- **Range caps** on reports (366 days).
- **ADRs** record trade-offs and their failure modes.

**Tests:** the race tests (tag, last unit, ETag, PM runners) against real SQL Server, and `RateLimitingTests`.

## A07:2025 Authentication Failures
**How it's handled**
- **Delegated to an OIDC IdP** (Keycloak locally, Entra ID in production): Authorization Code + PKCE, `state` and `nonce` (`Identity/OidcOptionsSetup.cs`).
- **Brute-force protection** is enabled in the realm (`deploy/keycloak/plantops-realm.json`), and the login endpoint is rate-limited.
- **Session cookie:** `__Host-` prefix, `HttpOnly`, `Secure`, `SameSite=Lax`, 8 h sliding lifetime.
- **Logout** is a POST that clears the local session and ends the IdP session.
- **CSRF:** `SameSite` plus a mandatory `X-CSRF` header on unsafe requests (`CsrfGuardMiddleware.cs`).

**Tests:** the PKCE/state/nonce redirect test, the logout test, CSRF 400 tests, E2E `auth.spec.ts` (real Keycloak), and the login rate-limit test.

**Gaps:** MFA is the IdP's job. Require it in Entra Conditional Access for supervisors and admins.

## A08:2025 Software or Data Integrity Failures
**How it's handled**
- **Audit trail written atomically** with each change (ADR-0007).
- **Transactional outbox and idempotent inbox:** no lost or duplicated side effects (ADR-0009).
- **Allow-listed event deserialisation.**
- **Service Bus duplicate detection** keyed on the outbox id.
- **Optimistic concurrency** prevents lost updates.
- **The perf harness only reads** what it seeds into its own container.

**Tests:** the redelivery idempotency tests, `DomainEventInterceptor` audit assertions, and the 412 race test.

## A09:2025 Security Logging and Alerting Failures
**How it's handled**
- **Structured security events with fixed event ids**, category `PlantOps.Security` (`BuildingBlocks.Infrastructure/SecurityEvents.cs`):

  | Id | Event |
  |---|---|
  | 1001 | sign-in succeeded |
  | 1002 | sign-in failed |
  | 1003 | access denied |
  | 1004 | CSRF rejected |
  | 1005 | rate limited |
  | 1006 | outbox message parked |

- **No tokens, cookies, bodies or e-mail addresses are logged.** Users are identified by `sub` only.
- **A semantic audit trail per module** (`AuditEntries`).

**Tests:** `SecurityEventsTests`.

**Gaps:** alerts don't exist until M9, which routes these event ids to Application Insights alert rules.

## A10:2025 Mishandling of Exceptional Conditions
**How it's handled**
- **One global exception handler:**
  - domain, not-found and conflict exceptions map to 400/404/409;
  - everything else becomes a generic 500 with no details;
  - logs keep the details.
- **Fail closed:**
  - authorisation helpers deny on missing data;
  - the SPA treats a failing `/me` as "unknown", not "anonymous", so there's no redirect loop.
- **Degrade, don't crash:**
  - logout succeeds locally even if the IdP is unreachable;
  - the outbox parks poison messages after 5 attempts instead of blocking the queue;
  - the Service Bus consumer throws so the broker retries and dead-letters;
  - the escalation runner skips a work order on a concurrency conflict and retries next run.
- **Transient faults:** `EnableRetryOnFailure` for SQL, with idempotent units of work where whole blocks are retried.

**Tests:** the host error-shape tests (`HostTests`), the logout test (`IdentityHostTests`), and the handler-failure aggregation in `IntegrationEventRegistryTests`.

**Gaps:** two paths have no dedicated test yet:
- outbox parking after 5 attempts;
- logout while the IdP is unreachable.

Both are cheap to add, and listed in "what I'd do next".
