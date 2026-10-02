# Study guide 02: Assets module (DDD basics)

> Goal: you can explain what an aggregate is *and why it's that size*, defend every value-object mapping choice against EF Core 9's limits, and show how the code stays correct under concurrent requests.
>
> Read sections 1–2 first, then do the code tour with the files open. The lab runs on your laptop with no database.

---

## 1. Concepts from first principles

### 1.1 Why DDD at all?
- Most CRUD apps put rules in controllers or services: "if status is decommissioned, return 400". When a second caller appears (a scheduler, an import, another endpoint), the rule is copied, or forgotten.
- DDD's core move is to put the rules **inside the object that owns the data**, so there is no way to change the data except through a method that enforces them.
- You only need *some* of DDD for that. This milestone uses three building blocks:
  - entities;
  - aggregates;
  - value objects.

### 1.2 Entity vs value object

| | Entity | Value object |
|---|---|---|
| Identity | Has one (`AssetId`). Two assets with identical fields are still two assets | None. Two `AssetTag("SMT-01")` *are the same value* |
| Mutability | Changes over time through methods | Immutable. You replace it, you don't modify it |
| C# shape | `class` with private setters | `record` (value equality for free) |
| Examples here | `Asset`, `ProductionLine` | `AssetTag`, `Location`, `AssetId` |

**Why value objects matter (the "primitive obsession" problem):**
- If the tag is a `string`, every method that receives one must re-validate it, and `Relocate("Station 4", lineId)` vs `Relocate(lineId, "Station 4")` is a bug the compiler can't see.
- With `AssetTag`, the only way to get an instance is `AssetTag.Create(...)`, which validates and normalises. *If you hold an `AssetTag`, it's valid.* That's the whole trick: **make invalid states unrepresentable.**

### 1.3 Aggregate and aggregate root
- An **aggregate** is a cluster of objects treated as one unit **for data changes**. The **root** is the single entry point: outsiders hold a reference to the root only and call its methods.
- **The rule that decides its size:** an aggregate is a *consistency boundary*. Everything inside must be consistent at the end of every transaction. Everything outside can be eventually consistent.
- **Asset vs ProductionLine:**
  - Is there an invariant like "a line can't have more than N assets" or "the asset order on a line must be contiguous"? No.
  - So they're **separate aggregates**, and Asset references the line **by id only** (no navigation property).
  - Loading a line doesn't load 200 assets, and two engineers editing two different assets on the same line never conflict.
- **Tag uniqueness** is a rule *across* aggregates ("no two assets share a tag"). One aggregate can't enforce it, because it can't see the others. That's why it lives in the database (§1.6).

### 1.4 Domain exceptions → HTTP problems
- Aggregate methods throw `DomainException` (rule broken), and endpoints throw `NotFoundException` / `ConflictException`.
- One `IExceptionHandler` in the host turns those into RFC 9457 `400 / 404 / 409` responses.
- Aggregates don't know HTTP exists, and endpoints don't contain `if (...) return BadRequest(...)` chains.

### 1.5 EF Core mapping without polluting the domain
EF needs:
- a parameterless constructor (it can be `private`);
- settable properties (they can be `private set`);
- a way to convert custom types (value converters).

We accept those small leaks (ADR-0005) and nothing more. There are no data annotations on the domain classes; all mapping lives in `IEntityTypeConfiguration<T>` classes in `Infrastructure/`.

### 1.6 Check-then-act is a race
```
Request A: SELECT ... WHERE Tag='SMT-01'  → none
Request B: SELECT ... WHERE Tag='SMT-01'  → none
Request A: INSERT 'SMT-01'                 → ok
Request B: INSERT 'SMT-01'                 → ok   ← duplicate!
```
- The fix isn't a smarter check. **The database's unique index is the only atomic arbiter.**
- Let both requests insert. The loser gets SQL error 2601, and we translate that to 409.
- This pattern (constraint + translate) is the standard answer to "how do you enforce uniqueness?" in an interview.

### 1.7 Projections and N+1
**N+1** happens when you load a list (1 query), then touch a navigation property on each item (N more queries). The classic EF example is `foreach (var a in assets) a.Line.Name`.

We avoid it **by construction**:
- reads never load entities at all;
- `Select(...)` projects straight into a DTO, and EF turns that into **one SQL statement** with a JOIN;
- there are no navigation properties, so there's nothing to lazy-load by accident.

An integration test counts the SQL commands with an EF interceptor and fails if the list endpoint runs more than 2 (one for the page, one for the total count).

### 1.8 Angular: the URL as state
- The asset list's filters (line, criticality, status, search, page, pageSize) live in the **query string**, not in component fields.
  - The URL is shareable: send a colleague "all criticality-A assets on SMT-1".
  - Refresh and the back button just work.
  - There's exactly one source of truth.
- **The data flow:**
  ```
  URL ?criticality=A&page=2
    → router (withComponentInputBinding)
    → signal inputs criticality(), page()
    → httpResource URL function reads them  ← re-runs automatically when they change
    → template
  user changes a filter
    → router.navigate([], { queryParams, queryParamsHandling: 'merge' })
    → URL changes → (loop)
  ```
- **The one RxJS stream:** search-as-you-type. A keystroke is an *event over time*, and `debounceTime(300) + distinctUntilChanged()` is exactly what RxJS is for. Everything else is signals.

---

## 2. Guided code tour (read in this order)

### Domain (start here; there's no framework code in it)
1. **`src/BuildingBlocks/PlantOps.SharedKernel/*.cs`**
   - Three tiny exception types.
   - *Notice:* the kernel is deliberately tiny. A shared kernel is shared coupling, so anything added here must be truly universal.
2. **`Domain/AssetTag.cs`**. *Notice:*
   - The private constructor, plus `Create()` as the only door in.
   - Normalisation (trim + upper-case) **before** validation.
   - `[GeneratedRegex]`: a source-generated regex, compiled at build time.
   - The error messages are written for the person filling in the form, because they reach the API caller verbatim.
3. **`Domain/AssetId.cs`, `ProductionLineId.cs`**
   - `readonly record struct` wrappers. Now `GetAsset(ProductionLineId id)` can't accidentally receive an asset id.
   - *Notice the comment on `Guid.CreateVersion7()`:* the ids are time-ordered, but SQL Server sorts `uniqueidentifier` by its *last* bytes, so they don't give sequential inserts there. Knowing this distinguishes you from someone who read one blog post.
4. **`Domain/Location.cs`, `Criticality.cs`, `AssetStatus.cs`, `TextRules.cs`**
   - Small and boring, which is the point.
5. **`Domain/Asset.cs`**. The heart of the milestone. *Notice:*
   - There are **no public setters**. State changes only through `Register`, `UpdateDetails`, `Relocate`, `ChangeCriticality` and `Decommission`.
   - `EnsureInService()` is called first in every mutator. That's invariant #1, enforced once, in one place.
   - `Decommission(on, reason, today)` takes `today` as a **parameter**. The aggregate never reads a clock, so the rule ("not in the future") is deterministic in tests. The endpoint supplies today from `TimeProvider`.
   - **The mapping compromises are commented right where they happen:**
     - `_tag` is a string field, with `Tag` exposed as an `AssetTag` on top (why: design doc 02, Decision 2);
     - `Location` is rebuilt from two scalar properties;
     - decommission state is two nullable columns, because EF 9 has no optional complex types.

### Persistence
6. **`Infrastructure/AssetsDbContext.cs`**
   - `HasDefaultSchema("assets")`: the module owns its schema (ADR-0002).
   - `ConfigureConventions` applies the id converters to *every* property of those types, so you don't have to remember them per property.
7. **`Infrastructure/AssetConfiguration.cs`**. *Notice:*
   - `Property<string>(Asset.TagField).HasColumnName("Tag")` maps a **private field**, and `Ignore(a => a.Tag)` hides the value-object getter from EF.
   - Enums are stored as **strings**: readable in SQL and reports, and reordering the enum can't silently corrupt data.
   - `UX_Assets_Tag` (unique), `IX_Assets_LineId`, and the FK with `Restrict` are **all declared in the model**, so the snapshot knows about them (read the design doc's "How we got here").
   - `HasOne<ProductionLine>().WithMany()` has no navigation property on either side. You get the FK without the coupling.
8. **`Infrastructure/ProductionLineConfiguration.cs`**
   - `HasData` seeds the four lines with fixed GUIDs, so the ids are identical in every environment.
9. **`Infrastructure/Migrations/*InitialAssets.cs`**
   - Read the `Up()` method like SQL. Check that every index from step 7 is there.
   - *Notice:* the class is `internal`. The scaffolder emits `public`, which would break the architecture test, so **regenerating requires the same one-word edit**.
10. **`Infrastructure/AssetsMigrationService.cs`**
    - It migrates only when `Database:ApplyMigrationsOnStartup=true` (compose sets this).
    - *Notice the comment:* an `IHostedService.StartAsync` finishes before Kestrel accepts requests, so no request ever sees a half-migrated schema.
11. **`AssetsModule.cs`**. *Notice:*
    - The connection string is read **inside** the `AddDbContext` callback. If it were read at registration time, test and user-secrets overrides added later would be missed.
    - `EnableRetryOnFailure()` retries transient Azure SQL faults.
    - The module registers its *own* readiness check. The host doesn't need to know which databases a module uses.

### HTTP
12. **`Endpoints/AssetDtos.cs`**
    - Request and response records. They're separate from the domain types, so the API contract can't change by accident when the model does.
13. **`Endpoints/AssetEndpoints.cs`**. *Notice:*
    - **Command shape:** load, call one domain method, `SaveChangesAsync`. There's no logic here that belongs in the aggregate.
    - **`RegisterAsset`'s catch block:** `when (ex.InnerException is SqlException { Number: 2601 or 2627 })`, an exception *filter* with a property pattern. Read the comment about why there's no pre-check.
    - **`ListAssets`:**
      - `AsNoTracking()`, filters composed conditionally, and a single projection with a join.
      - The count query runs **without** the join (the FK guarantees every asset has a line).
      - Query syntax (`from … join … orderby … select`) is used because EF can't translate an `OrderBy` on a property of a DTO it has just constructed.
    - **The search comment:** `StartsWith` / `Contains` make EF **escape `%`, `_` and `[`** in user input. The lab shows what happens without that.
    - `Decommission` takes `TimeProvider` from DI. Tests can swap it for `FakeTimeProvider`.
14. **`src/Host/PlantOps.Api/Http/ApiExceptionHandler.cs`**. *Notice:*
    - Unknown exceptions return `false`, so the framework emits a generic 500 with no details.
    - `BadHttpRequestException` (malformed JSON, `?criticality=Z`) is mapped to 400 with a **generic** message, because the framework's own message names internal CLR types.

### Tests
15. **`tests/PlantOps.Modules.Assets.Tests/Domain/*`**
    - Pure unit tests, no EF and no I/O, 54 of them running in milliseconds. One test per invariant, named after the rule.
16. **`Integration/IntegrationFactAttribute.cs`**
    - It sets `Skip` in the *constructor*, so xUnit never builds the fixture and the container never starts. Skipped tests show as **skipped**, never as passed.
17. **`Integration/SqlServerFixture.cs`**
    - One SQL Server container per test collection. It applies the **real migrations** (not `EnsureCreated`).
    - It injects the command counter via `ConfigureDbContext` in `ConfigureTestServices`.
18. **`Integration/CommandCounter.cs` + the N+1 test in `AssetsApiTests.cs`**
    - A `DbCommandInterceptor` counts SQL commands. The test seeds 12 assets and asserts the list endpoint runs exactly 2.
19. **`AssetsApiTests.Concurrent_registrations_of_the_same_tag_…`**
    - Two simultaneous POSTs, and the assertion is exactly one 201 plus one 409. This is §1.6 proven against real SQL Server.

### Frontend (`web/src/app/features/assets/`)
20. **`assets.models.ts`**
    - API types, label maps (criticality is shown as text, never colour alone), and `describeError` for problem+json bodies.
21. **`assets-list.page.ts`**, the most interesting frontend file so far. *Notice:*
    - **Inputs from query params:** `page` and `pageSize` use `transform: positiveInt(...)`. Query params are *always strings*, and an absent param arrives as `undefined`, not as your default. The transform absorbs junk like `?page=abc`.
    - **`httpResource` with params:** only non-empty values are sent.
    - **`linkedSignal` for `current`:**
      - When the URL changes, `httpResource` goes to `loading`, and `value()` becomes undefined (only `reload()` keeps the old value).
      - Without `current`, the table would flash empty on every filter change.
      - `linkedSignal` keeps "the last good page".
    - **`navigate()`:** every filter change resets to page 1, and `null` removes a param under `'merge'`.
22. **`asset-detail.page.ts`**
    - The `:id` route param becomes an input. *Notice* `encodeURIComponent`: the id comes from the URL, so it's untrusted (a reviewer fix, see pitfalls).
23. **`*.spec.ts`**
    - `setInput()` comes *before* the first `detectChanges()`, because the request is sent from an effect.
    - Requests are matched with a predicate on `r.url`, because the params live in `request.params`.
    - Search debounce is tested with `vi.useFakeTimers()`: three keystrokes produce one navigation.

---

## 3. Why this, not that

| Choice | Instead of | Why |
|---|---|---|
| Two small aggregates | Line contains its assets | No invariant spans them; a big aggregate means big locks and false conflicts |
| Value objects (`AssetTag`, `Location`, typed ids) | Strings and Guids | Valid by construction; argument mix-ups become compile errors |
| Private string field + VO getter for `Tag` | Value converter / complex type | A converter blocks index-friendly `LIKE`; a complex type can't carry an index in EF 9 (both verified, design doc 02) |
| Index and FK declared in the model | Hand-written in the migration | The snapshot must know the schema, or the next migration silently drops them |
| Unique index + catch 2601/2627 | `AnyAsync` pre-check | The pre-check is a race; the index is atomic |
| Domain exceptions + one handler | `Result<T>` everywhere | Invariants can't be ignored; less ceremony; switch when failures become normal flow (bulk import) |
| Task-based endpoints | `PUT` the whole asset | Intent is explicit; maps 1:1 to aggregate methods; meaningful audit in M4 |
| DbContext directly | Generic repository | DbContext already is a UoW + repository (ADR-0005) |
| Projection + `AsNoTracking` for reads | Load aggregates and map | One SQL statement; no tracking overhead; N+1 impossible |
| Testcontainers SQL Server | EF InMemory / SQLite | InMemory ignores unique indexes, so it would pass the race test while the real database fails |
| Offset paging | Keyset paging | A few hundred assets never reach deep pages; M7 measures before changing |
| URL query params as filter state | Component fields / a store | Shareable, refresh-safe, single source of truth |
| Migrate in a hosted service, behind a flag | Always migrate on startup | Production uses bundles with a privileged identity (ADR-0004); the app identity gets no DDL rights |

---

## 4. Common pitfalls and how this code avoids them

1. **Hand-editing schema into migrations.** This was caught in review this milestone. Operations that aren't in the model snapshot are invisible to the next `migrations add`. The rule: if EF can't express it in the model, change the *model shape* rather than the migration. `dotnet ef migrations has-pending-model-changes` reports "No changes" here.
2. **Value converters vs LINQ.** A converter hides the underlying type. `EF.Property<string>` on a converted property throws, and a cast translates to `CAST(... AS nvarchar(max))`, which kills the index seek. We measured both.
3. **LIKE wildcards in user input.** Searching "50%" must not match everything. `StartsWith` / `Contains` escape for you, but `EF.Functions.Like` does **not** (see the lab). This isn't injection, because the value is still a parameter. It's a correctness bug.
4. **Reading configuration too early.** `configuration.GetConnectionString(...)` at registration time ignores overrides added later by `WebApplicationFactory` or user-secrets. Read it inside the options callback.
5. **Reading an HTTP response body twice.** CI caught this in our own test: `AssertProblem` read the body, then the test read it again and got `ObjectDisposedException`. The fix was to read once and return the parsed body.
6. **`httpResource` drops its value on a new request.** Only `reload()` keeps the previous value. A changed URL means `loading`, with `value()` undefined. `linkedSignal` keeps the last good page.
7. **Query params are strings, and an absent one is `undefined`.** It is not your input's default. Use an input `transform`.
8. **Untrusted route params in URLs.** `encodeURIComponent` stops a crafted link like `/assets/..%2F..%2Fhealth` from retargeting the API call.
9. **Bundle size creep.** After M2 the initial bundle went from 453 kB to 537 kB, even though the new pages are lazy. We measured it with `ng build --stats-json`:
   - `@angular/forms` (38 kB raw) landed in the **initial** chunk. The shell's Material list module imports it statically, and once the lazy pages use forms, esbuild places it with its earliest importer.
   - Importing standalone directives instead of `MatListModule` made **no difference** (536.36 kB). We tried it, measured it, and reverted it.
   - Net cost: +17 kB over the wire (110.8 → 127.7 kB). We accepted that and set the warning budget to 560 kB rather than contort the shell. *Measure, decide, document.*
10. **Retries and non-idempotent inserts.** `EnableRetryOnFailure` retries transient faults. If a connection drops *after* SQL Server committed an insert but before the client heard back, the retry inserts again. Here the unique index turns that into a 409 even though the asset was created. It's rare, and the client can GET by tag to confirm, but it's a real at-least-once effect. Mention it when someone asks "what could go wrong with retries?"

---

## 5. Senior interview questions

<details>
<summary><strong>Q1. What is an aggregate, and how did you decide that Asset and ProductionLine are separate ones?</strong></summary>

**Model answer:** An aggregate is a consistency boundary: a cluster of objects that must be consistent at the end of each transaction, changed only through its root.

I size aggregates by their invariants, not by how the UI or the database looks. Asset's invariants are local: a decommissioned asset can't change, and the decommission date must lie between commissioning and today. No rule spans "all assets on a line". So ProductionLine is its own tiny aggregate, and Asset references it by id, with a same-schema FK as the safety net.

The benefits:
- loading or saving an asset never touches its siblings;
- two people editing different assets on one line never conflict;
- the transaction is one row.

The rule that *does* span assets, tag uniqueness, can't be enforced by any single aggregate, so it's a database constraint.

I don't wrap EF in a repository. The DbContext is internal to the module and already is a unit of work.
</details>

<details>
<summary><strong>Q2. How are your value objects persisted with EF Core 9, and what did you learn about its limits?</strong></summary>

**Model answer:** Typed ids use value converters, which are transparent and still map to `uniqueidentifier` columns.

For `AssetTag`, I first tried a converter, but then a prefix search can't translate to an index-friendly `LIKE`. It either throws or becomes `CAST([Tag] AS nvarchar(max)) LIKE`, which kills the seek. A complex type can't carry an index in EF 9.

So the tag is persisted as a private string field, with the `AssetTag` value object exposed as a getter. The domain API still speaks value objects; only the persistence mapping is primitive. `Location` is the same idea: two scalar columns, with the value object rebuilt on read, because we need an index and an FK on `LineId`.

We also rejected a draft that hand-wrote the indexes into the migration. Anything outside the model snapshot is invisible to future migrations, so the unique index would eventually be lost. On .NET 10, optional complex types would let decommissioning become a proper value object.
</details>

<details>
<summary><strong>Q3. Two users register the same asset tag at the same moment. Walk me through what happens.</strong></summary>

**Model answer:** Both requests validate and normalise the tag, both confirm the line exists, and both call `SaveChanges`. There's deliberately no "does this tag exist?" check, because check-then-act is a race: both checks would pass.

SQL Server's unique index `UX_Assets_Tag` serialises the two inserts. One commits, and the other fails with error 2601. An exception filter catches `DbUpdateException` with that inner error number and throws `ConflictException`, which the global handler turns into a 409 problem response.

An integration test fires two concurrent POSTs against real SQL Server in a container and asserts exactly one 201 and one 409. InMemory or SQLite-based tests would have hidden this, because InMemory doesn't enforce unique indexes.

One subtlety: with retry-on-failure enabled, a commit whose acknowledgement is lost can be retried and come back as a 409 even though it succeeded. So clients should treat a 409 on create as "check whether it exists".
</details>

<details>
<summary><strong>Q4. How do you avoid N+1 queries, and how do you know you have?</strong></summary>

**Model answer:** On the read side I never load entities. The list endpoint uses `AsNoTracking()` and a `Select` projection straight into a DTO, with the line name coming from a join in the same LINQ query. EF emits one SQL statement for the page and one for the count. There are no navigation properties, so nothing can lazy-load.

The proof is a test, not a review comment. An EF `DbCommandInterceptor` counts executed commands, and the integration test seeds 12 assets and asserts exactly 2 commands. If someone later adds a per-row lookup, the test fails.

To inspect the actual SQL I use `ToQueryString()`, which needs no database connection.

Paging is offset-based, because a factory has hundreds of assets, not millions. If measurements in M7 show deep-page cost, I'd move to keyset paging on `(Tag)`, which is already uniquely indexed.
</details>

<details>
<summary><strong>Q5. In Angular, how does the asset list react to filter changes, and where did you use RxJS?</strong></summary>

**Model answer:** The URL is the state. With `withComponentInputBinding()`, each query param becomes a signal input on the page component. `httpResource` builds its request from those inputs, so when an input changes the resource re-fetches automatically, with no subscriptions to manage.

Changing a filter calls `router.navigate([], { queryParams, queryParamsHandling: 'merge' })` and resets the page to 1. That makes filtered views shareable and refresh-safe, and the back button works.

Two subtleties:
- Query params are strings and absent ones arrive as `undefined`, so `page` and `pageSize` use input transforms with fallbacks.
- A new request drops `httpResource`'s value, so a `linkedSignal` keeps the last good page and the table doesn't flash.

RxJS appears in exactly one place: the search box. Keystrokes are a stream over time, and `debounceTime` plus `distinctUntilChanged` is the right tool. The result just calls `navigate`, so it feeds back into the same URL-driven flow.
</details>

---

## 6. Break-it lab: watch EF's wildcard escaping disappear (no database needed)

**Goal:** see the exact SQL EF generates, and prove why the endpoint uses `StartsWith` rather than `EF.Functions.Like`.

1. Run `git checkout -b lab-02`.
2. Create `tests/PlantOps.Modules.Assets.Tests/SqlLab.cs`:
   ```csharp
   using Microsoft.EntityFrameworkCore;
   using PlantOps.Modules.Assets.Domain;
   using PlantOps.Modules.Assets.Infrastructure;

   namespace PlantOps.Modules.Assets.Tests;

   public class SqlLab(ITestOutputHelper output)
   {
       [Fact]
       public void Show_search_sql()
       {
           // ToQueryString never opens a connection, so no database is needed.
           var options = new DbContextOptionsBuilder<AssetsDbContext>()
               .UseSqlServer("Server=unused;Database=unused")
               .Options;
           using var db = new AssetsDbContext(options);

           var search = "50%";
           var sql = db.Assets
               .Where(a => EF.Property<string>(a, Asset.TagField).StartsWith(search))
               .ToQueryString();

           output.WriteLine(sql);
       }
   }
   ```
3. Run it and show its output:
   ```bash
   dotnet test tests/PlantOps.Modules.Assets.Tests --filter "FullyQualifiedName~SqlLab" --logger "console;verbosity=detailed"
   ```
   **Observe** (verified on this codebase):
   ```sql
   DECLARE @__search_0_startswith nvarchar(20) = N'50\%%';
   ... WHERE [a].[Tag] LIKE @__search_0_startswith ESCAPE N'\'
   ```
   The user's `%` became `\%` (a literal percent sign), and EF appended the real wildcard `%`.
4. Now replace the `.Where(...)` line with:
   ```csharp
   .Where(a => EF.Functions.Like(EF.Property<string>(a, Asset.TagField), search + "%"))
   ```
   and run the same command.

   **Observe:**
   ```sql
   DECLARE @__p_1 nvarchar(20) = N'50%%';
   ... WHERE [a].[Tag] LIKE @__p_1
   ```
   There's no escaping and no `ESCAPE` clause. Searching "50%" now matches **every tag that starts with "50"**, and a search of `_` matches everything. It's still parameterised, so it's not SQL injection, but it returns wrong results.
5. **Bonus:** change `search` to `"smt"` and look at the case. Then explain why the endpoint calls `ToUpperInvariant()` on the prefix. (Tags are stored upper-case. Collation might save you on a case-insensitive database, but not on a case-sensitive one, and Azure SQL lets you choose either.)
6. Clean up:
   ```bash
   rm tests/PlantOps.Modules.Assets.Tests/SqlLab.cs && git checkout m2-assets && git branch -D lab-02
   ```

**What you should be able to say afterwards:** "I checked the generated SQL with `ToQueryString`. `StartsWith` escapes LIKE wildcards and stays index-friendly; `EF.Functions.Like` passes them through. That's why the search uses `StartsWith`, and there's an integration test that searches for `%` and `_` and expects no matches."

---

## 7. Measured numbers from this milestone
- **Tests:** 54 domain unit tests plus 9 host tests pass locally. 21 SQL Server integration tests run in CI; on their first CI run 20 passed and 1 failed because of a test bug (pitfall 5), which is fixed.
- **Angular production build:** initial bundle 536.67 kB raw / 127.7 kB estimated transfer (it was 453.44 / 110.8 after M1). Lazy chunks: `assets-list-page` 133.35 kB, `asset-detail-page` 4.10 kB.
- **Generated SQL** for the list query: one statement for the page (with JOIN and `OFFSET/FETCH`) and one `COUNT`. Verified with `ToQueryString`, and asserted by the command-count test.
