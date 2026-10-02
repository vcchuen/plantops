# 01 — Foundation & walking skeleton

## Problem
Before writing any business logic we need a skeleton that:
- makes module boundaries **mechanically enforced**, not "agreed in a meeting";
- runs end to end (browser → Angular → API → SQL Server) from one command;
- has CI from the first commit, so quality can't decay quietly;
- sets the Angular baseline (zoneless, standalone, OnPush, signals) once, so later milestones don't drift.

A "walking skeleton" is the thinnest slice that touches every layer. Ours is a status page in Angular that calls `/health/ready` on the API, which in turn checks that SQL Server is reachable.

## Decision 1 — How to structure a module in code

| Option | Description | Pros | Cons |
|---|---|---|---|
| A. One project per module, folders inside | `PlantOps.Modules.Assets` with `Domain/`, `Application/`, `Infrastructure/` folders | Few projects, fast build | Other modules can reference *everything* public in it; the boundary is only a convention |
| B. Clean Architecture per module | 4 projects per module (Domain, Application, Infrastructure, Api) → 16+ projects | Layer rules enforced by the compiler | Lots of ceremony; most interfaces end up with one implementation; slow to navigate |
| **C. Implementation + Contracts per module** | `PlantOps.Modules.Assets` (everything `internal`) + `PlantOps.Modules.Assets.Contracts` (public integration events and query interfaces) | The compiler stops cross-module access to internals; a module's public surface is one small, explicit project | Layering *inside* a module is by folder convention only |

**Decision: C.** The boundary that matters most in a modular monolith is *between modules*, not between layers inside one module. C puts the compiler on that line. Layering inside a module is checked in code review and, where it matters, by an architecture test.

## Decision 2 — How to enforce the boundaries
- Every type in a module project is `internal` by default. Only the module's registration entry point is `public`.
- Tests get access through `InternalsVisibleTo`.
- An **architecture test** loads every module assembly and fails if module X references module Y's *implementation* assembly. Referencing `Y.Contracts` is allowed.
- We use plain reflection (`Assembly.GetReferencedAssemblies()`) rather than NetArchTest or ArchUnitNET. For a rule this simple, a library adds a dependency without adding power.

## Decision 3 — API style

| Option | Pros | Cons |
|---|---|---|
| MVC controllers | Familiar to most MY/SG enterprise teams; attribute routing; filters | More ceremony; reflection-heavy; one class per resource tends to grow into a god-controller |
| **Minimal APIs + route groups** | Each module maps its own `MapGroup("/api/assets")`; `TypedResults` gives accurate OpenAPI; endpoint filters cover cross-cutting concerns | Less familiar; endpoint files need discipline to stay small |

**Decision: Minimal APIs.** Each module owns a single `MapXxxEndpoints()` extension, which reinforces the module boundary at the HTTP level too. It also gives you the "I've used both and here's why" answer for interviews.

## Decision 4 — How the browser reaches the API

| Option | Pros | Cons |
|---|---|---|
| Separate SPA host (nginx / Static Web Apps) + CORS | Frontend and backend deploy independently | CORS configuration, two origins, a second Azure resource |
| **API serves the built SPA from `wwwroot`** | Same origin, no CORS, one container, one App Service (cheaper) | Frontend and backend are released together |

**Decision: same origin.**
- In development, `ng serve` proxies `/api` and `/health` to the API (`proxy.conf.json`), so the browser still sees a single origin.
- The Docker image is a multi-stage build: a Node stage builds Angular, and the .NET stage copies the output into `wwwroot`.

## Decision 5 — Angular baseline
- Standalone components only (no NgModules), zoneless change detection, OnPush on every component.
- **What Angular 22 changed** (verified in the installed packages, not from memory):
  - New apps are zoneless by default, and `zone.js` isn't even installed.
  - **OnPush is now the default strategy.** The old `Default` strategy has been renamed `Eager`.
- We still write `provideZonelessChangeDetection()` and `ChangeDetectionStrategy.OnPush` explicitly. That way the intent survives a framework default changing, and someone reading the code who knows Angular ≤ 21 isn't misled.
- The ESLint rule `@angular-eslint/prefer-on-push-component-change-detection` (angular-eslint 22) **now flags components that opt out** to `Eager`. It is set to `error`, so "OnPush everywhere" can't regress quietly.
- Signals for local state, and `httpResource` for reads. Both are stable in Angular 22, which I confirmed from the `@publicApi 22.0` tags in the shipped type definitions.
- Angular Material for components, with lazy-loaded routes per feature.
- Vitest for unit tests.

## Decision 6 — Local orchestration
- **Options:** docker compose, .NET Aspire, or running everything by hand.
- **Decision: docker compose.**
  - It is what the spec asks for.
  - It is what most enterprise teams already run.
  - It works the same for an Angular developer who has no .NET tooling installed.
- Aspire is a good choice for .NET-heavy teams, but it adds a concept to learn without changing what we demonstrate here.
- SQL Server runs under amd64 emulation on Apple Silicon. That is slower but works.
- **Database setup is deferred by the user:** compose defines an empty SQL Server, and the API starts fine without it. Readiness reports "Unhealthy" until the database is reachable.

## Decision 7 — Build hygiene
- **`Directory.Build.props`:** nullable enabled, warnings as errors, latest analyzers.
- **Central Package Management (`Directory.Packages.props`):** NuGet versions are declared once, so no two projects can disagree on a version.
- **`global.json`:** pins the SDK to 9.0.x (see ADR-0003).
- **Solution file:** `.slnx`, the new XML solution format supported by the .NET 9 SDK. It's readable and gives you far fewer merge conflicts than `.sln`.

## Health checks: liveness vs readiness
- `/health/live` answers "is the process alive?". It never touches dependencies; if it did, a database outage would make the orchestrator restart healthy app instances in a loop.
- `/health/ready` answers "can this instance serve traffic?" and checks SQL Server.

## Out of scope for M1
Entities, migrations, auth, Keycloak, messaging. Each arrives in its own milestone.
