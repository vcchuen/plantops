# ADR-0001: A modular monolith, not microservices

- **Status:** Accepted
- **Date:** 2026-10-03

## Context
PlantOps has four capabilities: Assets, WorkOrders, Inventory and Identity. They are clearly separate, but they collaborate heavily. For example, completing a work order consumes spare parts and changes asset history. The system serves one factory, with tens to hundreds of concurrent users, and one team owns it.

Microservices buy **independent deployment and independent scaling per team**. They pay for that with:
- network calls where you used to have method calls (latency, partial failure, retries, timeouts);
- distributed transactions or sagas where one database transaction used to do;
- N pipelines, N deployables and N sets of dashboards;
- harder local development and harder end-to-end testing.

None of those benefits applies to one team serving one factory. All of the costs would.

## Decision
Build a **modular monolith**: one deployable unit containing modules with hard boundaries.
- Each module owns its **own database schema** (`assets.*`, `workorders.*`, `inventory.*`, `identity.*`) and its own `DbContext`. No module queries another module's tables.
- Modules talk to each other only through:
  1. their `*.Contracts` project (public query interfaces and DTOs), for synchronous reads; and
  2. **integration events**, delivered through an outbox, for side effects (from M5).
- The compiler (`internal` types) and an architecture test enforce these boundaries.

## How to extract a module later
Suppose Inventory needs to move to a separate service, for example because a warehouse team takes ownership. The steps are:
1. **Data:** Inventory already lives in its own schema with no foreign keys into other schemas. Move the schema to its own database. No other module's queries break, because none reference it.
2. **Events:** today the outbox dispatcher hands integration events to in-process handlers. Point it at Azure Service Bus instead. The event contracts in `Inventory.Contracts` don't change.
3. **Sync queries:** replace the in-process implementation of `IInventoryQueries` in other modules with an HTTP client that implements the same interface. Callers don't change.
4. **Host:** create a new ASP.NET Core host that calls `AddInventoryModule()` and `MapInventoryEndpoints()`. The module code moves unchanged.

Extraction is mechanical *because* the boundaries were enforced from day one. That is the whole bet of a modular monolith.

## Consequences
- Pros: one deployment, real ACID transactions inside a module, simple local development, cheap Azure hosting (a single App Service).
- Cons: the whole application scales as one unit, a bad module can take down the process, and everything deploys together.
- Risk: boundaries erode under deadline pressure. That's why the architecture test runs in CI, so a violation fails the build instead of waiting for a reviewer to notice.

## Alternatives considered
- **Microservices:** rejected for the reasons above. The organisational driver (many teams) is missing.
- **Traditional layered monolith (one DbContext, shared tables):** rejected. It's the fastest way to start, but nothing stops `WorkOrders` from joining `inventory.Parts` directly. A year later, extracting anything means untangling every query.
