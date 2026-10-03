# 04 — Work order lifecycle

## Problem
A machine breaks:
1. an operator reports it;
2. a supervisor approves (or rejects) the request and assigns a technician;
3. the technician starts and completes the repair;
4. the supervisor verifies and closes it.

Every step must be **allowed only for the right person**, **recorded for audit**, and **safe when two people act at once**. Each work order also carries a deadline (SLA) that depends on its priority.

## Domain model

```
WorkOrder (aggregate root, schema "workorders")
  Id, Number (WO-000123, from a DB sequence)
  AssetId + AssetTag + AssetName   ← snapshot copied from Assets at creation (no cross-schema join)
  Title, Description, Priority (P1..P4), AssetDown (bool: machine stopped?)
  Status: Submitted → Approved → Assigned → InProgress → Completed → Closed
          Submitted → Rejected (terminal)          Submitted|Approved|Assigned → Cancelled (terminal)
          Assigned → Assigned (reassign)
  ReportedBy, ApprovedBy, AssignedTo (user id + display name snapshots)
  SubmittedAt, ApprovedAt, StartedAt, CompletedAt, ClosedAt, DueAt
  Resolution (text), RejectionReason / CancellationReason
  RowVersion (optimistic concurrency)
```

**SLA policy** (value object `SlaPolicy`): the resolution target by priority is P1 = 4 h, P2 = 8 h, P3 = 24 h, P4 = 72 h.
- `DueAt = SubmittedAt + target`. The clock starts when the problem is *reported*, not when someone gets round to approving it. That's the honest measure of what the line experiences.
- **SLA state** is computed, never stored, because it depends on *now*:

| State | Rule |
|---|---|
| `OnTrack` | Open, and more than 25 % of the target remains |
| `AtRisk` | Open, and 25 % or less of the target remains |
| `Breached` | Open, and past `DueAt` |
| `Met` | Closed out, completed by `DueAt` |
| `Missed` | Closed out, completed after `DueAt` |

- Escalation on breach arrives in M6.
- **For reports (M7):** MTTR uses `CompletedAt − StartedAt`. Downtime uses `CompletedAt − SubmittedAt` when `AssetDown` is true.

## Decision 1 — The state machine lives in the aggregate
- **Each transition is a method:** `Approve`, `Reject`, `Assign`, `Start`, `Complete`, `Close`, `Cancel`.
- **Each method:**
  1. guards the current status, throwing `DomainException` with a message naming the illegal transition;
  2. records who did it and when (the actor and time are passed in, just as `today` was in M2);
  3. raises a **domain event** (`WorkOrderApproved`, …).
- **Why not a state-machine library (Stateless)?** Seven transitions with simple guards read clearly as methods, and a library adds indirection without adding safety.

## Decision 2 — Audit trail (ADR-0007)
"Every state change is audited" covers Assets as well as WorkOrders.

| Option | Pros | Cons |
|---|---|---|
| A. Aggregate appends its own history rows | Explicit | History logic duplicated in every aggregate; the aggregate grows a collection that only grows |
| B. EF interceptor diffs changed columns (generic) | Zero effort per entity | Records *what columns changed*, not *what happened* ("Status: 2→3" vs "Approved by Sam"); noisy |
| C. SQL Server temporal tables | Full row history for free, queryable `FOR SYSTEM_TIME` | No *who* or *why*; schema coupling |
| **D. Domain events → audit rows, written by a SaveChanges interceptor in the same transaction** | Semantic ("WorkOrderApproved by Sam"), one mechanism for all modules, atomic with the change | Events must be designed; the interceptor is shared infrastructure |

**Decision: D.**
- **SharedKernel** gets `IDomainEvent` and an `AggregateRoot` base class with a pending-events list.
- **A new building block, `PlantOps.BuildingBlocks.Infrastructure`** (EF-aware; the domain stays EF-free), provides:
  - `AuditEntry` and its mapping, with **one `AuditEntries` table per module schema** (modules still own their data);
  - `DomainEventInterceptor`. Before `SaveChanges`, it collects pending events from tracked aggregates, writes one `AuditEntry` per event, stamped with the actor (`ICurrentUser`) and time (`TimeProvider`), then clears them. It's all **one transaction**: the change and its audit row commit or roll back together.
- **M5 reuses the same interceptor** to write integration events to an **outbox**. That's the real payoff of choosing D now.
- **Retrofit:** `Asset` raises `AssetRegistered`, `AssetDetailsUpdated`, `AssetRelocated`, `AssetCriticalityChanged` and `AssetDecommissioned`.
- `GET /api/assets/{id}/history` and `GET /api/work-orders/{id}/history` read the module's audit table.

## Decision 3 — Optimistic concurrency (ADR-0008)
- **The problem:** two supervisors open the same work order. One assigns Tom, the other assigns Lee a second later. Without protection, the last write silently wins and Tom never hears he was unassigned.
- **Mechanism:** a SQL Server `rowversion` column, which the database bumps on every update.
  - `GET /api/work-orders/{id}` returns `ETag: "<base64 rowversion>"`.
  - Every command requires `If-Match: "<etag>"`. EF puts that version in the `UPDATE … WHERE Id = @id AND RowVersion = @v` clause.
  - Zero rows affected → `DbUpdateConcurrencyException` → **412 Precondition Failed**.
  - A missing `If-Match` → **428 Precondition Required**, so clients can't opt out by forgetting.
- **Why HTTP preconditions rather than a `version` field in the body?** It's the standard (RFC 9110). It works with caches and proxies, and the command bodies stay pure intent.
- **Assets** keeps last-write-wins for now (its UI is read-only). The study guide notes this as a deliberate gap.

## Decision 4 — Who may do what: role policies plus a resource rule

| Action | Who |
|---|---|
| Raise | any signed-in user (operators, technicians, supervisors, admins all report breakdowns) |
| Approve, reject, assign, close, cancel | `workorders:supervise`, i.e. supervisor or admin |
| Start, complete | the **assigned technician** (or an admin). This is a *resource-based* rule |

- **The resource-based rule:**
  - "Technician" isn't enough; it must be *this work order's* technician. A role policy can't express that, because the decision needs the loaded work order.
  - The endpoint loads the aggregate and calls `IAuthorizationService.AuthorizeAsync(user, workOrder, WorkOrderOperations.Work)`.
  - A `WorkOrderAuthorizationHandler` checks `AssignedTo == current user id || user is admin`. Failure gives 403.
- **`allowedActions`:** the detail response includes `allowedActions: ["approve", "reject", …]`, computed by the server from status + user + resource rule. The SPA shows exactly those buttons and never re-implements the rules.

## Decision 5 — Who is a technician? A user directory (Identity module)
- **The problem:** to *assign* a technician we need a list of them, but users live in the IdP.
- **Options:**
  - call the IdP's admin API (Keycloak admin REST or Microsoft Graph), which is provider-specific;
  - **JIT provisioning** (chosen): on every successful login, upsert `identity.Users (Id = sub, Name, Email, Roles, LastSeenAt)`.
- **Trade-off:** someone who has never logged in can't be assigned. That's acceptable for a factory; a nightly Graph sync can be added later.
- **Exposure:**
  - `Identity.Contracts` exposes `IUserDirectory` and `ICurrentUser`, the latter giving the actor id and name for audit and for snapshots.
  - `GET /api/identity/users?role=technician` uses the supervise policy.
- This adds the **identity schema and its first migration**.

## Decision 6 — Referencing an asset from another module
- WorkOrders needs to know the asset exists, is in service, and what its tag and name are.
- `Assets.Contracts` exposes `IAssetDirectory.FindAsync(Guid id) → AssetSummary?`. Assets implements it internally and registers it in DI.
- WorkOrders **snapshots** the tag and name onto the work order:
  - lists then need no cross-module join (ADR-0002);
  - and a later rename doesn't rewrite history, which is usually what an auditor wants.
- Raising a work order on a decommissioned asset gives 400.

## Decision 7 — Work order numbers
- A SQL Server **sequence** `workorders.WorkOrderNumbers` with `DEFAULT NEXT VALUE FOR …`. The number is gap-tolerant, unique, and assigned by the database at insert, so there's no `MAX()+1` race.
- Shown as `WO-000123`. The `Guid` stays the key.

## API
```
POST /api/work-orders                     { assetId, title, description, priority, assetDown } → 201 + ETag
GET  /api/work-orders?status&priority&assetId&mine&page&pageSize   (mine = assigned to me)
GET  /api/work-orders/{id}                → detail + allowedActions, ETag header
GET  /api/work-orders/{id}/history        → audit entries (newest first)
POST /api/work-orders/{id}/approve        { priority? }          If-Match required
POST /api/work-orders/{id}/reject         { reason }             "
POST /api/work-orders/{id}/assign         { technicianId }       "
POST /api/work-orders/{id}/start                                  "
POST /api/work-orders/{id}/complete       { resolution }         "
POST /api/work-orders/{id}/close                                  "
POST /api/work-orders/{id}/cancel         { reason }             "
GET  /api/identity/users?role=technician
GET  /api/assets/{id}/history
```
Commands return **204 with the new `ETag`**, so the client can issue the next command without re-fetching.

## Frontend
- **`/work-orders` list:** filters (status, priority, "assigned to me") in the URL as in M2, a colour-plus-text SLA badge, and the number, asset, title and assignee.
- **`/work-orders/new` raise form with Signal Forms:**
  - fields: asset picker (search `/api/assets`), priority, title, description, "machine is down";
  - field validation is declared in the form schema (required, max length);
  - server 400s are shown as a form-level error.
- **`/work-orders/:id` detail:**
  - a summary, an SLA countdown (a 1-minute ticking signal), and the history timeline;
  - **action buttons from `allowedActions`**, with small dialogs (Material) for reason, technician and resolution;
  - it sends `If-Match` from the `ETag`, and on 412 shows "Someone else changed this work order. Reloaded." and reloads.
- **Asset detail:** gains a history section.
- **Nav:** "Work orders".

## Out of scope
- Spare parts on work orders (M5).
- SLA breach escalation and preventive maintenance (M6).
- Attachments and photos.
