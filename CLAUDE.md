# PlantOps — session handover

Portfolio and study project: a maintenance and asset management system for a fictional electronics factory in Penang.
The user studies the code to defend it in senior .NET + Angular interviews. **The user does not write code. Claude builds it, then teaches it.**

## Working rules (study mode)
1. Before each milestone, write `docs/design/NN-name.md`: the problem, 2–3 options, trade-offs, the decision.
2. Work on a branch per milestone (`mN-name`) in small conventional commits. Open a PR with a PR-style summary.
3. After each milestone, write `docs/study/NN-name.md` with: first-principles concepts, a guided code tour in reading order, "why this, not that", pitfalls, 5 interview Q&As in `<details>`, and one "break it" lab.
4. Comment only non-obvious code, and say WHY.
5. **STOP after each milestone** and wait for the user to say "next".
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

## Progress
| # | Milestone | Status |
|---|-----------|--------|
| 1 | Foundation & walking skeleton | in progress |
| 2 | Assets module (DDD basics) | — |
| 3 | Identity & authorisation (Keycloak/OIDC) | — |
| 4 | Work order lifecycle | — |
| 5 | Inventory + events between modules (outbox) | — |
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
