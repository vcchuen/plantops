# Study guide 08: Test pyramid, demo seed, OWASP Top 10:2025

> Goal: you can explain what each test layer proves and costs, describe the first end-to-end run and what it found, defend the CSP line by line, and walk an interviewer through any OWASP 2025 category using `docs/security/owasp-top-10.md`.

---

## 1. Concepts from first principles

### 1.1 The test pyramid: what each layer *proves*

| Layer | Proves | Doesn't prove | Speed |
|---|---|---|---|
| Domain unit tests | Business rules and invariants | Anything about SQL, HTTP or the browser | milliseconds |
| Integration (Testcontainers SQL Server + WebApplicationFactory) | Real SQL behaviour (unique indexes, rowversion, migrations), HTTP contracts, authorisation | That the real IdP, the containers and the browser fit together | seconds |
| Component (Vitest) | Angular rendering and logic, in two time zones | Real HTTP, real CSS | seconds |
| **End-to-end (Playwright + axe)** | **The wiring:** Keycloak login, cookies, CSRF, the container image, compose networking, the real SPA | Edge-case business rules (too slow to enumerate here) | minutes |

- The pyramid's shape is economic: many cheap tests at the bottom, a few expensive ones at the top.
- Each bug class lands in exactly one layer. That was demonstrated earlier:
  - lab 07's range bug passed every unit test and only integration catches it;
  - this milestone's issues (§2) **only** E2E could catch.

### 1.2 Role-based locators
- `page.getByRole('button', { name: 'Approve' })` finds elements the way a screen reader does. If the test can't find your button by its accessible name, neither can a blind user.
- E2E selectors double as an accessibility check, for free.

### 1.3 Automated accessibility scans (axe)
- `@axe-core/playwright` runs the axe rule engine on the live page against the WCAG 2.1 A/AA tags, and fails on *serious* or *critical* findings.
- **It catches** missing labels, low contrast, invalid ARIA and duplicate ids.
- **It can't judge** whether keyboard flows make sense, or whether a label is *meaningful*. That still needs a human.

### 1.4 Content Security Policy
A CSP tells the browser which sources it may load and execute. Ours:
```
default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com;
font-src 'self' https://fonts.gstatic.com; img-src 'self' data:; connect-src 'self';
frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'
```
- **`script-src 'self'`** is the line that matters. Even if an attacker injects `<script>` (XSS), the browser won't run it: it's inline, and inline isn't allowed.
- **`frame-ancestors 'none'`** means nobody can put PlantOps in an iframe, which stops clickjacking.
- **`connect-src 'self'`:** injected code can't send data to another origin with `fetch`.
- **`'unsafe-inline'` for styles** is the known compromise (Angular injects component `<style>`). Styles can't execute code.

### 1.5 Rate limiting
- **`login`:** 10 per minute per IP, against credential stuffing.
- **`api`:** a token bucket per user, 100 per 10 s, against runaway clients and scrapers.
- A rejected request gets **429 + `Retry-After`**, a well-behaved signal clients can honour.
- Limits are **configuration**, so tests and demos can lift them, and that turned out to matter (§2).

### 1.6 Seeding through the domain
- Raw SQL seed scripts bypass every invariant, the audit trail and the outbox, so reports and maintenance history would be empty or inconsistent.
- Our seeder calls the aggregates' own methods with past timestamps. Events flow, and reporting facts and maintenance records appear through the normal outbox path.

---

## 2. What the first real end-to-end run found

This is the most important section, because it's what E2E is *for*. On push, CI built the image, started SQL Server, Keycloak and the API with `docker compose up --wait`, and ran 9 Playwright tests.

1. **The good news:** 8 of 9 passed on the first try. That included:
   - **a real Keycloak login**, so the issuer/backchannel wiring (`KC_HOSTNAME` + `KC_HOSTNAME_BACKCHANNEL_DYNAMIC`), unverified since M3, works;
   - the `__Host-` cookie on `http://localhost`;
   - the permission checks;
   - **every axe scan.**
2. **Failure 1, in the test:** in Tom's reserve dialog, `getByLabel('Part')` matched **two** elements, the combobox input and the autocomplete's listbox panel. Material labels both with the field label.
   - **Fix:** `getByRole('combobox', { name: 'Part' })`.
   - The same latent bug in the Asset picker was fixed before it bit. It had only passed because the panel hadn't rendered yet.
   - Playwright's **strict mode** (fail when a locator matches more than one element) turned an ambiguity into a clear error instead of a random click.
3. **Failure 2, in CI's integration tests:** the new API rate limit (100 per 10 s per user) answered **429** to the integration tests, whose single test user fires hundreds of requests per second.
   - The limiter worked exactly as designed.
   - **Fix:** the test fixtures lift the limit through configuration, just as compose does for demos.
   - **The lesson:** cross-cutting protections change the behaviour of *everything*, tests included. That's why they must be configurable.
4. **Failure 3, a real product bug only E2E could see:** with the locator fixed, Tom reserved the part and the dialog closed, but the "Reserved parts" table stayed empty.
   - The API log showed the INSERT and then the re-fetch, so the server side was fine.
   - **The cause:** `GET /api/inventory/reservations` returned a paged envelope `{ items, … }`, while the contract (design 05), and so the SPA, expected a plain `ReservationItem[]`. Angular's `value().length` was `undefined`, which isn't `0`, so the table rendered with nothing to iterate.
   - **Why no lower layer caught it:**
     - the backend integration tests asserted the envelope the backend built;
     - the Vitest specs mocked the array the contract promised.
     - **Each half was tested against its own assumption.**
   - **Fix:** the backend now returns the contracted array, and its test asserts `JsonValueKind.Array`.
   - **The durable fix:** generate the TypeScript client from the API's OpenAPI document, so the compiler catches drift. This is the second time drift happened (M4 was the first). It's at the top of "what I'd do next".

---

## 3. Guided code tour

### Security
1. **`src/Host/PlantOps.Api/Security/SecurityHeadersMiddleware.cs`**
   - Headers are set in `Response.OnStarting`, because the exception handler clears headers on error responses. That's why the 401 and 400 tests assert headers too.
2. **`Security/RateLimiting.cs`**
   - One global limiter chooses its policy by path. It's partitioned per user `sub` or per IP. Options are read lazily so test overrides work. 429 problem + `Retry-After`.
   - *Notice:* the "rate limited" log event records `user:<sub>` or `ip`, never the address itself (no PII).
3. **`Program.cs`**
   - Order: forwarded headers → HSTS (non-Development) → security headers → … → authentication → rate limiter → authorisation.
   - Forwarded headers are trusted only when `ForwardedHeaders:TrustAll=true` (App Service, M9).
4. **`BuildingBlocks.Infrastructure/SecurityEvents.cs`**
   - `[LoggerMessage]` source-generated methods with **fixed EventIds 1001–1006**. Fixed ids make alerting rules (M9) stable across code changes.
5. **`Identity/OidcOptionsSetup.cs`, `CsrfGuardMiddleware.cs`, `OutboxProcessor.cs`**: where those events are raised.

### Seed
6. **`Host/Seeding/DemoSeedPlan.cs`, `DemoSeedCatalog.cs`, `DemoSeedGenerator.cs`**
   - *Data*, generated deterministically (seed 20261003) and unit-tested without a database: counts, required tags, no duplicates, enough stock left.
7. **`Host/Seeding/DemoSeeder.cs`**: the only part that touches aggregates. *Notice:*
   - **Two phases.** Phase 1 raises work orders and reserves parts; phase 2 completes, closes and cancels. The outbox dispatcher is already running, so completing first could let Inventory consume a reservation the seeder hadn't finished writing.
   - **The empty-database gate**, and the recovery instruction (`docker compose down -v`).
   - **The honest limitation:** seeded *audit* rows show "System" at seeding time, because the interceptor stamps the real clock and user. The work orders' own columns carry the historical people and times.
8. **`deploy/keycloak/plantops-realm.json`**
   - Fixed user `id`s, so Keycloak's `sub` matches the seeded directory and seeded work orders can be assigned to Tom before he ever logs in.

### End-to-end
9. **`web/e2e/global-setup.ts` + `helpers.ts`**
   - Logs each user in **once** through the real Keycloak form and saves `storageState`.
   - Each test gets per-user browser contexts, so Olivia, Sam and Tom have separate sessions in one test.
10. **`web/e2e/work-order-lifecycle.spec.ts`**
    - The three-person journey.
    - `expect.poll` waits for the stock to drop: **eventual consistency, tested honestly**, with a timeout rather than a sleep.
11. **`web/e2e/a11y.spec.ts`**: an axe scan per page, with the full results JSON attached to the report.
12. **`.github/workflows/e2e.yml`**
    - Compose up with `--wait`.
    - On failure it dumps `ps`, the logs, and the OIDC discovery and readiness endpoints, so the first failure is diagnosable from the artifacts.
    - It always uploads the reports and always runs `down -v`.

### Supply chain
13. **`.github/workflows/ci.yml`**
    - `dotnet list package --vulnerable --include-transitive` *always exits 0*, so the step greps its output.
    - `npm audit --omit=dev --audit-level=high`: production dependencies only.
14. **`.github/dependabot.yml`**: weekly grouped updates for NuGet, npm and Actions.

### The walkthrough
15. **`docs/security/owasp-top-10.md`**
    - Every 2025 category, the file that handles it, the test that proves it, and the gaps (it lists two untested failure paths, honestly).

---

## 4. Why this, not that

| Choice | Instead of | Why |
|---|---|---|
| E2E against `docker compose` in CI | E2E against `ng serve` + a mocked API | Proves the real image, real Keycloak and real cookies; the wiring is what E2E is for |
| E2E only in CI | Also locally | The owner's no-browser-download constraint; CI is the shared truth anyway |
| Role/label locators | CSS selectors | Doubles as an accessibility check; resilient to markup changes |
| `expect.poll` with a timeout | `waitForTimeout(5000)` | Waits exactly as long as needed; fails clearly |
| axe on key pages | Manual audit only | Catches the mechanical WCAG failures on every push; humans still judge the rest |
| CSP with `script-src 'self'` | No CSP / `'unsafe-inline'` scripts | Neutralises injected scripts even if an XSS slips through |
| `'unsafe-inline'` styles | Nonce plumbing | Styles can't execute; nonce rewriting of `index.html` costs more than it buys here |
| Config-driven rate limits | Hard-coded | Tests and demos must lift them (CI proved it) |
| Seeding through the domain | SQL scripts | Audit, outbox, reports and maintenance history stay consistent |
| Fixed EventIds for security logs | Free-text logs | Alert rules survive refactors |

---

## 5. Senior interview questions

<details>
<summary><strong>Q1. What does your E2E suite test, and why so few tests?</strong></summary>

**Model answer:** It proves the wiring that no lower layer can:
- a real Keycloak login and the resulting `__Host-` cookie;
- the CSRF header and role-based access through the real SPA;
- our Docker image and compose networking, including the Keycloak issuer split between `localhost:8081` for the browser and `keycloak:8080` for the API.

There are 9 tests: login and logout, a full work order lifecycle across three users in separate browser contexts (including waiting for eventual consistency on stock), permission checks, and axe accessibility scans on key pages.

Business rules are covered hundreds of times lower in the pyramid, where they're cheap. Each E2E test costs minutes and has more ways to be flaky, so we spend them only on what nothing else can prove.

The first run validated the Keycloak wiring we'd had to leave unverified for five milestones, and it found a real ambiguity in our autocomplete locators.
</details>

<details>
<summary><strong>Q2. Walk me through your Content Security Policy.</strong></summary>

**Model answer:**
- **`script-src 'self'`** is the core: only scripts from our own origin run, with no inline script and no eval. So even if an XSS bug lets an attacker insert `<script>`, the browser refuses to execute it.
- **`connect-src 'self'`** stops injected code from sending data elsewhere.
- **`frame-ancestors 'none'`** prevents clickjacking.
- **`base-uri`, `form-action` and `object-src`** close the classic bypasses.

The compromise is `style-src 'unsafe-inline'`, because Angular injects component styles as `<style>` elements. The strict alternative is a per-request nonce, which means rewriting `index.html` on every request. Styles can't execute code, so I accepted it and wrote it down. Google Fonts origins are allowed too; self-hosting the fonts would remove them.

A test asserts the exact header string, so any loosening shows up in review. The lab does exactly that.
</details>

<details>
<summary><strong>Q3. Your rate limiter broke your own tests. What did you learn?</strong></summary>

**Model answer:** That cross-cutting protections apply to everything, including your test harness.

Our integration tests use one test identity that issues hundreds of API calls per second, and the per-user token bucket (100 per 10 s) correctly answered 429. The fix wasn't to weaken the limiter. It was to make limits configuration-driven: production defaults in code, generous values in the test fixtures and in docker-compose for demos and E2E.

The same reasoning applies to any global middleware: CSRF, CSP and auth all need explicit, documented test configurations. And we test the limiter itself with deliberately tiny limits: the 11th login in a minute gets a 429 with `Retry-After`.
</details>

<details>
<summary><strong>Q4. How do you seed realistic demo data without breaking your invariants?</strong></summary>

**Model answer:** Through the domain, never SQL scripts.
- A deterministic generator produces a plan as plain data: 31 assets across 4 Penang lines, 25 parts, 8 PM schedules, and 80 work orders in every status over six months. It's unit-tested without a database.
- A seeder replays the plan through the aggregates' own methods, passing historical timestamps, which our aggregates accept because they never read a clock.
- Because it goes through the domain, the outbox carries completion events, and the reporting facts and maintenance history fill in through the normal path.
- It runs in two phases so the live outbox dispatcher can't consume a reservation mid-seed.
- It runs only on an empty database.
- Keycloak's realm import uses fixed user ids, so the seeded technician is the same `sub` as the real login.

The limitation, documented: audit rows for seeded history show the seeding time and "System", because the interceptor stamps the real clock and actor.
</details>

<details>
<summary><strong>Q5. Pick any OWASP 2025 category and show me where you handle it.</strong></summary>

**Model answer (A03 Software Supply Chain Failures, new in 2025):**
- Every GitHub Action is pinned to a commit SHA, because a tag can be moved by whoever controls the action's repository. That's how the tj-actions compromise leaked secrets in 2025.
- CI fails on any known-vulnerable NuGet package, including transitive ones. `dotnet list package --vulnerable` always exits 0, so the step parses its output.
- CI fails on high-severity production npm advisories.
- Dependabot opens grouped weekly updates for NuGet, npm and Actions.
- Central Package Management keeps one version per package.
- The gaps I'd close next: an SBOM and image signing in the deploy pipeline.

The full map is `docs/security/owasp-top-10.md`, ten categories, each with files, tests and gaps.
</details>

---

## 6. Break-it lab: loosen the CSP (offline)

1. Run `git checkout -b lab-08`.
2. In `src/Host/PlantOps.Api/Security/SecurityHeadersMiddleware.cs`, make the "harmless" change many teams make when a third-party widget complains:
   ```
   script-src 'self' 'unsafe-inline';
   ```
3. Run `dotnet test tests/PlantOps.Api.Tests --filter "FullyQualifiedName~SecurityHeaders"`.

   **Observe** (verified on this code): **3 of 4 tests fail**: health, SPA fallback, and the API 401/400 responses.
4. Explain why this change matters:
   - With `'unsafe-inline'` in `script-src`, **any** XSS becomes executable again: `<img src=x onerror=...>` and inline `<script>` both run.
   - The CSP was the second line of defence behind Angular's escaping, and this one word removes it.
5. Clean up:
   ```bash
   git checkout -- . && git checkout m8-quality && git branch -D lab-08
   ```

---

## 7. Measured numbers from this milestone
- **CI:** the .NET suite (including the new security, rate-limit and seed tests, and the seed integration test on real SQL Server) and Vitest in two time zones. The counts are in the PR checks.
- **First E2E run:** 8 of 9 passed, with 1 failure (locator ambiguity) fixed in the next commit. The final result is in the PR checks.
- **`dotnet list package --vulnerable`:** no vulnerable packages. **`npm audit --omit=dev`:** 0 vulnerabilities, on 2026-10-03.
