# ADR-0007: Audit trail from domain events, written in the same transaction

- **Status:** Accepted
- **Date:** 2026-10-03

## Context
Every state change must be auditable: *who* did *what* to *which* asset or work order, and *when*. A sibling need follows in M5: other modules must react to these changes reliably.

## Decision
Aggregates raise domain events (`AggregateRoot.Raise`). A shared EF Core `SaveChangesInterceptor` (in `PlantOps.BuildingBlocks.Infrastructure`) runs just before each save. It:
1. collects the pending events from tracked aggregates;
2. converts each one into an `AuditEntry`: aggregate type and id, event type, JSON payload, actor id and name (from `ICurrentUser`), and time (from `TimeProvider`);
3. adds the entries to the **same** DbContext, so they're inserted in the same database transaction as the change;
4. clears the events.

Each module maps `AuditEntries` in its own schema.

## Consequences
- **No gap between change and audit:** the change and its audit row are atomic. There's no "saved, but the audit write failed" window.
- **Readable entries:** entries describe business facts ("WorkOrderAssigned to Tom by Sam"), not column diffs.
- **Design work:** events need deliberate design. Their payload is a contract with auditors.
- **Reuse:** M5 extends the same interceptor to write **outbox** rows for integration events. That's one mechanism for both needs.
- **Rejected alternatives:** temporal tables can't say who or why. Column-diff interceptors are noisy and miss intent. History collections on aggregates duplicate logic in every aggregate.
