# Study guide 01: Foundation & walking skeleton

> Goal: by the end of this guide you should be able to draw the solution on a whiteboard, explain why every project exists, and defend each choice against "why didn't you just…?"
>
> Estimated time: one evening. Read sections 1–2, then follow the code tour with the files open, then do the lab.

---

## 1. Concepts from first principles

### 1.1 Monolith, microservices, and the thing in between
- **Deployable unit:** the thing you ship. A monolith is one; microservices are many.
- **Module:** a unit of *ownership and change*. Something you can reason about, test and modify without understanding the rest.

The two are independent axes:

|  | Weak boundaries | Strong boundaries |
|---|---|---|
| **One deployable** | "Big ball of mud" monolith | **Modular monolith** ← us |
| **Many deployables** | "Distributed monolith" (the worst of both) | Microservices |

- Most failed microservice migrations land in the bottom-left cell: services that share a database or call each other synchronously for everything. You pay the network tax without gaining independence.
- The lesson senior interviewers listen for: **boundaries are a design property. Deployment topology is an operational choice.** Get the boundaries right first. You can change the topology later (see ADR-0001, "How to extract a module later").

### 1.2 How C# enforces a boundary: assemblies and `internal`
- Every `.csproj` compiles to one **assembly** (a `.dll`).
- `public` means visible to any assembly that references this one. `internal` means visible only inside this assembly.
- So if every type in `PlantOps.Modules.Assets` is `internal` except `AssetsModule`, another module **cannot compile** code that touches Assets' entities. That holds even if someone adds a project reference. The compiler is the first guard.
- The loophole: someone makes a type `public` "just to get it working". The second guard, the architecture test, catches that.
- `InternalsVisibleTo` deliberately opens the door for exactly one named assembly, our test project. It's a scalpel; don't hand it to other modules.

### 1.3 Contracts projects
Module B sometimes legitimately needs something from module A. For example, WorkOrders needs to know an asset exists. Where should the shared type live?
- Not in A's implementation. B would then depend on A's internals.
- Not in a shared "Common" project either. That becomes a dumping ground that couples everyone to everyone.
- **In `A.Contracts`:** a small project containing *only* what A promises to the outside world (DTOs, query interfaces, integration events). A can refactor its internals freely, as long as the contract holds.

### 1.4 ASP.NET Core Minimal APIs (you know controllers; here's the mapping)

| Controllers | Minimal APIs |
|---|---|
| `[ApiController] class AssetsController` | `app.MapGroup("/api/assets")` |
| `[HttpGet("{id}")] public IActionResult Get(Guid id)` | `group.MapGet("/{id}", (Guid id, …) => …)` |
| Action filters | Endpoint filters (`.AddEndpointFilter<T>()`) |
| `return Ok(dto)` | `return TypedResults.Ok(dto)` |

Same pipeline, same DI, same model binding. The difference is that endpoints are lambdas registered at startup rather than methods discovered by reflection.

`TypedResults` (rather than `Results`) matters: the return type `Results<Ok<AssetDto>, NotFound>` tells OpenAPI exactly which responses exist, with no `[ProducesResponseType]` attributes to drift out of date.

### 1.5 Problem Details (RFC 9457)
A standard JSON shape for HTTP errors:
```json
{
  "type": "...",
  "title": "Not Found",
  "status": 404,
  "detail": "...",
  "traceId": "..."
}
```
It's served with `Content-Type: application/problem+json`.
- `AddProblemDetails()` + `UseExceptionHandler()` + `UseStatusCodePages()` makes *every* error the API produces use this shape, including unhandled exceptions and bare 404s.
- The frontend then needs only one error parser.

### 1.6 Health checks: liveness vs readiness
Orchestrators (App Service, Kubernetes, Docker) ask two different questions:
- **Liveness:** "Is this process stuck? Should I kill and restart it?"
- **Readiness:** "Should I send this instance traffic right now?"

If the liveness probe checked the database, a 30-second database blip would make the orchestrator **restart every healthy API instance**. Restarts don't fix the database, and now you also have cold starts. So liveness checks nothing except "the process can answer HTTP". Readiness checks dependencies.

### 1.7 Central Package Management and `Directory.Build.props`
- MSBuild automatically imports `Directory.Build.props` and `Directory.Packages.props` from the project's folder or any parent folder.
- Settings placed there apply to every project: the TFM, nullable, warnings-as-errors and every NuGet version.
- Result: two projects can't use different versions of the same package by accident, and a version bump is a one-line diff.

### 1.8 Angular for a C# developer: the minimum mental model

| Angular | C#/.NET analogy |
|---|---|
| **Component**: a TypeScript class + HTML template + CSS | A Razor component / Blazor component |
| `inject(Service)` | Constructor injection (`inject()` is the function form) |
| `provideHttpClient()` in `app.config.ts` | `services.AddHttpClient()` in `Program.cs` |
| **Standalone component**: declares its own `imports: [...]` | Each class has its own `using` list; no `NgModule` "project file" grouping components |
| **Route with `loadComponent: () => import(...)`** | The page's code is a separate JS file downloaded only when you navigate there (like lazy-loading an assembly) |

### 1.9 Signals, change detection, zone.js and "zoneless"
**The problem change detection solves:** when data changes, which parts of the DOM must be re-rendered?

- **Old Angular (zone.js):**
  - zone.js monkey-patches every async browser API (`setTimeout`, `addEventListener`, `fetch`, …).
  - After *any* async event, Angular re-checks every component's template bindings, because it doesn't know what changed.
  - That's simple, but wasteful. It also hides bugs, because things update "by magic".
- **OnPush:** a component is re-checked only when its inputs change, an event fires inside it, or it is explicitly marked dirty. It's faster, but before signals it was easy to get stale UI.
- **Signals:**
  - A signal is a value container that *knows who reads it*. `count = signal(0)`; reading `count()` inside a template registers a dependency; `count.set(1)` notifies exactly those readers.
  - `computed(() => …)` is a derived signal. It's cached and recomputed only when a signal it read changes. Think of a lazily evaluated property with automatic cache invalidation.
- **Zoneless:** with signals, Angular knows precisely what changed, so it no longer needs zone.js to guess. In Angular 22 new apps are zoneless by default, and `zone.js` isn't even installed.
- **What Angular 22 changed** (verified in `@angular/core` 22's type definitions):
  - **OnPush is now the default change detection strategy.**
  - The old always-check mode was renamed `Eager`.
  - We still write OnPush explicitly (see the design doc for why).

### 1.10 `httpResource`
- `httpResource(() => '/health/ready')` is a *signal-based HTTP read*.
- It gives you `value()`, `error()`, `isLoading()`, `hasValue()` and `reload()`, all signals.
- If the URL function reads other signals (for example a selected id), the request re-runs automatically when they change.
- It's for **reads**. For commands (POST/PUT) you still use `HttpClient`, because a mutation shouldn't re-fire whenever a signal changes.

---

## 2. Guided code tour (read in this order)

### Backend
1. **`global.json`**
   - Pins the SDK feature band (9.0.3xx) with `latestFeature` roll-forward.
   - *Notice:* without this, a developer with SDK 10 installed would silently build with a different compiler than CI does.
2. **`Directory.Build.props`**
   - *Notice:* `TreatWarningsAsErrors` + `EnforceCodeStyleInBuild`. Style violations fail the build locally and in CI, so formatting debates end at the compiler.
3. **`Directory.Packages.props`**
   - *Notice:* versions live here. csproj files say only `<PackageReference Include="..."/>` with no version.
4. **`PlantOps.slnx`**
   - The XML solution format.
   - *Notice:* it's human-readable and diff-friendly, compared with the GUID soup in `.sln`.
5. **`src/Modules/Assets/PlantOps.Modules.Assets/AssetsModule.cs`**
   - *Notice:* the **only** public type in the assembly. `Add…Module` for DI, and `Map…Endpoints` for HTTP with its own route group.
   - The `IConfiguration` parameter is unused today. It's part of the module contract so the host never has to change when a module starts needing config (M2: its connection string).
6. **`src/Modules/Assets/PlantOps.Modules.Assets/PlantOps.Modules.Assets.csproj`**
   - *Notice:* `FrameworkReference Microsoft.AspNetCore.App` (modules map endpoints, so they need ASP.NET types), a reference to its own Contracts project, and `InternalsVisibleTo` for the tests only.
7. **`src/Host/PlantOps.Api/Program.cs`**, top to bottom. *Notice:*
   - The host only *composes*: it references modules, calls their two entry points, and contains no business logic.
   - Middleware order: exception handler first (it must wrap everything), then status-code pages.
   - Health endpoints use **tag-based predicates**. Liveness runs zero checks.
   - The `/api/{**rest}` catch-all: without it, an unknown API URL would fall through to `MapFallbackToFile` and return `index.html` with **200 OK**. Your frontend would then try to parse HTML as JSON, and API clients would think the call succeeded.
   - `UseStaticFiles`, not `MapStaticAssets` (read the comment). This is a deployment-shape decision leaking into code.
   - `public partial class Program;` lets the test project reference the top-level-statements program class.
8. **`src/Host/PlantOps.Api/Health/SqlServerHealthCheck.cs`**
   - *Notice:* `catch (Exception ex) when (ex is not OperationCanceledException)`. Cancellation (the client disconnected) must not be reported as "database down".
   - The returned description is generic. The details go to the log.
9. **`src/Host/PlantOps.Api/Health/HealthResponseWriter.cs`**
   - *Notice:* it projects to an anonymous object and deliberately drops `HealthReportEntry.Exception`.
   - Health endpoints are anonymous. Leaking `"Login failed for user 'sa' on server sql-prod-01"` hands an attacker your topology (OWASP A05, Security Misconfiguration).
10. **`tests/PlantOps.Api.Tests/ArchitectureTests.cs`**
    - *Notice:* each assertion message explains **which rule** was broken, **why** it exists, and **how to fix it**. A failing test is documentation for the next developer.
    - Read the comment about `GetReferencedAssemblies()`. The lab demonstrates it.
11. **`tests/PlantOps.Api.Tests/HostTests.cs`**
    - *Notice:* `UseSetting("ConnectionStrings:PlantOps", "")` overrides user-secrets, so a test run can't hit your real local database.
    - *Notice:* the "unreachable server" test uses port 1 with `Connect Timeout=1`. That makes it fast and deterministic, with no Docker needed.
    - *Notice:* the SPA fallback test creates a temporary web root, because `wwwroot` doesn't exist until Docker copies Angular in.

### Frontend (`web/`)
12. **`web/src/main.ts` → `web/src/app/app.config.ts`**
    - `bootstrapApplication(App, appConfig)` is the `Program.cs` of Angular.
    - *Notice:* `provideZonelessChangeDetection()` is explicit, and `withFetch()` makes HttpClient use the browser's `fetch` instead of XHR.
13. **`web/src/app/app.routes.ts`**
    - *Notice:* `loadComponent: () => import(...)`. Each page becomes its own JS chunk. The build output shows `status-page` as a separate ~89 kB lazy chunk.
    - `title` sets the browser tab title per route, which screen readers announce on navigation.
14. **`web/src/app/app.ts` + `app.html`**. *Notice:*
    - `toSignal(breakpointObserver.observe(...))` is the bridge from an RxJS stream (screen size changes over time, a genuine stream) to a signal the template can read. This is the "RxJS only where streams genuinely fit" rule in action.
    - **Accessibility:**
      - landmarks (`<header>`, `<nav aria-label>`, `<main>`);
      - a skip link (it calls `main.focus()` because a `#hash` link would fight the router);
      - `ariaCurrentWhenActive="page"` so screen readers announce the current page;
      - icons marked `aria-hidden` so they're not read out as "check_circle".
15. **`web/src/app/features/status/status.models.ts`**
    - *Notice:* the `isHealthReport` **type guard**. TypeScript types vanish at runtime, so anything arriving over the network is `unknown` until you check it.
16. **`web/src/app/features/status/status.page.ts`**. The most important frontend file this milestone. *Notice:*
    - `httpResource<HealthReport>(...)`. The `<HealthReport>` is a *claim*, not a check. The type guard is the check.
    - **The 503 problem:** the API returns 503 *with a valid report body* when the database is down. `httpResource` treats any non-2xx as an error, so `value()` is unavailable (it throws in error state). The `report` computed reads `error()` first and recovers the body from `HttpErrorResponse.error`.
    - `unreachable` is a *separate* computed. "The system is unhealthy" and "I can't reach the system" are different situations for an operator, and they get different UI.
17. **`web/src/app/features/status/status.page.html`**
    - *Notice:* the new control flow (`@if`, `@else if`, `@for`), and `aria-live="polite"` so screen readers announce status changes.
    - The status is shown as **icon + text**, never colour alone (WCAG 1.4.1).
18. **`web/src/app/features/status/status.page.spec.ts`**
    - *Notice:* the comment in `beforeEach`. It's the biggest testing gotcha of this milestone (see pitfalls).
19. **`web/proxy.conf.json`**
    - *Notice:* in development, `ng serve` (port 4200) forwards `/api` and `/health` to the API on port 5080. The browser sees a single origin, so no CORS is needed. That matches production, where the API serves the SPA.
20. **`web/eslint.config.js`**
    - *Notice:* `prefer-on-push-component-change-detection: 'error'`.

### Delivery
21. **`Dockerfile`**. *Notice:*
    - The three stages (node build → dotnet publish → slim runtime). The final image contains no SDK and no node.
    - csproj files are copied **before** source, so `dotnet restore` is cached until a dependency changes.
    - `USER $APP_UID`: the .NET images ship a non-root user. Running as root in a container is an OWASP A05 finding.
22. **`docker-compose.yml`**. *Notice:*
    - `platform: linux/amd64` (there's no arm64 SQL Server image).
    - `depends_on: condition: service_healthy`, so the API waits for SQL to be *ready*, not just started.
    - `${MSSQL_SA_PASSWORD:?…}` fails fast if `.env` is missing.
    - `$$` escapes `$` for compose.
23. **`.github/workflows/ci.yml`**. *Notice:*
    - `permissions: contents: read`: least privilege for the CI token.
    - Actions are **pinned to commit SHAs**. In March 2025 (CVE-2025-30066) attackers re-pointed the tags of the widely used `tj-actions/changed-files` action to malicious code that dumped CI secrets into build logs. Repos pinned to a SHA were unaffected (OWASP A08, supply chain).
    - `concurrency` cancels superseded runs.

---

## 3. Why this, not that

| Choice | Instead of | Why |
|---|---|---|
| Modular monolith | Microservices | One team, one factory. Microservices' benefit (independent deploy per team) doesn't apply, but all their costs would (ADR-0001). |
| Module + Contracts (2 projects) | Clean Architecture (4 per module) | The compiler guards the boundary that matters (module↔module). Four layers per module adds ~12 projects of ceremony for the same protection. |
| Reflection-based architecture test | NetArchTest / ArchUnitNET | Three rules, ~60 lines, no dependency. Switch to a library when the rules need namespace/layer queries. |
| Minimal APIs | Controllers | A per-module route group mirrors the module boundary, and `TypedResults` gives accurate OpenAPI. Controllers are equally valid; know both. |
| Same origin (API serves SPA) | Separate SPA host + CORS | No CORS, simpler auth cookies/tokens later, one App Service to pay for. Cost: front and back release together. |
| Custom SQL health check (~25 lines) | `AspNetCore.HealthChecks.SqlServer` package | Trivial code, and we control what gets exposed. In M2 it becomes `AddDbContextCheck` once EF Core is in. |
| `httpResource` | `HttpClient` + `subscribe` | Loading, error and value as signals with no manual subscription management. HttpClient remains the right tool for commands. |
| Docker compose | .NET Aspire | It's what the spec asks for and what enterprise teams know. Aspire is great but adds a concept without changing what we demonstrate. |
| .NET 9 | .NET 10 | Laptop constraint. Documented with its end-of-support date (ADR-0003). Owning a known risk is the senior move. |

---

## 4. Common pitfalls and how this code avoids them

1. **An API 404 returns `index.html` with 200.** This is the classic SPA-hosting bug. Avoided by the `/api/{**rest}` catch-all, and pinned by a test.
2. **A database check in liveness causes a restart storm.** Avoided by `Predicate = _ => false` on `/health/live`.
3. **Health or error responses leak internals.** The exception goes to the log, and a generic message goes to the response. Tests assert that `127.0.0.1` and "Exception" never appear.
4. **Secrets in `appsettings.json`.** No connection string is committed. Local dev uses user-secrets, and compose reads `.env` (gitignored; `.env.example` is committed).
5. **"Public just to make it compile".** Caught by the architecture test that allows exactly one public type per module.
6. **Awaiting `whenStable()` before flushing a fake HTTP request (Angular tests).** The in-flight request is pending work, so the app never becomes stable, and the test times out after 10 s with a confusing error. The fix is in the spec's `beforeEach`: `detectChanges()` to fire the request, then flush, *then* `whenStable()`.
7. **Reading `value()` on an errored resource.** It throws. Check `error()` / `hasValue()` first.
8. **Trusting `HttpErrorResponse.error`.** It's `any`, and behind a proxy it can be an HTML gateway page. The type guard turns "trust me" into "check me".
9. **`ng lint` passing ≠ the code compiles.** ESLint doesn't type-check. CI runs lint, test *and* build.
10. **`--` inside an XML comment in a `.csproj`.** It breaks the MSBuild parse (MSB4025) with a misleading stack trace. (Hit during this milestone.)

---

## 5. Senior interview questions

<details>
<summary><strong>Q1. Why a modular monolith and not microservices? When would you split a module out?</strong></summary>

**Model answer:** Microservices optimise for organisational scale: independent deployment and scaling per team. They cost network failure modes, distributed data consistency, and N pipelines. PlantOps has one team and one factory, so we'd pay every cost and get none of the benefit.

What I *do* need from microservices is strong boundaries. A modular monolith gives me those at compile time: internal types, Contracts projects, a schema per module, and an architecture test in CI.

I'd extract a module when there's a concrete driver:
- a different team owns it;
- it needs a different scaling profile (for example reporting load hurting transactional latency);
- it needs a different release cadence;
- or it needs isolation for compliance.

Because Inventory already owns its schema, has no cross-schema foreign keys, and communicates through contracts and events, extraction is mechanical: move the schema, swap the in-process event dispatcher for Service Bus, and implement the query interface over HTTP.
</details>

<details>
<summary><strong>Q2. How do you stop modules reaching into each other? What are the limits of your approach?</strong></summary>

**Model answer:** There are three layers of defence:
1. **The compiler:** everything is `internal` except the module's entry point.
2. **Project structure:** shared types live only in `.Contracts`.
3. **An architecture test in CI** that checks the assembly reference graph and allows exactly one public type per module.

The limits:
- `GetReferencedAssemblies()` only sees references actually used in IL, so an unused project reference passes. That's acceptable, because an unused reference doesn't couple anything, but it's worth knowing.
- Reflection, `dynamic` or string-based coupling, such as one module querying another's tables with raw SQL, is invisible to it. The schema-per-module rule plus separate DbContexts covers the database side. In M2 I'd add a test that each DbContext only maps entities from its own assembly.
- `InternalsVisibleTo` can be abused, so we grant it only to the test assembly.
</details>

<details>
<summary><strong>Q3. What's the difference between liveness and readiness probes? What happens if you get it wrong?</strong></summary>

**Model answer:**
- **Liveness** asks "is the process healthy enough to keep running?". Failing it causes a restart.
- **Readiness** asks "should this instance receive traffic?". Failing it takes the instance out of the load balancer.

If you put a database check in liveness, a transient database outage fails every instance's liveness at once. The orchestrator restarts them all, which doesn't fix the database, adds cold-start latency, and can amplify the outage (thundering herd on reconnect).

Dependencies belong in readiness. Liveness should check only things a restart would actually fix, such as a deadlocked thread pool.

We also keep the health response free of exception details, because the endpoint is anonymous.
</details>

<details>
<summary><strong>Q4. Explain zoneless change detection and why OnPush matters. What changed in Angular 22?</strong></summary>

**Model answer:**
- **zone.js:** patched async browser APIs so Angular could re-check the whole component tree after any event. Correct, but wasteful, and it masked missing-update bugs.
- **OnPush:** limits checks to components whose inputs changed, which fired an event, or which were marked dirty.
- **Signals:** they track exactly which template reads which value, so Angular can schedule precise updates without zone.js. That's zoneless.
- **Angular 22:** zoneless is the default for new apps, OnPush is the default strategy, and the old check-always mode is now called `Eager`.

We still declare OnPush and `provideZonelessChangeDetection()` explicitly. That way the intent is visible, survives future default changes, and doesn't confuse anyone reading with v19-era knowledge. Lint fails any component that opts back to `Eager`.

Practical consequence: state that the template reads must be a signal (or come through `async`/`toSignal`). Mutating a plain field in a `setTimeout` won't update the view, and that's a feature, because it forces explicit reactive state.
</details>

<details>
<summary><strong>Q5. Your status page uses httpResource. How do you handle a 503 that carries a useful body, and when would you not use httpResource?</strong></summary>

**Model answer:** `httpResource` treats any non-2xx response as an error, so on 503 `value()` is unavailable. The health report is then in `HttpErrorResponse.error`, typed `any`.

I derive a `report` computed that checks `error()` first and runs the body through a runtime type guard, because behind a reverse proxy a 503 might be an HTML error page. A separate `unreachable` computed covers network failures, because "the database is down" and "I can't reach the API" mean different things to an operator.

When *not* to use httpResource:
- **Commands (POST/PUT/DELETE):** use `HttpClient` directly. A resource re-fetches when its signal dependencies change, which you never want for a mutation.
- **Genuinely stream-shaped problems** (typeahead with debounce and cancel, websockets, polling with backoff): RxJS operators are the better tool, bridged into the template with `toSignal`.
</details>

---

## 6. Break-it lab: watch the boundary guards fire

**Goal:** see which guard catches which mistake, and discover that the compiler alone isn't enough.

1. Create a new branch so you can throw it away: `git checkout -b lab-01`
2. Create `src/Modules/WorkOrders/PlantOps.Modules.WorkOrders/WorkOrderNumber.cs` with this content:
   ```csharp
   namespace PlantOps.Modules.WorkOrders;

   public sealed record WorkOrderNumber(string Value);
   ```
3. Make Assets reference the WorkOrders **implementation** project:
   ```bash
   dotnet add src/Modules/Assets/PlantOps.Modules.Assets reference src/Modules/WorkOrders/PlantOps.Modules.WorkOrders
   ```
4. Build and test:
   ```bash
   dotnet build PlantOps.slnx && dotnet test PlantOps.slnx
   ```
   **Observe:**
   - The **build succeeds**. The compiler is perfectly happy, because `WorkOrderNumber` is public.
   - **One test fails:** `Modules_expose_exactly_one_public_type_named_after_the_module`. Read its message; it tells you the rule, the reason, and the fix.
   - `Modules_do_not_reference_other_module_implementations` still **passes**, even though the project reference exists. Why? (Hint: re-read the comment at the top of `ArchitectureTests.cs`.)
5. Now actually *use* the type. In `AssetsModule.AddAssetsModule`, add this line before `return services;`:
   ```csharp
   _ = new PlantOps.Modules.WorkOrders.WorkOrderNumber("WO-1");
   ```
   Run `dotnet test PlantOps.slnx` again.

   **Observe:** now `Modules_do_not_reference_other_module_implementations` fails too, naming `PlantOps.Modules.WorkOrders`. The reference became real IL.
6. Change `public sealed record` to `internal sealed record` and build.

   **Observe:** a **compile error** (CS0122, "inaccessible due to its protection level"). That's the first guard, the cheapest one, catching it before any test runs.
7. Clean up:
   ```bash
   git checkout main
   git branch -D lab-01
   ```
   If git refuses because of uncommitted changes, first run `git restore . && rm src/Modules/WorkOrders/PlantOps.Modules.WorkOrders/WorkOrderNumber.cs`.

**What you should be able to say afterwards:** "`internal` stops accidental access at compile time. The public-surface test stops someone 'fixing' the compile error by making things public. The reference test catches real coupling. Each guard covers a hole in the previous one."

---

## 7. Measured numbers from this milestone
Only numbers we actually observed:
- **Angular production build:** initial bundle 453.44 kB raw, about 110.72 kB estimated transfer. The lazy `status-page` chunk is 88.74 kB raw (16.25 kB transfer).
- **Tests:** 8 .NET tests pass (~1 s), and 5 Vitest tests pass (~1.3 s), on the author's laptop.
