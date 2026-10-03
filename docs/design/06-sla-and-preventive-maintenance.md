# 06 — SLA escalation and preventive maintenance

## Problem
1. **Escalation:** when a work order **breaches its SLA** (open past `DueAt`), supervisors must be notified **once**, reliably, even if the web app is restarting.
2. **Preventive maintenance (PM):** machines need **preventive maintenance** on a schedule, for example "Clean reflow oven RF-02 every 30 days". The system must raise those work orders automatically, a few days ahead, **without ever creating duplicates**.

## Decision 1 — Escalation is a domain transition, detected by a scheduled job
- **The aggregate:** `WorkOrder.Escalate(now)`:
  - is allowed only for open statuses when `now > DueAt`;
  - is **idempotent**: it does nothing if already escalated;
  - sets `EscalatedAt` and raises `WorkOrderSlaBreached`.
- **The job:** `SlaEscalationRunner.RunAsync(now)` queries `Status IN (open) AND DueAt < @now AND EscalatedAt IS NULL` (index on `(DueAt)` filtered `EscalatedAt IS NULL`), calls `Escalate`, and saves each work order **separately**. A conflict on one (rowversion, e.g. a technician completing it at that very moment) is logged and skipped, and the next run re-evaluates it.
- **The SLA *state* is still computed on read** (M4). Escalation is a separate, stored *fact*: "we notified people at 14:05".

## Decision 2 — Notifications leave through the outbox to Azure Service Bus
- `WorkOrderSlaBreached` maps to the integration event `WorkOrderSlaBreachedIntegrationEvent(WorkOrderId, Number, Title, AssetTag, Priority, DueAt, EscalatedAt, AssignedToName?)`, written to the outbox in the same transaction (M5 machinery).
- **An outbox handler forwards it to the Service Bus queue `sla-breaches`.**
  - `MessageId` = the outbox message id, so Service Bus **duplicate detection** can drop a re-forward after a crash.
  - When `ServiceBus:FullyQualifiedNamespace` is configured it uses **managed identity** (`DefaultAzureCredential`). When `ServiceBus:ConnectionString` is set it uses that (local emulator). Otherwise it uses a logging publisher (developer laptop, tests).
- **A consumer Azure Function (`SlaBreachNotifier`, Service Bus trigger)** turns the message into a notification. In this project that means a structured log plus an optional Teams incoming-webhook post (`Notifications:TeamsWebhookUrl`).
  - The queue gives retries, a **dead-letter queue** after the max delivery count, and decouples "the app noticed" from "someone was told".
- **Why a queue rather than "just call the Teams webhook from the API"?** If Teams is down, the queue holds the message and the Function retries; the API never blocks on a third party. *Fail-isolated side effects* is the senior phrase.

## Decision 3 — Preventive maintenance schedules
```
PmSchedule (aggregate, schema workorders)
  Id, AssetId + AssetTag + AssetName (snapshot), Title, Instructions
  IntervalDays (1..365), LeadDays (0..30), Priority, NextDueOn (DateOnly), IsActive, RowVersion
  Create(...), Update(...), Deactivate(), Activate()
  GenerateIfDue(today) → (WorkOrder?)   advances NextDueOn by IntervalDays when it generates
```
- **Generation rule:** when `today >= NextDueOn − LeadDays` and the schedule is active, create a work order and advance `NextDueOn += IntervalDays`.
  - **Catch-up:** if the job was down for 3 intervals, generate **one** work order, for the oldest due date, and advance past `today`. Three identical PM jobs for one machine help nobody.
- **The PM work order:**
  - `Source = Preventive`, `PmScheduleId`, `PmDueOn`;
  - it starts **Approved**, because a planned job needs no approval;
  - it's raised by the **System** actor;
  - `DueAt` = the end of `PmDueOn` in factory local time, so the SLA for PM work is the due date, not a priority target.
- **No duplicates, guaranteed by the database:** a **filtered unique index on `WorkOrders (PmScheduleId, PmDueOn) WHERE PmScheduleId IS NOT NULL`**.
  - The runner also checks first, but the index is the arbiter (the same lesson as the asset tag in M2).
  - If the timer fires twice, or two instances race, the second insert hits 2601 and is treated as "already generated".
- **Factory time zone:** `Factory:TimeZone = "Asia/Kuala_Lumpur"` (UTC+8, no DST). "Today" for PM is the **factory's** calendar date, not UTC. At 07:30 in Penang it's still yesterday in UTC.
  - Fixed here and tested with `FakeTimeProvider`.
  - This also settles the "which day is today?" question deferred from M2's decommission date.

## Decision 4 — Where scheduled jobs run (ADR-0010)

| Option | Pros | Cons |
|---|---|---|
| `BackgroundService` in the web app | Simple; one deployable | Runs once *per instance* (needs its own leader election); stops when App Service idles or restarts; competes with web traffic |
| Hangfire / Quartz in the web app | Rich scheduling, dashboards | Another dependency and its own storage; same "lives in the web process" issue |
| **Azure Functions timer trigger** | Platform guarantees a **single execution** across instances (blob lease); Consumption/Flex plan costs almost nothing; independent of web deploys | Another deployable; local run needs Functions Core Tools |

**Decision:**
- **The job logic lives in the WorkOrders module** behind public contracts in `WorkOrders.Contracts`: `ISlaEscalationRunner` and `IPreventiveMaintenanceRunner`.
- **Azure:** an **isolated-worker Azure Functions app** (`src/Functions/PlantOps.Functions`) calls `AddWorkOrdersModule()` and invokes the runners from timer triggers:
  - SLA every 5 minutes;
  - PM daily at 06:00 factory time (22:00 UTC).
- **Locally:** without the Functions host, `Jobs:RunInProcess = true` makes the API run the same runners from a `BackgroundService`. This is for development only and is documented as single-instance.

## API (all `workorders:supervise` except reads)
```
GET  /api/pm-schedules?assetId=&active=           any signed-in user
GET  /api/pm-schedules/{id}
POST /api/pm-schedules        { assetId, title, instructions, intervalDays, leadDays, priority, nextDueOn }   → 201 + ETag
PUT  /api/pm-schedules/{id}   { title, instructions, intervalDays, leadDays, priority, nextDueOn }   If-Match → 204 + ETag
POST /api/pm-schedules/{id}/deactivate | /activate   If-Match → 204 + ETag
```
- Work order list/detail gain `source` (`Reactive | Preventive`), `escalatedAt`, and `pmScheduleId`.
- The list gains an `escalated=true` filter.

## Frontend
- **`/pm-schedules`:** a list (asset, title, every N days, next due, active), and create/edit with Signal Forms. The date input is native `<input type="date">` bound to a string. Deactivate/activate use If-Match.
- **Work order list and detail:** a "Preventive" chip, an "Escalated at …" badge (icon plus text), and an "Escalated only" filter.

## Out of scope
- Meter-based PM (every N cycles).
- Email notifications.
- Escalation levels beyond the first.
