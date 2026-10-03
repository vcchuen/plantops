# Study guide 06: SLA escalation and preventive maintenance

> Goal: you can explain idempotent background jobs, why scheduled work moved to Azure Functions, how Service Bus decouples "we noticed" from "someone was told", and why "what day is it?" is a design decision in a Penang factory.

---

## 1. Concepts from first principles

### 1.1 Idempotent jobs: "running twice is harmless"
Schedulers fire twice, instances race, and processes restart mid-run. So instead of trying to guarantee "exactly once", we make each job **idempotent**: running it again changes nothing.
- **SLA escalation:** the query only picks work orders with `EscalatedAt IS NULL`, and `WorkOrder.Escalate()` is a no-op if already escalated.
- **Preventive maintenance:** a **filtered unique index** on `(PmScheduleId, PmDueOn)` makes a duplicate insert *impossible*. A losing racer gets SQL error 2601 and treats it as "already generated".

Same lesson as the asset tag (M2): the database constraint is the arbiter, and the app-side pre-check is only an optimisation.

### 1.2 Stored fact vs computed state
- The SLA **state** (OnTrack/AtRisk/Breached…) is still computed on read (M4).
- **Escalation** is different: "we notified people at 14:05" is a historical *fact*, so `EscalatedAt` is stored.
- Ask "is this a function of now, or something that happened?" to decide whether to store it.

### 1.3 Where scheduled work runs (ADR-0010)
- **A `BackgroundService` in the web app:**
  - runs once *per instance*, so 3 instances means 3 runs;
  - stops when the app idles or redeploys;
  - competes with web traffic.
- **An Azure Functions timer trigger:** Microsoft documents that *"only a single instance of a timer-triggered function is run across all instances"*, using a storage lock. It runs independently of web deploys and costs almost nothing.
- **The logic is still the module's:** `ISlaEscalationRunner` and `IPreventiveMaintenanceRunner` are public contracts. The Functions app is just a *second host* calling them. That's the modular monolith's extraction story in practice.

### 1.4 Service Bus and fail-isolated side effects
- Sending a Teams message directly from the API means a Teams outage either fails our request or silently drops the notification.
- Instead the breach is an integration event: outbox → forwarder → **Service Bus queue** → a **Function with a Service Bus trigger** → Teams.
- If Teams is down, the Function throws, Service Bus **redelivers**, and after `MaxDeliveryCount` (5) the message goes to the **dead-letter queue**, where an operator can inspect it.
- **Duplicate detection:** the forwarder sets `MessageId` = the outbox message id. If the dispatcher crashes after sending but before marking the row processed, the re-send carries the same `MessageId`, and Service Bus (with duplicate detection on) drops it.

### 1.5 Time zones: "today" is a business decision
- At 07:30 in Penang it's still 23:30 *yesterday* in UTC. A PM job "due today", computed in UTC, would be a day late every morning.
- **`FactoryClock`** answers *"what calendar day is it at the factory?"* using `Asia/Kuala_Lumpur`.
- **Three places where time zones bit us or nearly did:**
  1. **Server "today":** fixed by `FactoryClock`. M2's decommission date now uses it too.
  2. **Function schedules:** Azure Functions cron is **UTC**, and Microsoft documents that *`WEBSITE_TIME_ZONE` and `TZ` aren't supported on Linux Consumption/Flex Consumption*. So "06:00 Penang" is written as `0 0 22 * * *` (22:00 UTC), with a comment.
  3. **Browser rendering of date-only values:** see the lab, and the reviewer's own mistake in pitfall 1.

### 1.6 PM schedule catch-up
- If the job was down for three intervals, generate **one** work order (the oldest due date) and advance past today. Three identical "clean the oven" jobs help nobody.
- The implementation is stricter still: it advances until the *next* occurrence's lead window starts after today, so a catch-up can never produce two work orders in one day.

---

## 2. Guided code tour

### Building blocks and domain
1. **`BuildingBlocks.Infrastructure/FactoryClock.cs`**
   - `Today` and `EndOfDay(date)` (23:59:59.9999999 local, returned as UTC).
   - `FindSystemTimeZoneById("Asia/Kuala_Lumpur")` works on Linux because the .NET runtime image installs `tzdata`; we checked the Dockerfile.
2. **`WorkOrders/Domain/WorkOrder.cs`**. *Notice:*
   - `Escalate(now)` returns a bool and is a no-op when already escalated or not yet due;
   - `RaisePreventive(...)` creates an **Approved** work order raised by `SystemActor`;
   - `Source`, `PmScheduleId`, `PmDueOn`.
3. **`Domain/SlaPolicy.cs`**
   - SLA state is now judged against the **stored `DueAt`**. A PM work order's deadline is its due date, not a priority target. The agent caught this, and it's correct.
4. **`Domain/PmSchedule.cs`**
   - Validation ranges, and `GenerateIfDue(today, now, dueAtOf)`. The deadline function is *passed in*, so the domain reads no clock and no zone.
   - Note `PmOccurrenceAlreadyGenerated`: even the "someone else generated it" path is audited.

### Persistence
5. **`Infrastructure/WorkOrderConfiguration.cs` + migration `AddSlaEscalationAndPm`**. *Notice:*
   - the **filtered unique** `UX_WorkOrders_PmSchedule_DueOn` (`WHERE PmScheduleId IS NOT NULL`);
   - the filtered `IX_WorkOrders_DueAt_NotEscalated` (`WHERE EscalatedAt IS NULL`), so the escalation query scans only candidates;
   - `Source` added with default `'Reactive'`, so existing rows are backfilled by the migration. This is an *expand*-style, backward-compatible change (ADR-0004).

### Jobs
6. **`WorkOrders/Jobs/SlaEscalationRunner.cs`**
   - Saves **one work order per SaveChanges**. A concurrency conflict on one (a technician completed it at that instant) is logged and skipped; the next run re-evaluates.
7. **`Jobs/PreventiveMaintenanceRunner.cs`**
   - Generates and advances in one SaveChanges. A unique violation → reload, record "already generated", advance.
8. **`Jobs/InProcessJobs.cs`**: the dev-only `BackgroundService`, active when `Jobs:RunInProcess=true`.

### Messaging
9. **`WorkOrders/Integration/SlaBreachPublishing.cs`**. *Notice:*
   - `ServiceBusClient` is a **singleton**: it's thread-safe and expensive to create.
   - Managed identity (`DefaultAzureCredential`) when `FullyQualifiedNamespace` is set; a connection string for the local emulator; otherwise a logging publisher.
   - `MessageId` = the outbox id, for duplicate detection.
10. **`deploy/servicebus/Config.json` + the `servicebus` compose service** (profile `messaging`). This is the official emulator, with property names taken from Microsoft's docs (`DuplicateDetectionHistoryTimeWindow`).

### Functions app
11. **`src/Functions/PlantOps.Functions/Program.cs`**
    - The isolated worker. It calls `AddWorkOrdersModule` and a `NoCurrentUser`, so audit entries say "System".
12. **`TimerFunctions.cs`**
    - `0 */5 * * * *` for SLA, and `0 0 22 * * *` for PM. Read the time zone comment.
13. **`SlaBreachNotifier.cs`**
    - A Service Bus trigger that posts an Adaptive Card to Teams if configured, and **throws on failure** so Service Bus retries and eventually dead-letters.
    - *Notice* the queue name is the flat setting `%SlaBreachQueue%`: `%a:b%` isn't guaranteed to resolve in the Functions host.

### API and frontend
14. **`Endpoints/PmScheduleEndpoints.cs`**
    - The same ETag/If-Match pattern as work orders, plus `/history`. The history endpoint was **missing** until CI's integration tests called it; it was added in review.
15. **`web/src/app/features/pm-schedules/*`**
    - Signal Forms with a native `<input type="date">` bound to a string.
    - Edit mode repopulates from the resource on 412, so stale edits are replaced by the latest version.
16. **`work-orders-list` / `detail`**: the "Preventive" chip, the escalated indicator (icon plus text), and the "Escalated only" filter.

---

## 3. Why this, not that

| Choice | Instead of | Why |
|---|---|---|
| Idempotent runners + DB unique index | "Make sure the timer only fires once" | You can't guarantee it; you can make a repeat harmless |
| Azure Functions timers | BackgroundService / Hangfire in the web app | Single execution across instances (documented storage lock); independent of web deploys |
| Runner contracts in `WorkOrders.Contracts` | Logic inside the Function | The module owns its rules; the Function is just another host |
| Service Bus queue to a Function | Call Teams from the API | Failures are retried and dead-lettered, not lost or blocking |
| `MessageId` = outbox id + duplicate detection | Inbox on the forwarder | The broker de-duplicates re-sends for free |
| `FactoryClock` (Asia/Kuala_Lumpur) | UTC date | The business day is Penang's |
| Cron in UTC (`0 0 22 * * *`) | `WEBSITE_TIME_ZONE` | Not supported on Linux Consumption/Flex (Microsoft docs) |
| One catch-up work order | One per missed interval | Duplicate jobs for one machine are noise |
| `EscalatedAt` stored | Escalation computed | It's a historical fact, not a function of now |

---

## 4. Common pitfalls (this milestone had real ones)

1. **The reviewer introduced a date bug.**
   - **The reasoning:** after reading the frontend agent's note that date-only values render correctly, I "fixed" every `| date: 'mediumDate'` to `| date: 'mediumDate' : 'UTC'`, reasoning that `new Date('2026-10-20')` is UTC midnight. That's true for `new Date`.
   - **The facts:** Angular's `formatDate` deliberately parses a bare `YYYY-MM-DD` as a **local** date. My "fix" formatted that local midnight in UTC, which in Penang (UTC+8) shows **Oct 19**.
   - **How it was caught:** by running the suite in `Asia/Kuala_Lumpur`.
   - **Fixed:** I reverted, and CI now runs the frontend tests in **both** `America/Los_Angeles` and `Asia/Kuala_Lumpur`, because date bugs are directional.
   - *Lesson: review fixes need the same verification as the code they fix.*
2. **A missing endpoint found by tests.** Three integration tests called `/api/pm-schedules/{id}/history`, which the implementer tested but never mapped. That's exactly what CI is for.
3. **The first deploy after this migration:** every already-overdue open work order will be escalated once and notified. That's correct, but warn the operators before deploying.
4. **The outbox dispatcher also runs inside the Functions host**, because `AddWorkOrdersModule` registers it. `READPAST` claiming makes that safe, but know it's there.
5. **PM schedules for decommissioned assets** keep generating until someone deactivates them. A clean fix is event-driven: Assets publishes `AssetDecommissioned` → WorkOrders deactivates that asset's schedules. That's in "what I'd do next".
6. **Two config keys for one queue name** (`ServiceBus:SlaBreachQueue` for the API, `SlaBreachQueue` for the Functions binding). Bicep in M9 must set both.

---

## 5. Senior interview questions

<details>
<summary><strong>Q1. How do you make sure a preventive maintenance job doesn't create duplicate work orders?</strong></summary>

**Model answer:** I don't rely on the scheduler firing exactly once. Timers fire twice, and instances race.

The runner is idempotent, and the database enforces it: a filtered unique index on `WorkOrders(PmScheduleId, PmDueOn) WHERE PmScheduleId IS NOT NULL`. The runner pre-checks to avoid the normal case, but if two runners race, the second insert fails with SQL error 2601. We treat that as "already generated", reload the schedule, advance it, and record an audit event.

An integration test runs four PM runners concurrently against real SQL Server and asserts exactly one work order.

Catch-up after downtime generates one work order for the oldest missed date, not one per missed interval.
</details>

<details>
<summary><strong>Q2. Why Azure Functions for the timers rather than a BackgroundService?</strong></summary>

**Model answer:** A BackgroundService runs once per web instance: three instances, three escalation runs. It also stops when App Service recycles or idles, and it competes with request traffic.

Azure Functions timer triggers use a storage lock, so only one instance runs each occurrence across scale-out, which Microsoft documents. They also run independently of web deployments, and on a Consumption or Flex plan two small timers cost very little.

The logic isn't in the Function, though. `ISlaEscalationRunner` and `IPreventiveMaintenanceRunner` are public contracts in WorkOrders.Contracts, implemented inside the module. The Function app is a second host. Locally, a flag runs the same runners in a BackgroundService for single-instance development.

And the runners are idempotent anyway, so correctness doesn't rest on the lock.
</details>

<details>
<summary><strong>Q3. Walk me through what happens when a work order breaches its SLA.</strong></summary>

**Model answer:**
1. Every five minutes the escalation runner queries open work orders where `DueAt < now` and `EscalatedAt IS NULL`, using a filtered index. For each one it calls `Escalate(now)`, which sets `EscalatedAt` and raises `WorkOrderSlaBreached`.
2. In the same SaveChanges, the interceptor writes the audit row and an outbox row for `WorkOrderSlaBreachedIntegrationEvent`.
3. The dispatcher picks it up, and a forwarder sends it to the Service Bus queue `sla-breaches`, with `MessageId` set to the outbox id so duplicate detection drops re-sends.
4. A Service Bus–triggered Function posts a Teams card. If Teams fails, the Function throws, Service Bus retries, and after five attempts the message goes to the dead-letter queue.

Each step is idempotent or de-duplicated, and none blocks a user request.
</details>

<details>
<summary><strong>Q4. Your factory is in Penang. What time zone issues did you have to handle?</strong></summary>

**Model answer:** Three of them.
1. **The business day.** At 07:30 in Penang, UTC still says yesterday, so a `FactoryClock` defines today using `Asia/Kuala_Lumpur`. It's used for PM generation and decommission dates, and tested with `FakeTimeProvider` around midnight. The runtime image includes tzdata, which we checked, so the zone resolves on Linux.
2. **Function schedules.** Cron runs in UTC, and `WEBSITE_TIME_ZONE` isn't supported on Linux Consumption or Flex Consumption plans, so "06:00 Penang" is `0 0 22 * * *` with a comment.
3. **The browser.** Angular's DatePipe parses a date-only string as a local date. Forcing UTC formatting shows the previous day in Penang. I made exactly that mistake in review and caught it by running the tests in Kuala Lumpur time. CI now runs the frontend suite in both a UTC-minus and a UTC-plus zone, because date bugs only appear on one side.
</details>

<details>
<summary><strong>Q5. Why is the SLA state computed but EscalatedAt stored?</strong></summary>

**Model answer:** SLA state is a function of now: OnTrack becomes Breached while nobody touches the record, so storing it would make it stale immediately. It's computed on read from `DueAt` by a pure, tested function.

Escalation is a historical fact: "supervisors were notified at 14:05". It must not change, it drives idempotency (`EscalatedAt IS NULL`), and auditors need it.

The rule of thumb: if the value depends on the current time, compute it; if it records that something happened, store it.
</details>

---

## 6. Break-it lab: the date that moves (offline, from a real review mistake)

1. Run `git checkout -b lab-06`.
2. In `web/src/app/features/pm-schedules/pm-schedules-list.page.html`, change:
   ```html
   {{ s.nextDueOn | date: 'mediumDate' }}
   ```
   to the "obviously safer":
   ```html
   {{ s.nextDueOn | date: 'mediumDate' : 'UTC' }}
   ```
3. Run the spec in each zone (from `web/`):
   ```bash
   TZ=America/Los_Angeles npx ng test --no-watch --include src/app/features/pm-schedules/pm-schedules-list.page.spec.ts
   TZ=Asia/Kuala_Lumpur  npx ng test --no-watch --include src/app/features/pm-schedules/pm-schedules-list.page.spec.ts
   ```
   **Observe** (verified on this code):
   - **LA:** all tests pass.
   - **Kuala Lumpur:** 1 failure, `expected '…' to contain 'Oct 20, 2026'`, with *Received: "…Oct 19, 2026…"*.
4. Explain why:
   - Angular parses `"2026-10-20"` as **local** midnight. In Penang that's 2026-10-19 16:00 UTC. Formatting it in UTC prints Oct 19.
   - In LA, local midnight is 07:00 UTC on the same day, so the bug hides.
5. Now explain why CI runs the suite in both zones, and why a UTC-only CI would never have caught it.
6. Clean up:
   ```bash
   git checkout -- . && git checkout m6-sla-pm && git branch -D lab-06
   ```

---

## 7. Measured numbers from this milestone
- **CI:** **439 .NET tests, 0 skipped**:
  - 38 host;
  - 92 Assets;
  - 43 Inventory;
  - 266 WorkOrders, including the escalation once-only test, four racing PM runners producing exactly one work order, and the schedule endpoints.
- **Vitest:** 178 tests, run in America/Los_Angeles, Asia/Kuala_Lumpur and (locally) UTC.
- **Angular initial bundle:** 551.19 kB raw / 134.74 kB estimated transfer.
- **Not run:** the Functions host (no Core Tools), the Service Bus emulator, and a Teams webhook.
