# ADR-0002: Module structure, one schema per module

- **Status:** Accepted
- **Date:** 2026-10-03

## Decision
Each module consists of two projects:
- `PlantOps.Modules.<Name>`: all of the module's code, `internal` by default. Its only public surface is `Add<Name>Module(IServiceCollection, IConfiguration)` and `Map<Name>Endpoints(IEndpointRouteBuilder)`.
- `PlantOps.Modules.<Name>.Contracts`: public integration events, query interfaces and DTOs that other modules may depend on.

The module rules:
- **Allowed references:** a module may reference `PlantOps.SharedKernel` and other modules' `.Contracts` projects only.
- **Data ownership:** each module has its own `DbContext` with `HasDefaultSchema("<name>")` and its own migrations history table in that schema.
- **No cross-schema foreign keys.** For example, a work order stores an `AssetId` as a plain value. Referential checks across modules happen in application code or through events.

See `docs/design/01-foundation.md` for the options considered.

## Consequences
- You can't do a database join from WorkOrders to Assets. A screen that needs both either:
  - composes two queries in the API, or
  - (for reporting, M7) reads a projection that is deliberately denormalised.
- Migrations are per module, so you can deploy schema changes for one module without touching the others.
