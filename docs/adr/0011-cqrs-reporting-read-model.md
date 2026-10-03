# ADR-0011: CQRS read model for reporting only

- **Status:** Accepted
- **Date:** 2026-10-03

## Context
Reports aggregate completed work orders by production line, asset, priority and month. The line lives in the Assets module, and ADR-0002 forbids cross-schema joins. The write tables are indexed for transactional access (by id, status and assignee), not for time-range aggregation.

## Decision
A **Reporting module** owns a denormalised read model, `reporting.WorkOrderFacts`. It's populated from `WorkOrderCompletedIntegrationEvent` through the outbox, with an inbox for idempotency. The asset's line is resolved at completion time. Report endpoints query only this table.

Every other screen keeps reading its own module's tables through projections. **There's no general CQRS framework and no MediatR.** Using CQRS everywhere would double the model code for screens whose read and write shapes are nearly identical.

## Consequences
- **Reporting can't break writes:** reporting indexes and queries never touch transactional tables.
- **Extraction path:** the read model is the natural thing to move to a reporting database or hand to Power BI.
- **Eventual consistency** of a couple of seconds.
- **Rebuildable:** projections must be rebuildable, so `POST /api/reports/rebuild` re-projects from the source module through a contract query. This is also how pre-existing data gets in.
- **Attribution is historical:** facts record the line *at completion*, which is the right answer for "downtime by line last quarter", even if machines move later.
