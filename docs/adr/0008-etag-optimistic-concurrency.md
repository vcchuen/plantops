# ADR-0008: Optimistic concurrency with rowversion + ETag/If-Match

- **Status:** Accepted
- **Date:** 2026-10-03

## Context
Work orders are edited by several people (supervisors, technicians) at overlapping times. Lost updates, where the last write silently wins, would unassign technicians or reopen closed work without anyone noticing.

## Options
1. **Pessimistic locking:** `SELECT … WITH (UPDLOCK)` held while a user decides. That doesn't fit HTTP, where users think for minutes between requests.
2. **Optimistic concurrency with a version in the request body.** It works, but it mixes concurrency metadata into every command contract.
3. **Optimistic concurrency with HTTP preconditions (RFC 9110):**
   - `GET` returns `ETag` (the base64 SQL `rowversion`);
   - commands send `If-Match`;
   - a mismatch returns `412 Precondition Failed`, and a missing header returns `428 Precondition Required`.

## Decision
Option 3 for WorkOrders.
- EF maps `RowVersion` with `IsRowVersion()`.
- The endpoint sets the property's *original value* from `If-Match`, so EF's `UPDATE … WHERE RowVersion = @original` detects the conflict.
- Successful commands return the new `ETag`.

## Consequences
- **Clients must send `If-Match`:** a client must have read the resource before changing it. The SPA keeps the ETag from `httpResource` headers.
- **Conflicts are visible:** they surface as 412, and the UI reloads and tells the user.
- **Assets is not covered yet:** it still uses last-write-wins (read-only UI). Apply the same pattern before adding asset edit screens.
