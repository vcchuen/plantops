# Study guide 04: Work order lifecycle

> Goal: you can explain the work order state machine, how every change is audited atomically, how two simultaneous edits are detected, and how "only the assigned technician" is enforced. You should also be able to show a Signal Forms form and explain it.

---

## 1. Concepts from first principles

### 1.1 A state machine inside an aggregate
- A work order moves through statuses:
  `Submitted → Approved → Assigned → InProgress → Completed → Closed`
  - side exits: `Rejected` (from Submitted) and `Cancelled` (before work starts).
- **Each arrow is a method** (`Approve`, `Assign`, …). Each method:
  1. checks the current status, throwing `DomainException("Cannot start a work order that is Submitted")` if the move is illegal;
  2. records who and when (the actor and `now` are *passed in*, so the aggregate never reads a clock or HttpContext);
  3. raises a **domain event** describing what happened.
- There's no state-machine library. Seven transitions with simple guards read best as plain methods.

### 1.2 Domain events
- A **domain event** is an immutable record of a business fact in the past tense: `WorkOrderApproved(WorkOrderId, Priority, DueAt)`.
- The aggregate *raises* events (adds them to a pending list in `AggregateRoot`). It doesn't know who listens.
- This milestone has one listener, the audit trail. M5 adds a second, integration events between modules.

### 1.3 Atomic audit with a SaveChanges interceptor (ADR-0007)
EF Core lets you hook into `SaveChanges`. Our `DomainEventInterceptor`:
1. runs just before the SQL is sent;
2. finds tracked aggregates with pending events;
3. turns each event into an `AuditEntry` row (event type, JSON payload, actor, time) and adds it to the *same* DbContext;
4. clears the events.

Because the audit rows are inserted by the same `SaveChanges`, they're in **the same database transaction** as the change. Either both commit or neither does. There's never a "the change saved but the audit write failed" gap.

### 1.4 Optimistic concurrency: rowversion, ETag, If-Match (ADR-0008)
- **The lost update:** Sam and Lee both open WO-000042. Sam assigns Tom. Lee, still looking at the old page, assigns Lee. Without protection, Lee's write silently overwrites Sam's.
- **SQL Server `rowversion`:** a column the database changes automatically on every update.
- **How we use it:**
  1. **GET** returns it as an HTTP **ETag** header: `ETag: "AAAAAAAAB9E="`.
  2. **Every command** must send it back as **`If-Match: "AAAAAAAAB9E="`**.
  3. EF issues `UPDATE … WHERE Id = @id AND RowVersion = @clientVersion`. If someone else changed the row first, **0 rows** match, EF throws `DbUpdateConcurrencyException`, and we return **412 Precondition Failed**.
  4. A missing `If-Match` returns **428 Precondition Required**, so a client can't "forget" its way past the check.
- **Optimistic** means we don't lock while the user is thinking; we *detect* the conflict at write time. Pessimistic locks don't fit HTTP, because users think for minutes between requests.

### 1.5 Role-based vs resource-based authorisation
- **Role-based:** "supervisors may approve". The decision needs only the user.
- **Resource-based:** "only *this work order's* technician may start it". The decision needs the user **and the loaded resource**.
- In ASP.NET Core:
  - define an `OperationAuthorizationRequirement` (`WorkOrderOperations.Work`) and an `AuthorizationHandler<TRequirement, WorkOrder>`;
  - call `IAuthorizationService.AuthorizeAsync(user, workOrder, requirement)` *after* loading the work order.
- **`allowedActions`:** the server computes which buttons this user may press on this work order and sends them, so the SPA doesn't duplicate the rules.

### 1.6 SLA as a computed value
- `DueAt = SubmittedAt + target(priority)`, where P1 = 4 h … P4 = 72 h.
- **The SLA *state* (OnTrack / AtRisk / Breached / Met / Missed) is never stored**, because it depends on *now*. A stored "Breached" flag would be wrong a minute later, or need a job to keep it fresh.
- It's computed in C# after the database query. SQL could compute it too, but then the boundary rules (exactly 25 % remaining, exactly at `DueAt`) would live in two languages.

### 1.7 Signal Forms (Angular 22)
- **The model is a signal:** `model = signal({ assetId: '', title: '', priority: 'P3', … })`.
- **`form(model, path => { required(path.title); maxLength(path.title, 200); … })`** builds a *field tree*. Each field exposes signals: `value`, `touched()`, `invalid()`, `errors()`.
- **In the template:** `<input matInput [formField]="raiseForm.title">` binds both ways.
- **`submit(form, async () => …)`** marks all fields touched, runs the action only if valid, and tracks `submitting()`.
- **Compared with Reactive Forms:** no `FormGroup`/`FormControl` classes and no `valueChanges` observables. Validation is declared once, in the schema, and everything is a signal, so it composes with the rest of a zoneless app.

---

## 2. Guided code tour (read in this order)

### Shared building blocks
1. **`src/BuildingBlocks/PlantOps.SharedKernel/AggregateRoot.cs`, `IDomainEvent.cs`, `Actor.cs`**
   - Still EF-free. *Notice:* an architecture test now *enforces* that SharedKernel has no EF or ASP.NET references.
2. **`src/BuildingBlocks/PlantOps.BuildingBlocks.Infrastructure/`**
   - `ICurrentUser`, `AuditEntry`, `DomainEventInterceptor` (read every comment) and `AddMigrateOnStartup<T>()`.
   - *Notice why `ICurrentUser` lives here and not in Identity.Contracts:* a building block must never depend on a module.

### WorkOrders domain
3. **`Domain/WorkOrder.cs`**. *Notice:*
   - The guard at the top of every transition.
   - Snapshots: asset tag and name, and person names, are copied in rather than joined later.
   - `Reassign` is allowed from `Assigned`.
   - Every transition raises an event.
4. **`Domain/SlaPolicy.cs`**
   - Targets per priority, and `StateAt(now)`. The boundary cases are unit-tested.
5. **`Domain/WorkOrderEvents.cs`**
   - The payloads are the audit contract: ids plus the new values, not the whole entity.

### Authorisation
6. **`Authorization/WorkOrderAccess.cs`**
   - `CanWork` (the lab breaks it) and `AllowedActions(status, canSupervise, canWork)`: one pure function and one source of truth.
7. **`Authorization/WorkOrderAuthorizationHandler.cs`**
   - It reads `sub` from the principal and the assignee from the resource.

### Persistence
8. **`Infrastructure/WorkOrderConfiguration.cs`**. *Notice:*
   - `HasSequence` is on the **ModelBuilder**, plus `HasDefaultValueSql("NEXT VALUE FOR …")` and `ValueGeneratedOnAdd`. The number comes from the database, so there's no `MAX()+1` race.
   - `RowVersion.IsRowVersion()`.
   - The list-filter indexes are declared in the model.
9. **`Infrastructure/Migrations/*InitialWorkOrders.cs`**
   - Find `CREATE SEQUENCE`, the `rowversion` column and `AuditEntries`. The migration class was changed to `internal`, as for every migration.

### HTTP
10. **`Endpoints/IfMatch.cs`**
    - It parses exactly one strong, quoted base64 ETag, and rejects `*`, weak (`W/`) and list values. It's unit-tested.
11. **`Endpoints/WorkOrderEndpoints.cs` → `Execute(...)`**, the heart of every command. *Notice the order:*
    1. read If-Match (428/400);
    2. load (404);
    3. resource rule (403);
    4. **compare the ETag before calling the domain method** (412);
    5. run the domain method;
    6. set the RowVersion *original value*;
    7. `SaveChanges` (412 on a race);
    8. return the new ETag.

    Step 4 matters: a stale client re-approving an already-approved order should hear "reload", not a confusing "cannot approve an Approved work order".
12. **`Describe(...)`**
    - `allowedActions` per user: it evaluates the supervise policy and the resource rule for *this* principal.

### Identity additions
13. **`src/Modules/Identity/.../UserProvisioner.cs` and `OidcOptionsSetup.cs` (`OnTicketReceived`)**
    - JIT provisioning. *Notice the comment on event order:* userinfo claims (roles, email) are added **after** `OnTokenValidated`, so the upsert must run in `OnTicketReceived`.
14. **`UserDirectory.cs`**
    - Roles are stored as `,technician,supervisor,` (a delimited column). Read the agent's trade-off note in the PR.

### Host
15. **`src/Host/PlantOps.Api/Program.cs`**, the last lines. *Notice:*
    - The SPA fallback pattern `{*path:nonfile:regex(^(?!api(/|$)))}`. This replaced an `/api/{**rest}` catch-all **in this milestone** (pitfall 1).

### Frontend
16. **`features/work-orders/work-order-new.page.ts`**: Signal Forms.
    - The schema with `required` and `maxLength`.
    - `[formField]` bindings in the template.
    - `submit(form, …)`, and a form-level `role="alert"` for server 400s.
    - The asset picker uses a custom `ErrorStateMatcher`, because it edits search text while the model holds only the chosen id.
17. **`work-order-commands.ts`**
    - `execute(id, action, etag, body)` sends `If-Match` **exactly as received, quotes included**, and returns the new ETag.
18. **`work-order-detail.page.ts`**. *Notice:*
    - `detail.headers()?.get('ETag')`: `httpResource` exposes response headers as a signal.
    - The action bar renders **only `allowedActions`**.
    - A 412 shows a snackbar and reloads.
    - The SLA countdown uses one 60-second `now` signal.
19. **`sla.ts`**
    - The pure `slaCountdown(dueAt, now)`, tested at its boundaries.
20. **`features/shared/history-timeline.ts`**
    - Reused by asset detail and work-order detail.

---

## 3. Why this, not that

| Choice | Instead of | Why |
|---|---|---|
| Transitions as aggregate methods | Stateless library / status setter | Guards and events in one readable place; no illegal state reachable from outside |
| Audit from domain events + interceptor | Temporal tables / column-diff interceptor / history collection | Semantic ("approved by Sam"), atomic, one mechanism reused by M5's outbox |
| `ICurrentUser` in a building block | In Identity.Contracts | Building blocks must not depend on modules (architecture test) |
| rowversion + ETag/If-Match, 412/428 | Version in body / last write wins / locks | The HTTP standard; command bodies stay intent-only; HTTP can't hold locks |
| ETag compared before the domain method | Only rely on EF's WHERE clause | Stale clients get "reload" (412), not a misleading business-rule 400 |
| Resource-based handler | Checking `AssignedToId` inline in endpoints | One rule, unit-tested, reused for both enforcement and `allowedActions` |
| Server-computed `allowedActions` | UI re-implements the rules | Rules live in one place; the UI can't drift |
| DB sequence for numbers | `MAX(Number)+1` | Atomic; no duplicate numbers under concurrency |
| Asset/user snapshots on the work order | Join across modules | No cross-schema joins (ADR-0002); history shows names as they were |
| JIT user directory | Keycloak admin API / Graph | Provider-neutral; trade-off: never-logged-in users can't be assigned |
| SLA state computed on read | Stored flag | Always correct; no background job needed |
| Signal Forms | Reactive Forms | Signal-native, schema validation, no observables |

---

## 4. Common pitfalls and how this code avoids them

1. **A catch-all route that masks real errors.** Caught by CI this milestone.
   - **What happened:** 13 integration tests failed with 404. The test helper POSTed commands with **no body**. Endpoints that bind a JSON body only match JSON requests, so routing skipped the real endpoint, and our `/api/{**rest}` catch-all answered "404 Not Found".
   - **Two fixes:**
     - the test now always sends JSON;
     - the catch-all was replaced by excluding `/api` from the SPA fallback route. Now a body-less POST reaches the endpoint and gets a meaningful 400.
   - **Bonus discovery:** with no endpoint matched, the **fallback authorisation policy still runs**. An anonymous caller gets 401 for *any* unknown `/api` path, so route existence can't be probed anonymously. Signed-in users get the proper 404.
2. **`OnTokenValidated` is too early for roles.** Userinfo claims arrive later. Per-login work belongs in `OnTicketReceived`.
3. **Stale ETag vs business rule order.** If you call the domain method first, a stale "approve" says "cannot approve an Approved work order" instead of "someone changed this; reload".
4. **Echo the ETag exactly.** The quotes are part of the value. The SPA passes the header through untouched.
5. **A failed save has already cleared the domain events.** Fine for a request-scoped DbContext (the request ends), but never reuse that context after a failed `SaveChanges`.
6. **xUnit v3 requires public test classes**, so theories over `internal` enums take strings and parse them.
7. **A new `httpResource` on a page breaks that page's old specs.** `http.verify()` fails until you flush the extra request.
8. **API contract drift between parallel teams.** The backend returned `assignedTo: {id,name}`, but the frontend was built against `assigneeName`. This was caught in review and fixed before merge. In a real team an OpenAPI-generated client prevents it. That's an item for "what I'd do next".

---

## 5. Senior interview questions

<details>
<summary><strong>Q1. How do you guarantee that every state change is audited, and that the audit can't be lost?</strong></summary>

**Model answer:** Aggregates raise domain events for every transition, for example `WorkOrderAssigned(WorkOrderId, TechnicianId, TechnicianName)`.

A shared EF Core SaveChanges interceptor runs just before the SQL is sent. It collects pending events from tracked aggregates, converts each into an AuditEntry row stamped with the current user and time, adds those rows to the same DbContext, and clears the events.

Because it's a single SaveChanges, the change and its audit rows are in one transaction: both commit or neither does. The entries are semantic ("approved by Sam") rather than column diffs, and each module keeps its own audit table in its own schema.

I rejected temporal tables because they can't tell you who or why. The same interceptor is where the outbox for integration events goes, so audit and messaging share one mechanism.
</details>

<details>
<summary><strong>Q2. Two supervisors assign different technicians to the same work order at the same time. What happens?</strong></summary>

**Model answer:** Both loaded the work order with the same ETag, the base64 of its SQL rowversion, and both send it back as If-Match.

The first command passes, and EF's `UPDATE … WHERE Id = @id AND RowVersion = @original` matches one row. The database bumps the rowversion, and the response carries the new ETag.

The second command's ETag no longer matches. The endpoint compares it before running the domain method and returns 412. Even if both slipped past that check at the same instant, the SQL `WHERE` clause matches zero rows, EF throws `DbUpdateConcurrencyException`, and we still return 412.

The SPA shows "someone else changed this work order" and reloads. A missing If-Match returns 428, so clients can't opt out. There's an integration test that fires two commands with the same ETag and asserts exactly one 204 and one 412 against real SQL Server.
</details>

<details>
<summary><strong>Q3. How do you enforce "only the assigned technician can start the job"?</strong></summary>

**Model answer:** A role policy isn't enough, because the rule needs the resource.

I use resource-based authorisation: an `OperationAuthorizationRequirement` called Work, and an `AuthorizationHandler<Work, WorkOrder>` that succeeds when the user's `sub` equals the work order's `AssignedToId`, or when the user is an admin. The endpoint loads the work order and calls `IAuthorizationService.AuthorizeAsync(user, workOrder, Work)` before running the command, returning 403 on failure.

The same rule feeds `allowedActions` in the detail response, so the UI shows "Start" only to the right person, from the same source of truth.

The rule guards against a subtle bug: if both ids are null (an unassigned order and a principal without `sub`), a naive `==` would grant access. The lab shows the tests catch that.
</details>

<details>
<summary><strong>Q4. Why is the SLA state not a column in the database?</strong></summary>

**Model answer:** It's a function of time. `OnTrack`, `AtRisk` and `Breached` change while nobody touches the record.

A stored flag would be stale the moment it was written, or would need a job to refresh it, and that job then becomes something that can fail.

We store the facts (`SubmittedAt`, `Priority`, `DueAt`, `CompletedAt`) and compute the state on read with a pure, unit-tested function. That includes the boundaries: exactly 25 % remaining, and exactly at `DueAt`.

Escalation in M6 needs to *act* on breaches, so it will query `DueAt < now AND status is open` directly. That query is index-friendly and doesn't depend on a stored flag either.
</details>

<details>
<summary><strong>Q5. Walk me through your Signal Forms form. What does it give you over Reactive Forms?</strong></summary>

**Model answer:** The model is a plain signal holding the form value. `form(model, schema)` builds a field tree from it, where the schema declares `required(path.title)` and `maxLength(path.title, 200)`. Each field exposes `value`, `touched`, `invalid` and `errors` as signals, and the template binds inputs with `[formField]`.

On submit, `submit(form, action)` marks everything touched, runs the async action only if the form is valid, and exposes `submitting()` so the button disables itself. A server 400 is shown as a form-level alert.

Compared with Reactive Forms, there are no `FormControl` classes or `valueChanges` subscriptions, and validation lives in one declarative schema. Because everything is signals, it fits a zoneless OnPush app without bridging.

Signal Forms became stable in Angular 22 (`@publicApi 22.0`), which we checked in the type definitions rather than assuming.
</details>

---

## 6. Break-it lab: the null == null authorisation hole (offline)

1. Run `git checkout -b lab-04`.
2. In `src/Modules/WorkOrders/PlantOps.Modules.WorkOrders/Authorization/WorkOrderAccess.cs`, change:
   ```csharp
   isAdmin || (!string.IsNullOrEmpty(userId) && userId == assignedToId);
   ```
   to the "obviously equivalent":
   ```csharp
   isAdmin || userId == assignedToId;
   ```
3. Run `dotnet test tests/PlantOps.Modules.WorkOrders.Tests`.

   **Observe** (verified on this code): 2 failures, both in `Can_work_is_the_assignee_or_an_admin`, for `(userId: null, assignedToId: null)` and `(userId: "", assignedToId: "")`.
4. Explain the attack:
   - A work order that isn't assigned yet has `AssignedToId = null`.
   - A principal without a `sub` claim has `userId = null`, for example from a misconfigured IdP or a future service-to-service token.
   - `null == null` is `true`, so that caller could start or complete work it was never given.
   - Also: `allowedActions` would show "Start" to them.
5. Clean up:
   ```bash
   git checkout -- . && git checkout m4-workorders && git branch -D lab-04
   ```

**Take-away:** authorisation code must *fail closed*. Equality on identifiers needs an explicit "both present" check, and the tests need the null and empty cases, not just "Tom vs Lee".

---

## 7. Measured numbers from this milestone
- **CI:** 297 .NET tests, **0 skipped**:
  - 38 host;
  - 89 Assets (28 integration);
  - 170 WorkOrders (25 integration, including the full lifecycle, the concurrency race and the authorisation matrix against SQL Server 2022).
- **Vitest:** 93 tests.
- **Angular production build:** initial bundle 549.76 kB raw / 131.36 kB estimated transfer (budget 560 kB). The new lazy chunks are work-order-detail 57.19 kB, work-order-new 25.83 kB and list 8.07 kB.
- **Not run:** a real login, or compose.
