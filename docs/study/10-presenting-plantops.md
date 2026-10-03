# Study guide 10: Presenting PlantOps in a senior interview

> The code is done. This guide is about **telling the story**: in 2 minutes, in 10 minutes, and under hostile questioning. Read the README first, then practise this out loud.

## 1. The 2-minute version (memorise the shape, not the words)
1. **Problem:** machine downtime in a Penang electronics plant. Repairs must be fast, accountable and auditable.
2. **What it does:**
   - an operator reports a fault, a supervisor approves and assigns, a technician fixes it against a priority SLA;
   - spare parts are reserved and consumed;
   - preventive maintenance raises its own work orders;
   - reports on MTTR, SLA compliance and downtime go to Excel.
3. **Architecture:** a .NET 9 modular monolith with five modules, each owning its schema and talking through contracts and an outbox, plus an Angular 22 SPA using signals, Signal Forms and a BFF cookie login.
4. **What I'm proud of:**
   - concurrency handled per use case (412 vs retry vs unique index);
   - every performance claim measured in CI (the index cut reads by 94.6 %; I *rejected* a compiled query because the benchmark said so);
   - E2E against real Keycloak caught a contract bug that both unit-test suites missed.
5. **Honest limits:**
   - written for Azure but not deployed;
   - .NET 9 STS because of a tooling constraint, with the upgrade path documented.

## 2. The 10-minute walkthrough (screen share)

| Minute | Show | Say |
|---|---|---|
| 0–1 | README GIF | The user journey in 20 seconds |
| 1–3 | Mermaid diagram, then `src/Modules` | Modular monolith; `internal` + Contracts; the architecture test (ADR-0001/0002) |
| 3–5 | `WorkOrder.cs`, `WorkOrderEndpoints.Execute` | State machine in the aggregate, ETag/If-Match, resource-based authorisation, `allowedActions` |
| 5–6 | `DomainEventInterceptor`, `OutboxProcessor` | Audit + outbox in one transaction; at-least-once delivery, exactly-once effect |
| 6–7 | `docs/study/07` measured tables | "Here's the plan that told me my first index was wrong" |
| 7–8 | `IdentityModule`, `OidcOptionsSetup` | BFF: no tokens in the browser; CSRF; fallback policy |
| 8–9 | `web/e2e`, a CI run | Three users in separate contexts; axe; what the first run found |
| 9–10 | `docs/security/owasp-top-10.md`, "What I'd do next" | Security as a map, not a claim; the honest backlog |

## 3. Questions you'll get, and where your answer lives

| Question | Your answer is in |
|---|---|
| Why not microservices? | ADR-0001; study 01 Q1 |
| How do two supervisors not overwrite each other? | ADR-0008; study 04 Q2 |
| Why is the stock reservation retried but the work order refused? | Study 05 §1.6 |
| How do modules communicate reliably? | ADR-0009; study 05 Q1–Q2 |
| How did you pick indexes? | Study 07 §2.2 |
| Where are tokens stored? | ADR-0006; study 03 Q1 |
| What's your test strategy? | Study 08 §1.1 and §2 |
| What does it cost on Azure? | `docs/azure-cost.md`; study 09 Q4 (list price vs actual spend) |
| What would you change? | README "What I'd do next" |
| What went wrong while building it? | The pitfalls sections of each study guide (good stories below) |

## 4. Five true war stories (each one shows senior judgement)
1. **The masking catch-all route (M4):** a `/api/{**rest}` 404 route hid real 400s. The fix was structural: exclude `/api` from the SPA fallback. And the fallback auth policy turned out to cover "no endpoint", so anonymous callers can't probe for routes.
2. **The index the plan rejected (M7):** the obvious covering index left two report queries scanning. The plan's output columns showed exactly what was missing.
3. **My own date bug (M6):** as reviewer I "fixed" date rendering with `'UTC'`. Angular parses date-only strings as local, so I broke the factory's own timezone. CI now runs the frontend tests on both sides of UTC.
4. **The contract nobody owned (M8):** the API returned a paged envelope where the SPA expected an array. Both test suites passed against their own assumptions, and only E2E connected them. The next step is an OpenAPI-generated client.
5. **The rate limiter that throttled my tests (M8):** cross-cutting protections change everything, so they must be configurable.

**How to tell each one:** context → what broke → how you found it → the fix → **the rule you now follow**.

## 5. The code-review exercise
- The `review-me` branch (PR #8) contains planted problems. Review it like a teammate's PR before an interview: write your findings as PR review comments with severity, impact and fix.
- Then ask for grading. It's the closest rehearsal there is for "here's some code, what do you see?" interview rounds.

## 6. Practice checklist
- [ ] Give the 2-minute version out loud, under 2:30.
- [ ] Do the 10-minute walkthrough with the repo open, without notes.
- [ ] Explain one ADR you'd change today, and why.
- [ ] Answer "what's not production-ready?" in three bullets: not deployed; .NET 9 STS; no OpenAPI client.
- [ ] Complete the `review-me` exercise and get it graded.
