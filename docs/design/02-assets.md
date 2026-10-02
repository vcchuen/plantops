# 02 — Assets module (DDD basics)

## Problem
The factory has machines (pick-and-place, reflow ovens, AOI inspection, test fixtures) on production lines. We need to:
- register them;
- move them between lines and stations;
- classify how critical they are (this later drives SLA targets);
- retire them.

All while keeping the data consistent no matter who calls the API or how. This is the first real business logic, so it's where we establish the DDD building blocks every later module reuses.

## Domain model

```
ProductionLine (aggregate)          Asset (aggregate root)
─────────────────────────           ─────────────────────────────────────
Id: ProductionLineId                Id: AssetId
Code: "SMT-1"                       Tag: AssetTag            ← value object
Name: "SMT Line 1"                  Name, Manufacturer, Model, SerialNumber?
                                    Location: (LineId, Station) ← value object
                                    Criticality: A | B | C
                                    Status: InService | Decommissioned
                                    CommissionedOn: DateOnly
                                    Decommissioning?: (On, Reason)
```

**Asset behaviours** are task-based methods, not setters:
- `Register(...)`
- `UpdateDetails(...)`
- `Relocate(Location)`
- `ChangeCriticality(Criticality)`
- `Decommission(on, reason)`

**Invariants** the aggregate protects:
1. A decommissioned asset can't be changed (no relocate, recategorise or edit).
2. Decommissioning requires a reason and can't be dated before commissioning.
3. The tag is normalised (trimmed, upper-case) and must match `^[A-Z0-9]+(-[A-Z0-9]+)*$`, 3–20 characters.
4. The tag is unique across the factory. This one can't be enforced inside one aggregate (see Decision 4).

**Criticality** uses the industry ABC classification:
- **A:** failure stops a line.
- **B:** failure degrades a line.
- **C:** has a workaround.

M4's SLA policy keys off it.

## Decision 1 — Aggregate boundaries

| Option | Pros | Cons |
|---|---|---|
| `ProductionLine` contains a collection of `Asset`s | "Feels" natural; one root | Changing one asset loads and locks the whole line (hundreds of assets); concurrent edits to *different* assets conflict |
| **Two aggregates; Asset references the line by id** | Small aggregates; independent edits; each transaction touches one aggregate | "Does this line exist?" is checked when the command runs (a same-schema FK backs it up) |

**Decision: two aggregates.**
- The rule of thumb (Vernon): *design small aggregates; reference other aggregates by identity*.
- An aggregate is a **consistency boundary**, not a containment hierarchy. No invariant spans "all assets on a line", so there is no reason to load them together.

## Decision 2 — Value objects and how EF Core maps them
**Primitive obsession** means passing `string tag`, `Guid lineId` and `string station` around everywhere. The validation then has to be repeated (or forgotten) at every call site, and `Relocate(stationName, lineId)` compiles even with the arguments swapped.

| Concept | Type | EF Core 9 mapping |
|---|---|---|
| `AssetId`, `ProductionLineId` | `readonly record struct` wrapping a `Guid` | Value converter. The column is still `uniqueidentifier` |
| `AssetTag` | `sealed record` with private constructor + `Create()` | Value converter to `nvarchar(20)` |
| `Location` (LineId + Station) | `sealed record` | **Complex type** (EF 8+): two columns on the `Assets` table, no identity, compared by value |
| `Decommissioning` (On + Reason) | `sealed record` | Complex type. Its absence means the asset is in service (see below) |

**Why complex types, not owned types?**
- Owned types (the older approach) secretly have a shadow key and identity semantics. That makes them entity-like, which is the wrong model for a value object.
- Complex types are genuinely value-based.
- **Limitation in EF Core 9:** a complex type can't be optional. So "not decommissioned" is modelled as two nullable columns on the Asset itself (`DecommissionedOn`, `DecommissionReason`), with a domain method that returns the value object. This is a known limitation; it's fixed in EF Core 10, which supports optional complex types.

**Why not a value converter for `Location`?** A converter maps one property to one column. Location is two columns, and we want `Location.LineId` to be indexable and filterable in SQL.

## Decision 3 — Reporting rule violations

| Option | Pros | Cons |
|---|---|---|
| **Domain exceptions** (`DomainException`, `ConflictException`, `NotFoundException`) mapped centrally to problem+json by an `IExceptionHandler` | Aggregate methods stay clean and return `void`; can't be ignored; one mapping place | Exceptions for expected outcomes is debated; slightly costlier (irrelevant at this traffic) |
| `Result<T>` / `OneOf` returned from every method | Failures are explicit in the signature | Every caller must check and propagate; ceremony spreads; easy to `_ =` discard |
| FluentValidation at the edge only | Great field-level messages | Invariants then live outside the aggregate, so a second caller (a PM scheduler in M6) can bypass them |

**Decision: domain exceptions, mapped to RFC 9457 problems** (400 for rule violations, 404, 409).
- The invariant lives *inside* the aggregate, so every caller gets it for free.
- Field-shaped input checks (required, max length) are done by the value-object factories, so there's one source of truth.
- In an interview, say you know the `Result` camp's argument and when you'd switch: when failures become a normal, high-frequency control flow, for example bulk imports reporting per-row errors.

## Decision 4 — Tag uniqueness: a cross-aggregate rule
- **Option A, the app check:** "check, then insert" (`AnyAsync(tag)` then `Add`). This **is a race**: two requests both pass the check, and both insert.
- **Option B, the database:** a unique index on `Tag` is the only thing that's actually atomic. The endpoint catches `DbUpdateException` with SQL error 2601/2627 and returns **409 Conflict**.

**Decision: unique index + translate the violation.** No pre-check. It would only duplicate the rule and still be racy. *Native platform feature over app code.*

## Decision 5 — Data access: DbContext directly (no repository layer)
See ADR-0005. `AssetsDbContext` is `internal` to the module and is already a unit of work plus repository. Endpoints load the aggregate, call a method and `SaveChangesAsync()`.

## Decision 6 — Migrations
See ADR-0004. There is one migration set per module, each with its own history table inside its own schema (`assets.__EFMigrationsHistory`). Migrations are applied at startup **only** when `Database:ApplyMigrationsOnStartup=true`, which is set by compose. Production applies them as a pipeline step (M9).

## Decision 7 — Commands: task-based endpoints, not CRUD `PUT`

| Option | Pros | Cons |
|---|---|---|
| `PUT /api/assets/{id}` with the whole object | One endpoint; familiar | Intent is lost (was this a relocation or a typo fix?); clients can try to set `Status` directly; audit in M4 can't say *what happened* |
| **Task-based** `POST /api/assets/{id}/relocate`, `/criticality`, `/decommission` + `PUT /details` for plain edits | Each endpoint maps 1:1 to an aggregate method; intent is explicit; the M4 audit trail gets meaningful events | More endpoints |

**Decision: task-based.**

## Decision 8 — Read side (list/detail)
- Reads **project straight to DTOs** with `AsNoTracking()` + `Select(...)`. This loads no aggregates and tracks nothing, and it makes **N+1 impossible by construction**: one SQL statement per request. The line name comes through a join in the same projection.
- The list is filtered by `lineId`, `criticality`, `status` and `search`, where search is a prefix match on tag *or* a contains match on name.
- **Offset paging** (`page`, `pageSize`, capped at 100) with `totalCount`.
  - Keyset paging is faster on deep pages, but a few hundred assets will never have deep pages.
  - **M7 measures** before we change anything.
- This is not full CQRS. The reads use the same DbContext and simply skip the domain model. Real CQRS (separate read models) is reserved for reporting (M7), where the read shape is very different from the write shape.

## Decision 9 — Integration tests with real SQL Server
- **Why not the EF InMemory provider or SQLite?**
  - InMemory doesn't enforce unique indexes, so it would happily pass the race test in Decision 4.
  - SQLite has different SQL semantics: collation, `datetimeoffset`, schemas.
- **Testcontainers** starts a real SQL Server container per test run, applies the migrations, and runs the tests against it.
- **Constraint for now:** you've deferred database setup, and the image is about 1.6 GB.
  - The integration tests skip themselves unless `PLANTOPS_INTEGRATION_TESTS=1` is set. The skip is visible: xUnit v3 reports it as *skipped*, not passed.
  - CI sets the variable, so they always run there.

## Frontend
- **Asset list** (`/assets`):
  - Material table, filters (line, criticality, status, search) and a paginator.
  - **Filters live in the URL query string**, so a filtered view is shareable and survives refresh and the back button. Router `withComponentInputBinding()` turns query params into component **signal inputs**.
  - The `httpResource` URL function reads those signals, so changing a filter re-fetches automatically. No subscriptions, no manual reload.
  - Search is debounced, which is a genuine stream problem and the one place RxJS earns its keep here.
- **Asset detail** (`/assets/:id`): the `id` route param is an input signal, read by `httpResource`.
- Create and edit forms come in M4, built with Signal Forms. M2 stays read-only in the UI; the API commands are covered by integration tests.

## Out of scope
Auth (M3), optimistic concurrency and audit (M4), maintenance history (built from WorkOrders events in M5).
