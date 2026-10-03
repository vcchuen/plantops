# Study guide 05: Inventory, reservations and events between modules

> Goal: you can explain the transactional outbox and idempotent inbox from memory, say why "exactly-once delivery" is a myth while "exactly-once effect" is achievable, and contrast *retrying* a concurrency conflict with *refusing* one.

---

## 1. Concepts from first principles

### 1.1 The dual-write problem
- **The requirement:** when a work order completes, stock must go down (Inventory) and the machine's maintenance history must grow (Assets).
- **The naive version:** *save the work order, then call Inventory, then call Assets.*
- **What can go wrong:** the process can die **between** those steps. Deploys, crashes and App Service restarts happen.
  - If it dies after the first save, stock is never consumed.
  - If you call Inventory *before* saving and the save then fails, you consumed stock for a completion that never happened.
- Writing to two places (a database and "somewhere else") can't be made atomic without a distributed transaction, and we don't want those (slow, fragile, unsupported by most brokers).

### 1.2 The transactional outbox
- **The trick:** don't write to two places. Write the *intent to notify* **into the same database, in the same transaction** as the change: a row in `workorders.OutboxMessages`.
  - The work order update and its outbox row commit together, or neither does.
  - A **dispatcher** (a background loop) later reads unprocessed rows and delivers them.
- **If the dispatcher dies mid-delivery:** the row is still unprocessed, so it's delivered again after restart.
- **So delivery is at-least-once.** A message can arrive twice, but never zero times.

### 1.3 The idempotent inbox
At-least-once means consumers can see duplicates. So each consumer keeps an **inbox**: `InboxMessages(MessageId, Handler)` with a unique key.
1. The handler writes **its effect and its inbox row in one SaveChanges**.
2. A duplicate tries to insert the same inbox row, hits the unique key (SQL error 2601/2627), and the whole change rolls back.
3. The handler treats that as "already done".

**Delivery is at-least-once; the effect is exactly-once.** That sentence is the senior answer.

### 1.4 Domain events vs integration events

| | Domain event | Integration event |
|---|---|---|
| Example | `WorkOrderCompleted` (internal record) | `WorkOrderCompletedIntegrationEvent` (public, in `WorkOrders.Contracts`) |
| Audience | Same module (audit) | Other modules (or, later, other services) |
| Stability | Can change freely | **A contract.** Add fields, never repurpose them |
| Delivery | Same SaveChanges (audit row) | Outbox, then dispatcher, then handlers |

A per-module **mapper** decides which domain events become integration events. The internal model can evolve without breaking consumers.

### 1.5 Claiming outbox rows safely with several app instances
If two App Service instances both run the dispatcher, both could pick the same row. Our claim query:
```sql
SELECT TOP (@n) * FROM workorders.OutboxMessages WITH (UPDLOCK, READPAST, ROWLOCK)
WHERE ProcessedAt IS NULL ORDER BY OccurredAt
```
- **`UPDLOCK`:** lock the rows I read, as if I'm about to update them.
- **`READPAST`:** *skip* rows someone else has locked, instead of waiting.
- **`ROWLOCK`:** lock rows, not pages.

Two dispatchers therefore take *different* batches. The inbox still makes an accidental duplicate harmless; that's belt and braces.

### 1.6 Two concurrency policies, one mechanism
Both M4 and M5 use a `rowversion` to detect that someone else changed the row. What happens next depends on **who made the decision**.

| | M4 work order assign | M5 reserve the last unit |
|---|---|---|
| Who decided? | A **human**, looking at a page that's now stale | The **server** ("is there stock?") |
| On conflict | **Refuse** with 412, the user reloads and decides again | **Retry** automatically: reload, re-run `Reserve`, which now sees `Available = 0`, so 409 "Only 0 available" |

The retry uses a **fresh DbContext per attempt**, so no stale tracked entity leaks into the next try. Picking the policy per use case is the senior move.

### 1.7 Eventual consistency, honestly
- For one polling interval (default 2 s) after "Complete", stock still shows the parts as reserved. That's the price of modules not calling each other synchronously.
- The UI says so: "Stock is updated shortly after completion."
- In an interview, name the trade-off rather than hiding it.

### 1.8 Never `Type.GetType()` a string from your database
- The outbox stores the event *type name*. Resolving it with `Type.GetType(row.Type)` would let anyone who can write that column (via an SQL injection elsewhere, or a compromised admin) make the app instantiate an arbitrary type: a **deserialisation gadget**.
- `IntegrationEventRegistry` is an **allow-list**: only event types registered at startup can be resolved. That's OWASP A08, Software and Data Integrity Failures.

---

## 2. Guided code tour (read in this order)

### The plumbing (BuildingBlocks.Infrastructure)
1. **`SharedKernel/IIntegrationEvent.cs`**
   - A marker. It lives here so the Contracts projects can implement it without referencing EF.
2. **`IIntegrationEventMapper.cs` + the changed `DomainEventInterceptor.cs`**
   - The interceptor now writes **audit rows and outbox rows in the same SaveChanges**.
   - *Notice* `ContextType` on the mapper: one interceptor serves every module's context, and this is how it picks the right mapper without a service locator.
3. **`OutboxMessage.cs`, `InboxMessage.cs`**
   - *Notice* in the migrations:
     - the **filtered index** `WHERE ProcessedAt IS NULL` (pending rows only, so it stays tiny);
     - the inbox **composite primary key** (MessageId, Handler).
4. **`IntegrationEventRegistry.cs`**: the allow-list (§1.8).
5. **`OutboxProcessor.cs`**. Read slowly. *Notice:*
   - the claim SQL (§1.5);
   - `CreateExecutionStrategy().ExecuteAsync(...)`: with `EnableRetryOnFailure`, a *user-initiated transaction* must run inside the execution strategy, so the whole claim, deliver and commit block is the unit that gets retried. That's safe only because handlers are idempotent;
   - failure handling: `Attempts++`, a truncated `LastError`, and parking after 5 attempts;
   - every handler for an event runs even if one fails, and the failures are aggregated.
6. **`OutboxDispatcher.cs`**
   - A `BackgroundService` polling at `Outbox:PollInterval`. It waits one interval before its first pass, so short-lived test hosts never touch the database.
7. **`InboxGuard.cs`**
   - Effect plus inbox row in one save. *Notice* the filter: `DbUpdateConcurrencyException` *is a* `DbUpdateException`, so only genuine unique violations (2601/2627) count as "already handled".
8. **`ConcurrencyRetry.cs`**: the retry policy from §1.6.

### Producer: WorkOrders
9. **`WorkOrders.Contracts/WorkOrderCompletedIntegrationEvent.cs`, `…Cancelled…`**: public, versioned contracts.
10. **`WorkOrders/WorkOrderIntegrationEventMapper.cs`**: domain event to integration event.
11. **`WorkOrders.Contracts/IWorkOrderDirectory.cs`**: the synchronous read Inventory uses to validate reservations.

### Consumer: Inventory
12. **`Inventory/Domain/SparePart.cs`**. *Notice:*
    - `QuantityAvailable = OnHand − Reserved`;
    - `Reserve` checks it (the lab breaks this);
    - reserving again for the same work order increases the existing reservation;
    - `ConsumeFor` and `ReleaseFor` are driven by events.
13. **`Inventory/Infrastructure/*Configuration.cs`**. *Notice* **three layers of defence** for the stock invariant:
    - the domain check;
    - the `rowversion` (so a concurrent change is detected);
    - the database **CHECK constraint** `CK_SpareParts_Quantities` (0 ≤ Reserved ≤ OnHand).

    Also the **filtered unique index**: one *Active* reservation per (part, work order).
14. **`Inventory/Endpoints/*`**. *Notice:*
    - the reserve flow: authorisation (supervisor, or the WO's assigned technician via `IWorkOrderDirectory`), work order status, then `ConcurrencyRetry`;
    - `InsufficientStockException` → 409 with "Only 2 pcs of FDR-8MM-001 available".
15. **`Inventory/Handlers/WorkOrderCompletedHandler.cs`, `WorkOrderCancelledHandler.cs`**: short, because `InboxGuard` does the hard part.

### Consumer: Assets
16. **`Assets/Domain/MaintenanceRecord.cs` + its handler**
    - *Notice* `WorkOrderId` is **unique**, so even without the inbox a duplicate couldn't create two records. That's defence in depth again.

### Tests
17. **`tests/PlantOps.Modules.Inventory.Tests/Integration/InventoryFixture.cs`**
    - Boots the whole app with `Outbox:PollInterval = 1 h`, so events are delivered **only** when a test calls `DrainWorkOrdersOutboxAsync()`. "Before delivery" and "after delivery" assertions are therefore deterministic. No `Task.Delay` and no flaky timing.
18. **`InventoryApiTests.cs`**. Read these three:
    - the last-unit race (`Task.WhenAll` of two reserves: exactly one 201 and one 409);
    - the end-to-end flow (raise … complete → drain → stock consumed and maintenance record present);
    - the redelivery test (the same message processed twice → effect applied once).

### Frontend
19. **`features/inventory/*`**
    - The parts list (low stock as icon plus text), part detail with a Signal Forms "receive stock" form, and `part-new.dialog.ts`.
20. **`features/inventory/reserve-part.dialog.ts`**: a 409 keeps the dialog open and shows the server's message in `role="alert"`.
21. **`work-order-detail.page.*` → Parts section**
    - *Notice* the comment: the button rule only *hides* the button, and the server enforces.

---

## 3. Why this, not that

| Choice | Instead of | Why |
|---|---|---|
| Transactional outbox | Call other modules after save / in-memory bus / publish to a broker after commit | Only option without a crash window or dual write |
| Inbox with unique key | "We'll make sure it's only delivered once" | Exactly-once *delivery* is impossible across crashes; exactly-once *effect* is achievable |
| Domain → integration event mapper | Publish domain events directly | Internal model can change; contracts stay stable |
| `UPDLOCK, READPAST` claim | Plain `SELECT`, or a single-instance assumption | Safe with several App Service instances; no blocking |
| Allow-listed type registry | `Type.GetType(row.Type)` | No deserialisation gadgets (OWASP A08) |
| Automatic retry on reserve conflict | 412 like work orders | The server makes the decision, so it can re-decide on fresh data |
| Domain check + rowversion + CHECK constraint | Domain check only | Defence in depth: a bug in one layer is caught by the next |
| Fixture with a 1 h poll interval + explicit drain | `Task.Delay` and hope | Deterministic tests |
| Separate per-module fixtures kept | One big refactor | Couldn't run the old integration tests locally to prove a refactor safe; added a whole-app fixture only where needed |

---

## 4. Common pitfalls and how this code avoids them

1. **Client-assigned Guid keys on child entities.**
   - **The symptom:** add a child with its Id already set through a tracked parent's collection, and EF may treat it as an *existing* row (UPDATE instead of INSERT) when the key is configured as generated-on-add.
   - **The fix:** `ValueGeneratedNever()` on `Reservation.Id`.
2. **User transactions under `EnableRetryOnFailure`.** Must run inside `CreateExecutionStrategy().ExecuteAsync`, otherwise EF throws. Then the whole block can re-run, so it must be idempotent.
3. **Swallowing concurrency errors as duplicates.** `DbUpdateConcurrencyException` derives from `DbUpdateException`. Filter on the SQL error number, not the exception type.
4. **Derived properties in LINQ.** `QuantityAvailable` is a C# property, so EF can't translate it. Queries spell out `OnHand - Reserved`.
5. **Timing-based tests.** "Wait 3 seconds for the dispatcher" is flaky on slow CI agents. Drain explicitly.
6. **Forgetting the user's view of eventual consistency.** Say it in the UI.
7. **`Type.GetType` from data.** Never; use an allow-list.

---

## 5. Senior interview questions

<details>
<summary><strong>Q1. What is the transactional outbox pattern and what problem does it solve?</strong></summary>

**Model answer:** It solves the dual-write problem. You can't atomically commit a database change *and* publish a message to another system, so a crash between the two either loses the message or publishes one for a change that rolled back.

The outbox makes it a single write. The event goes into an `OutboxMessages` table in the same database transaction as the business change. A background dispatcher later reads unprocessed rows, delivers them, and marks them processed.

In PlantOps, our EF SaveChanges interceptor writes the audit row and the outbox row together with the work order update. The dispatcher claims rows with `UPDLOCK, READPAST`, so several instances don't collide, delivers them to in-process handlers, and retries failures up to 5 times before parking them.

Because a crash can happen after delivery but before marking the row processed, delivery is at-least-once. Consumers must therefore be idempotent.
</details>

<details>
<summary><strong>Q2. How do your consumers handle duplicate messages?</strong></summary>

**Model answer:** With an inbox. Each consuming module has an `InboxMessages` table keyed by (MessageId, Handler). The handler writes its effect, for example consuming reserved stock, and the inbox row in one SaveChanges.

If the message is redelivered, the inbox insert hits the unique key, the whole transaction rolls back, and the handler treats it as already processed. Delivery is at-least-once, but the effect is exactly-once.

I filter on the SQL unique-violation error numbers, not on `DbUpdateException` in general, because a concurrency exception is also a `DbUpdateException` and must not be swallowed. Assets adds a unique index on `WorkOrderId` in `MaintenanceRecords` as a second guard.

An integration test processes the same message twice and asserts the stock moved once.
</details>

<details>
<summary><strong>Q3. Two technicians reserve the last unit of a part at the same time. Walk me through it.</strong></summary>

**Model answer:** Both requests load the part with `Available = 1`, and both domain checks pass. Both try to save. The `rowversion` lets only one UPDATE match, so the other gets `DbUpdateConcurrencyException`.

Unlike work order commands, where a stale human decision is refused with 412, here the decision is the server's. So we retry the command with a fresh DbContext: reload the part, run `Reserve` again, and this time it sees `Available = 0` and throws `InsufficientStockException`, which maps to 409 "Only 0 pcs available".

The client sees exactly one 201 and one 409, and a test asserts that against real SQL Server.

Even if the domain check had a bug, the database CHECK constraint `0 ≤ Reserved ≤ OnHand` would reject the second write. That's three layers: domain check, optimistic concurrency, and a database constraint.
</details>

<details>
<summary><strong>Q4. Isn't eventual consistency a problem for users?</strong></summary>

**Model answer:** It's a trade-off we chose and surfaced. After a technician completes a work order, Inventory and Assets update within one dispatcher polling interval, 2 seconds by default.

The alternative is to synchronously call Inventory and Assets inside the WorkOrders request. That couples the modules' availability and transaction boundaries, and it doesn't even solve the crash window.

We tell the user "Stock is updated shortly after completion". Nothing in the business process needs stock to be accurate within milliseconds of completion.

If it did, for example a hard rule that you can't complete without consuming stock in the same transaction, then those two concepts probably belong in the same module or aggregate. A requirement for synchronous consistency across modules is usually a sign the boundary is in the wrong place.
</details>

<details>
<summary><strong>Q5. How would this change if Inventory became a separate microservice?</strong></summary>

**Model answer:** The producer side doesn't change: WorkOrders still writes outbox rows in its transaction. The dispatcher's *destination* changes. Instead of invoking in-process handlers, it publishes to Azure Service Bus, which is what M6 does for SLA notifications.

The new Inventory service subscribes, and its handler code, including the inbox, moves unchanged. The integration event records are already public contracts in `WorkOrders.Contracts`, so they become the message schema.

The synchronous `IWorkOrderDirectory` read becomes an HTTP call or, better, a local projection Inventory maintains from WorkOrder events. And Inventory's schema moves to its own database, which is easy because there are no cross-schema foreign keys.

That mechanical path is the whole point of ADR-0001.
</details>

---

## 6. Break-it lab: remove the stock check (offline)

1. Run `git checkout -b lab-05`.
2. In `src/Modules/Inventory/PlantOps.Modules.Inventory/Domain/SparePart.cs`, inside `Reserve`, change:
   ```csharp
   if (quantity > QuantityAvailable)
   ```
   to:
   ```csharp
   if (false && quantity > QuantityAvailable)
   ```
3. Run `dotnet test tests/PlantOps.Modules.Inventory.Tests`.

   **Observe** (verified on this code): 2 failures:
   - `Reserving_more_than_available_throws_a_conflict_with_the_designed_message`;
   - `The_increase_is_checked_against_available_stock_too`.

   Notice that the compiler gave **no warning**: `false && …` is legal code that silently disables a business rule.
4. Think it through: what would happen *in production* with the check gone?
   - A reservation that over-reserves would try to save `Reserved > OnHand`.
   - The database's `CK_SpareParts_Quantities` CHECK constraint would reject it, with SQL error 547 → `DbUpdateException` → a 500.
   - **The data stays correct, but the user gets an ugly error instead of a clear 409.**
   - That's what defence in depth buys you: the data stays safe when the friendly layer fails.
5. Clean up:
   ```bash
   git checkout -- . && git checkout m5-inventory && git branch -D lab-05
   ```

---

## 7. Measured numbers from this milestone
- **CI:** **357 .NET tests, 0 skipped**:
  - 38 host;
  - 92 Assets;
  - 184 WorkOrders;
  - 43 Inventory, including 17 integration tests: last-unit race, end-to-end event flow, redelivery idempotency and the cancel flow.
- **Vitest:** 131 tests.
- **Angular production build:** initial bundle 550.37 kB raw / 132.65 kB estimated transfer. New lazy chunks: parts list 12.24 kB, part detail 7.40 kB.
- **Not measured:** outbox throughput and latency under load. A plausible M7 benchmark, but no number until it's measured.
