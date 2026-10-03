# 08 — Test pyramid, realistic seed data, OWASP Top 10:2025

## Problem
Seven milestones in, the system has many unit tests, real-database integration tests and component tests. But **nothing has ever exercised it the way a user does**: browser → Keycloak login → API → SQL Server, in containers built from our own Dockerfile and compose file.
- The Keycloak issuer wiring (M3) and the compose healthchecks are still **unverified**.
- The app starts empty, so a demo or an interview walkthrough shows blank tables.
- The security posture is spread across milestones and was never reviewed as a whole against the **OWASP Top 10:2025**. The current edition's numbering differs from the 2021 list some earlier docs cite.

## Decision 1 — The test pyramid, and where E2E runs

| Layer | Tool | Count (approx.) | Runs |
|---|---|---|---|
| Domain unit | xUnit | hundreds | every push, locally |
| Integration (real SQL Server) | xUnit + Testcontainers + WebApplicationFactory | ~100 | every push, CI |
| Component | Vitest (Angular TestBed) | ~200 | every push, both time zones |
| **End-to-end** | **Playwright + axe** | **a handful of journeys** | **every push, CI, against `docker compose`** |

- **E2E is few and slow by design.** It proves the *wiring* (login, cookies, CSRF, proxying, containers). Business rules are already proven lower down.
- **E2E runs only in CI.** The dev laptop doesn't install browsers (the owner's constraint). CI runs `npx playwright install --with-deps chromium`, then `docker compose up -d --wait`, which builds our image and starts SQL Server and Keycloak, and runs the tests against `http://localhost:8080`.
  - **This is the first time the Keycloak/compose wiring is exercised for real.** Failures here are expected and valuable.
- **The journeys:**
  1. **Login:** an anonymous visit to `/assets` → Keycloak login form → back on `/assets`, signed in. Sign out → anonymous.
  2. **Work order lifecycle across three people**, each in a separate browser context so their sessions are separate:
     - Olivia (operator) raises a work order;
     - Sam (supervisor) approves it and assigns Tom;
     - Tom (technician) reserves a part, starts and completes;
     - Sam closes.
     - Check: the history shows each step by the right person, and stock went down after completion (eventual consistency, so the test polls the part with a timeout).
  3. **Permissions:** Olivia sees no "Reports" link, and calling `/api/reports/mttr` from her session returns 403.
  4. **Accessibility smoke:** `@axe-core/playwright` scans the asset list, the work order detail and the raise form, and fails on any *serious* or *critical* WCAG 2.1 A/AA violation.
- **Selectors:** role- and label-based (`getByRole('button', { name: 'Approve' })`), which doubles as an accessibility check. CSS selectors are a last resort.

## Decision 2 — Realistic seed data
- **Switch:** `Seed:Demo=true` (compose sets it). A hosted service runs **after migrations** and seeds only if there are no assets yet, so it's idempotent across restarts.
- **The data:** a Penang SMT/final-assembly factory:
  - about 30 assets across the 4 lines (pick-and-place, reflow ovens, AOI, stencil printers, functional testers, screwdriving cells), with manufacturer and model names as a real plant would have them;
  - 25 spare parts;
  - 8 PM schedules;
  - about 80 historical work orders across 6 months in every status.
- **It goes through the domain**, never raw SQL, so audit rows, outbox events, maintenance records and reporting facts all come out consistent. Historical timestamps are passed in, which the aggregates already allow because they never read a clock themselves.
- **Users:** the realm import gets **fixed user ids** for Olivia, Tom, Sam and Ada, and the seed upserts them into `identity.Users`. Seeded work orders can then be assigned to Tom before he has ever logged in, and when he does log in, his `sub` matches.

## Decision 3 — Security hardening, closing the gaps found in the review

| Gap | Fix |
|---|---|
| No browser security headers | `SecurityHeadersMiddleware`: **CSP**, `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy` (camera, mic and geolocation off), `Cross-Origin-Opener-Policy: same-origin`, and `frame-ancestors 'none'` (clickjacking). **HSTS** outside Development |
| No rate limiting | ASP.NET Core rate limiter: `login` = fixed window, 10/min per client IP; `api` = token bucket per user (per IP when anonymous), 100 requests per 10 s. **429 problem + `Retry-After`** |
| Supply chain unchecked | CI fails on known-vulnerable NuGet packages (`dotnet list package --vulnerable --include-transitive`) and high-severity production npm advisories (`npm audit --omit=dev --audit-level=high`); **Dependabot** for NuGet, npm and Actions |
| Security events not distinguishable in logs | Structured log events with fixed `EventId`s: sign-in success and failure, 403, CSRF rejection, rate-limit rejection, outbox message parked. M9 turns them into Application Insights alerts |

**The CSP, and its one compromise:**
```
default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com;
font-src 'self' https://fonts.gstatic.com; img-src 'self' data:; connect-src 'self';
frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'
```
- **`script-src 'self'`** has no inline scripts and no `eval`. That's what actually stops XSS from running.
- **`style-src 'unsafe-inline'`** is needed because Angular injects component styles as `<style>` elements.
  - The strict alternative is a per-request nonce: the server rewrites `index.html` with `ngCspNonce`.
  - We record it as a known trade-off rather than add HTML rewriting. *Inline styles can't execute code*, so the risk that remains is CSS-based data exfiltration, which is low here.
- **Google Fonts origins** are allowed because `ng add @angular/material` linked them. Self-hosting the fonts would remove both entries; that's on the "next" list.

## Decision 4 — The OWASP Top 10:2025 walkthrough
`docs/security/owasp-top-10.md` maps each 2025 category to:
- **where it's handled**, as file links;
- **how it's tested**;
- **known gaps**.

It's a living document an interviewer can poke at. The categories, checked against owasp.org on 2026-10-03:
- **A01** Broken Access Control
- **A02** Security Misconfiguration
- **A03** Software Supply Chain Failures
- **A04** Cryptographic Failures
- **A05** Injection
- **A06** Insecure Design
- **A07** Authentication Failures
- **A08** Software or Data Integrity Failures
- **A09** Security Logging and Alerting Failures
- **A10** Mishandling of Exceptional Conditions

## Out of scope
- Penetration testing.
- WAF / Front Door (M9 may mention).
- Load testing beyond the M7 harness.
