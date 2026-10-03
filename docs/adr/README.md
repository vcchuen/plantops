# Architecture decision records

Each ADR records one decision: its context, the options, the choice and its consequences. They're short on purpose. The longer reasoning, with alternatives tables, lives in the milestone design docs in [`../design`](../design).

| # | Decision | Status |
|---|---|---|
| [0001](0001-modular-monolith.md) | A modular monolith, not microservices; how to extract a module later | Accepted |
| [0002](0002-module-structure-and-schemas.md) | Module = implementation + Contracts project; one schema per module | Accepted |
| [0003](0003-target-dotnet-9.md) | Target .NET 9 (STS) instead of .NET 10; upgrade path | Accepted, revisit |
| [0004](0004-migration-strategy.md) | Migrations: startup only behind a flag locally; bundles in the pipeline | Accepted |
| [0005](0005-dbcontext-no-repositories.md) | Use the module DbContext directly; no generic repository | Accepted |
| [0006](0006-bff-cookie-auth.md) | BFF cookie authentication; no tokens in the browser | Accepted |
| [0007](0007-audit-via-domain-events.md) | Audit trail from domain events, in the same transaction | Accepted |
| [0008](0008-etag-optimistic-concurrency.md) | Optimistic concurrency with rowversion + ETag/If-Match | Accepted |
| [0009](0009-outbox-inbox.md) | Transactional outbox + idempotent inbox between modules | Accepted |
| [0010](0010-scheduled-jobs.md) | Scheduled jobs in Azure Functions; logic stays in the module | Accepted |
| [0011](0011-cqrs-reporting-read-model.md) | CQRS read model for reporting only | Accepted |

**Format:** status, date, context, decision, consequences, and alternatives where they matter. Superseding an ADR means writing a new one and marking the old one "Superseded by NNNN". ADRs are never rewritten.
