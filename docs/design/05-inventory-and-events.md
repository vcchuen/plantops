# 05 — Inventory, reservations and events between modules

## Problem
1. **Spare parts:** technicians reserve spare parts for a work order. Two technicians must never both get the last unit of a part.
2. **Completion side effects:** when a work order is **completed**, two other modules must react:
   - **Inventory** consumes the reserved parts (stock goes down);
   - **Assets** records the job in the machine's **maintenance history**.

   When a work order is **cancelled**, its reservations are released.
3. **Module boundaries:** WorkOrders must not call into Inventory's or Assets' internals (ADR-0001). The reactions must not be lost if the process crashes between "work order saved" and "stock updated".

## Domain model (schema `inventory`)
```
SparePart (aggregate root)
  Id, PartNumber (unique, e.g. "FDR-8MM-001"), Name, Unit ("pcs"), BinLocation ("A-03-2")
  QuantityOnHand, QuantityReserved, ReorderLevel      Available = OnHand − Reserved
  Reservations: [ Reservation(Id, WorkOrderId, Quantity, Status: Active|Consumed|Released, ...) ]
  RowVersion
  Receive(qty), Reserve(workOrderId, qty), Release(reservationId), ConsumeFor(workOrderId), ReleaseFor(workOrderId)
```
**Invariants:**
- `0 ≤ Reserved ≤ OnHand` at all times.
- You can't reserve more than `Available`.
- Quantities are positive.
- At most **one active reservation per (part, work order)**. Reserving again increases it.

## Decision 1 — The last-unit race: optimistic concurrency with *automatic retry*
- **The problem:** two technicians each reserve the last unit. Both load `Available = 1`, both pass the check, both save, and stock goes negative.
- **The options:**
  1. **Atomic SQL** (`UPDATE … SET Reserved = Reserved + @q WHERE Id = @id AND OnHand - Reserved >= @q`). It's correct, but the rule moves out of the aggregate into SQL.
  2. **A pessimistic row lock** (`UPDLOCK`). Correct, but it serialises *all* reservations on a part and couples domain code to locking hints.
  3. **Optimistic: the aggregate has a `RowVersion`, so the second save fails.** Then **retry the whole command** from a fresh read: reload the part, re-run `Reserve`, which now sees `Available = 0` and throws a domain error (409 "Only 0 available").
- **Decision: 3, with up to 3 automatic retries.**
- **Contrast with M4:** M4 *refuses* conflicts with 412, because a human decided based on stale data and must look again. Here the decision is *the server's* ("is there stock?"), so the server can simply re-decide on fresh data. **Same mechanism, different policy, chosen per use case.** This is a favourite interview topic.
- **How the retry runs:** a small `RetryOnConcurrencyConflict` helper. It uses a fresh `DbContext` scope per attempt, so no stale tracked entities survive into the next attempt.

## Decision 2 — Events between modules: transactional outbox + idempotent inbox (ADR-0009)

| Option | What goes wrong |
|---|---|
| WorkOrders calls `IInventoryService.Consume()` in-process after saving | If the process dies after WorkOrders commits, Inventory never hears. If you call it *before* committing, Inventory may consume stock for a completion that then rolls back. Either way, modules are temporally coupled |
| In-memory event bus (MediatR notifications) after `SaveChanges` | Same crash window; events vanish on restart |
| Publish straight to Service Bus after `SaveChanges` | The "dual write" problem: the DB commit and the broker publish can't be atomic |
| **Transactional outbox** | ✓ The event is a row written **in the same transaction** as the change; a dispatcher delivers it later, at least once |

**The mechanics:**
1. **Integration events** are public records in the producer's **Contracts** project:
   - `WorkOrderCompletedIntegrationEvent(WorkOrderId, Number, AssetId, Title, Resolution, CompletedAt, TechnicianName, AssetDown, SubmittedAt, StartedAt)`;
   - `WorkOrderCancelledIntegrationEvent(WorkOrderId, …)`.

   They're versioned contracts: add fields, never repurpose them.
2. **Mapping:** domain events stay internal. A per-module `IIntegrationEventMapper` turns selected domain events into integration events. The existing `DomainEventInterceptor` (M4) asks the mapper and writes an **`OutboxMessages`** row (Id, Type, Payload, OccurredAt, ProcessedAt, Attempts, LastError) in the **same SaveChanges**. Audit and outbox are written together, atomically.
3. **The dispatcher:** a generic `OutboxDispatcher<TContext>` (BackgroundService, one per producing module).
   - It polls every few seconds.
   - It claims a batch with `SELECT TOP (n) … WITH (UPDLOCK, READPAST, ROWLOCK) WHERE ProcessedAt IS NULL ORDER BY OccurredAt` inside a transaction, so two app instances never claim the same rows.
   - It invokes every registered `IIntegrationEventHandler<T>` in a new DI scope, then marks the row processed.
   - On a handler failure it increments `Attempts`, records `LastError`, and moves on. After 5 attempts the message is parked: it's left unprocessed and logged as an error, for an operator to look at.
4. **Consumers are idempotent (inbox).** The delivery guarantee is at-least-once: a crash after the handler commits but before the outbox row is marked means **redelivery**.
   - Each consuming module has an **`InboxMessages`** table (MessageId, Handler, ProcessedAt) with a unique key on (MessageId, Handler).
   - A handler writes its effect **and** its inbox row in one SaveChanges.
   - A duplicate hits the unique key, the change is rolled back, and the handler treats that as "already done".
5. **M6** adds a second destination: some events also go to **Azure Service Bus** (SLA breach notifications) through the same outbox.

**Consumers in this milestone:**
- **Inventory:** `WorkOrderCompleted` → `ConsumeFor(workOrderId)` on every part with an active reservation. `WorkOrderCancelled` → `ReleaseFor(workOrderId)`.
- **Assets:** `WorkOrderCompleted` → inserts a `MaintenanceRecord` (AssetId, WorkOrderId, Number, Title, Resolution, TechnicianName, CompletedAt, DowntimeMinutes when `AssetDown`) into `assets.MaintenanceRecords`. This is the "maintenance history" from the spec. `GET /api/assets/{id}/maintenance` lists them.

**Eventual consistency, stated plainly:** for a few seconds after "Complete", stock still shows the reserved parts as reserved. The UI says "Stock is updated shortly after completion". That's the honest trade-off of not coupling the modules.

## Decision 3 — Reserving parts requires knowing about the work order
- Inventory must check that the work order exists and is **Assigned or InProgress**, and that the caller is a supervisor **or the assigned technician**.
- `WorkOrders.Contracts` exposes `IWorkOrderDirectory.FindAsync(id) → WorkOrderSummary? (Id, Number, Status, AssignedToId)`, the same pattern as `IAssetDirectory` in M4.
- This is a *synchronous* read across modules. That's fine for queries; ADR-0001 explains how it becomes an HTTP call if the module is ever extracted.

## API
```
GET  /api/inventory/parts?search=&lowStock=true&page=&pageSize=
GET  /api/inventory/parts/{id}                          (includes active reservations)
POST /api/inventory/parts            { partNumber, name, unit, binLocation, reorderLevel }      inventory:manage
POST /api/inventory/parts/{id}/receive   { quantity }                                           inventory:manage
GET  /api/inventory/reservations?workOrderId=
POST /api/inventory/reservations     { partId, workOrderId, quantity }   assigned technician or supervisor/admin
POST /api/inventory/reservations/{id}/release                            assigned technician or supervisor/admin
GET  /api/assets/{id}/maintenance
```
- **New policy:** `inventory:manage` = supervisor or admin.
- **Errors:** insufficient stock → **409** problem ("Only 2 pcs of FDR-8MM-001 available"); work order not reservable → 400.

## Frontend
- **`/inventory`:** parts list with a "low stock" filter (`Available ≤ ReorderLevel`), shown as text plus an icon.
- **Part detail:** stock figures, active reservations with work order links, and "Receive stock" for managers.
- **Work order detail:** a **Parts** section listing reservations and a "Reserve part" dialog (part search plus quantity), shown when status is Assigned/InProgress and the user may work it or supervise. After completion it shows the "updated shortly" note.
- **Asset detail:** a **Maintenance history** section.

## Out of scope
- Purchase orders and suppliers.
- Part cost (reports would need it).
- Parking-lot UI for failed outbox messages (logged only).
