# PlantOps — session handover

Portfolio and study project: a maintenance and asset management system for a fictional electronics factory in Penang.
The user studies the code to defend it in senior .NET + Angular interviews. **The user does not write code. Claude builds it, then teaches it.**

## Working rules (study mode)
1. Before each milestone, write `docs/design/NN-name.md`: the problem, 2–3 options, trade-offs, the decision.
2. Work on a branch per milestone (`mN-name`) in small conventional commits. Open a PR with a PR-style summary.
3. After each milestone, write `docs/study/NN-name.md` with: first-principles concepts, a guided code tour in reading order, "why this, not that", pitfalls, 5 interview Q&As in `<details>`, and one "break it" lab.
4. Comment only non-obvious code, and say WHY.
5. ~~STOP after each milestone~~ **Changed by the user on 2026-10-03: continue automatically to the next milestone. Stop only if the token budget runs low.** Skip DB/server/Azure deployment steps; write the code and config, and validate via CI.
6. Never invent numbers (performance, cost). Measure them or leave them out.
7. The `review-me` branch with planted bugs comes after M7. Never reveal the bug locations.

## Model routing
Opus (main session) does planning, design docs, study docs, ADRs and diff review.
Sonnet subagents implement the code. Haiku subagents handle mechanical work (scaffolding, seed data, formatting).

## Environment constraints
- **.NET 9 (SDK 9.0.308), not .NET 10.** The user won't install new SDKs. See ADR-0003.
- Node 24.16, npm 11, Angular 22.2, Docker 29, Apple Silicon (SQL Server image runs under amd64 emulation).
- Database and Azure are deferred. Code against them and set them up later, when the user asks.
- Ask before large downloads (Docker images, Playwright browsers). Install only Chromium for Playwright.
- The GateGuard hook blocks the first write to each new file. Batch new files, state the facts, then retry. The user chose to keep the gate.
- On the user's laptop, `dotnet` resolves to SDK 9.0.308. `global.json` pins 9.0.x.
- Subagents must create files with the Write tool (not Bash heredocs) so they go through the gate.

## Notes for upcoming milestones
- EF Core 9 mapping rule (M2): value objects in the domain API, plain columns in the persistence mapping where EF 9 must index or query them. Never hand-write indexes or FKs into migrations; the snapshot is the source of truth (`dotnet ef migrations has-pending-model-changes` must say "No changes").
- After `dotnet ef migrations add`, change the generated migration class from `public` to `internal`, or the architecture test fails.
- Integration tests skip locally unless `PLANTOPS_INTEGRATION_TESTS=1`. CI runs them, so push and check CI to validate them.
- Merging PRs is blocked for Claude by the user's permission rules. Stack branches and let the user merge.
- Auth (M3): BFF cookie (`__Host-plantops`), fallback policy = authenticated, policies in Identity.Contracts, CSRF guard requires `X-CSRF: 1` on unsafe /api requests. Tests use the `X-Test-User` test scheme (tests/PlantOps.Api.Tests/Support/TestAuth.cs). New write endpoints need `.RequireAuthorization(Policies.X)`. Clients in tests must send `X-CSRF`.
- Keycloak compose wiring (KC_HOSTNAME + BACKCHANNEL_DYNAMIC) and its healthcheck are unverified until compose actually runs.
- Verify framework-behaviour claims from subagents with a probe. In M3 an agent wrongly claimed `MapJsonKey` doesn't split arrays.
- M4: domain events + `DomainEventInterceptor` (BuildingBlocks.Infrastructure) write `AuditEntries` per module schema in the same transaction; M5's outbox should extend this interceptor. WorkOrders uses rowversion + ETag/If-Match (412/428). Resource-based auth via `WorkOrderOperations.Work`. JIT user directory in `identity.Users` (upsert in OnTicketReceived).
- Routing: there is no `/api/{**rest}` catch-all any more. The SPA fallback excludes `/api` by regex. Anonymous unknown `/api` paths return 401 (the fallback policy also covers "no endpoint"); signed-in users get a 404 problem. Integration test clients must always send a JSON body on POST commands.
- Subagents keep using heredocs despite instructions. Check `git status` for unexpected files after each agent.
- M5: transactional outbox (`OutboxMessages` per producing schema) plus inbox (`InboxMessages` per consuming schema) in BuildingBlocks.Infrastructure. Register events via `AddIntegrationEvent<T>` (allow-list). Integration tests drain with `IOutboxProcessor<TContext>.ProcessOnceAsync()` (the fixture sets `Outbox:PollInterval` to 1 h). The whole-app fixture lives in Inventory.Tests. Reserve uses `ConcurrencyRetry` (retry policy); WorkOrders uses 412 (refuse policy).
- Angular 22 facts (verified): zoneless by default, OnPush by default (`Default` renamed `Eager`), Vitest default, file naming `app.ts` (no `.component`). Signal Forms and httpResource are stable (`@publicApi 22.0`).
- Angular test gotcha: with httpResource, call `fixture.detectChanges()` then flush, then `await whenStable()`. Never await whenStable before flushing.
- `ng add @angular/material` adds Google Fonts links. This matters for the CSP in M8.

## Progress
| # | Milestone | Status |
|---|-----------|--------|
| 1 | Foundation & walking skeleton | done (PR #1 — user must merge; auto-merge is blocked by permissions) |
| 2 | Assets module (DDD basics) | done (PR #2, stacked on m1-foundation) |
| 3 | Identity & authorisation (Keycloak/OIDC) | done (PR #3, stacked on m2-assets) |
| 4 | Work order lifecycle | done (PR #4, stacked on m3-identity) |
| 5 | Inventory + events between modules (outbox) | done (PR #5, stacked on m4-workorders) |
| 6 | SLA escalation + preventive maintenance | — |
| 7 | Reporting (CQRS read side) + EF performance | — |
| — | `review-me` branch | — |
| 8 | Test pyramid + OWASP | — |
| 9 | Azure (Bicep, Actions deploy) | — |
| 10 | README / portfolio polish | — |

## Key decisions (details in docs/adr)
- ADR-0001: a modular monolith, not microservices.
- ADR-0002: each module has an implementation project plus a Contracts project, and owns its own DB schema. Boundaries are enforced by `internal` and an architecture test.
- ADR-0003: target net9.0 because of the laptop constraint. Upgrading is a TFM bump.

## Commands
- Backend: `dotnet build PlantOps.slnx` · `dotnet test`
- Frontend: `cd web && npm start` · `npm test` · `npm run lint`
- Everything: `docker compose up --build`
