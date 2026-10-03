# 07 — Reporting (CQRS read side) and measured EF performance

## Problem
Plant managers need three reports, filterable by date range, on screen and **exported to Excel**:
- **MTTR** (mean time to repair) by line, asset and month;
- **SLA compliance** by priority and month;
- **downtime** by line and month.

The data spans modules (work orders, assets and their production lines), and the queries aggregate over months of history. The transactional tables were designed for *changing one work order safely*, not for *summing a year of them*.

The spec also demands EF Core performance work **measured, not asserted**:
- indexes chosen from real query plans;
- N+1 avoided and *shown*;
- compiled queries only where measurable.

## Decision 1 — CQRS, for reporting only (ADR-0011)
- **CQRS** means separate models for writes (commands) and reads (queries). We use it **only here**.
  - Asset and work order screens read their own tables through projections (M2/M4). A second model there would double the code for no gain.
  - Reporting is different: it **crosses modules** (a work order's line comes from Assets), **aggregates over time**, and wants a **denormalised shape** that the write model must not be bent into.
- **The read model:** a new **Reporting module** (schema `reporting`) owns `reporting.WorkOrderFacts`, **one row per completed work order**.
  - **Columns:** WorkOrderId (PK), Number, AssetId, AssetTag, LineId, LineName, Priority, Source, SubmittedAt, StartedAt, CompletedAt, DueAt, MetSla, RepairMinutes, DowntimeMinutes, and CompletedMonth (the first day of the month, in factory time).
  - **How it's filled:** by an **integration event handler** on `WorkOrderCompletedIntegrationEvent`, through the M5 outbox with an inbox for idempotency.
  - **What changes upstream:**
    - the event gains `Priority`, `Source` and `DueAt`. Adding fields is a backward-compatible contract change;
    - the handler resolves the line through `IAssetDirectory` **at completion time**, which is the *historically correct* line. If the machine moves next month, last month's downtime still belongs to its old line.
- **Rebuild:** `POST /api/reports/rebuild` (admin) re-projects from the WorkOrders module's completed orders via a contract query (`IWorkOrderDirectory.CompletedSince`). That's needed once for data from before this milestone, and it's an honest part of any read-model design: projections must be rebuildable.
- **Consistency:** reports lag completion by one outbox poll (~2 s). Nobody runs an MTTR report expecting millisecond freshness.

## Decision 2 — Report queries
- **What the queries look like:** plain LINQ `GroupBy` over `WorkOrderFacts`, filtered on `CompletedAt` within `[from, to)` and projected to small DTOs. They're computed in SQL (`AVG`, `SUM`, `COUNT`), never in memory.
- **Report definitions:**
  - **MTTR** = average `RepairMinutes` (`CompletedAt − StartedAt`).
  - **SLA compliance** = `SUM(MetSla) / COUNT(*)` per group.
  - **Downtime** = `SUM(DowntimeMinutes)` where the asset was down.
- **Ranges:** `from`/`to` are factory-local dates converted to UTC instants by `FactoryClock`. The range is capped at 366 days (400 above that).
- **Excel export** (`GET /api/reports/export.xlsx?from&to`): **ClosedXML** (MIT) builds a workbook with three sheets, a header row, typed number columns, and frozen headers. It's streamed as `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` with a `Content-Disposition` filename.
  - *Why a library:* the raw OpenXML SDK needs about ten times the code for the same workbook.
  - **Formula injection:** cells beginning with `=`, `+`, `-` or `@` are prefixed with `'`. Asset names and titles are user input, and a crafted title like `=HYPERLINK(...)` must not become a live formula in a manager's Excel (OWASP, "CSV/Excel injection").
- **Policy:** `reports:view` = supervisor or admin.

## Decision 3 — Performance work, measured in CI
There's no SQL Server on the dev laptop, so measurement happens where SQL Server exists: **GitHub Actions + Testcontainers**.
- **`perf/PlantOps.Perf`** is a console project (not part of the normal test run). It starts SQL Server in a container, applies all migrations, and seeds a realistic volume: **20,000 work orders** and **20,000 fact rows** across 4 lines, 200 assets and 24 months. Then it runs three experiments and writes **`perf-results.md`** as a workflow artifact.
  1. **Index selection from query plans.** For each report query:
     - capture the **actual execution plan** (`SET STATISTICS XML ON`) and `SET STATISTICS IO, TIME` output **without** the candidate index;
     - create the candidate index and capture again;
     - record logical reads, elapsed time, and the plan's main operator (scan vs seek).
     - **The candidate:** `IX_WorkOrderFacts_CompletedAt` with `INCLUDE (LineId, LineName, Priority, MetSla, RepairMinutes, DowntimeMinutes)`, a covering index for the date-range filter. It's added to the model **only if the measurement shows a material reduction in reads**.
  2. **N+1 vs projection.**
     - The *naive* version loads the work orders and then, per row, asks for the asset name with a separate query, the classic N+1 that lazy loading or a loop produces.
     - The *real* endpoint uses a single projection with the snapshot columns.
     - It reports the **SQL command count** (DbCommandInterceptor) and elapsed time for a 100-row page.
  3. **Compiled query.**
     - `EF.CompileAsyncQuery` vs the regular LINQ for the hottest single-row read (work order detail by id), with **BenchmarkDotNet** (mean, error, allocations).
     - Kept in the code **only if** BenchmarkDotNet shows a difference larger than the error bars. Otherwise the result is documented and the regular query stays.
- **The workflow:** `.github/workflows/perf.yml` runs on `workflow_dispatch` (manual), so it doesn't slow every PR. Shared CI runners are noisy, so we compare *ratios within one run*, never absolute numbers across runs, and the study guide says so.
- **The rule from the project brief applies:** numbers that appear in docs come from that artifact, quoted with the run id. If a run hasn't happened, there's no number.

## API
```
GET  /api/reports/mttr?from=2026-01-01&to=2026-10-01&groupBy=line|asset|month       reports:view
GET  /api/reports/sla-compliance?from&to&groupBy=priority|month                      reports:view
GET  /api/reports/downtime?from&to&groupBy=line|month                                 reports:view
GET  /api/reports/export.xlsx?from&to                                                 reports:view
POST /api/reports/rebuild                                                            admin
```
**Response row shapes (exact):**
- MTTR: `{ key, label, workOrders, meanRepairMinutes }`
- SLA: `{ key, label, completed, metSla, compliancePercent }`
- Downtime: `{ key, label, events, downtimeMinutes }`

Each endpoint returns `{ from, to, groupBy, rows: [...] }`.

## Frontend
- **`/reports`** (supervisor/admin):
  - a date range with native date inputs (default: the last 3 months);
  - three cards, each with a group-by select, a table, and a **CSS bar** per row (an inline-size percentage of the max). No chart library: a bar is a `div` with a width.
  - The values are also in the table, as text for screen readers.
- **"Export to Excel"** is a plain `<a href="/api/reports/export.xlsx?...">`. Same origin, so the cookie goes along and the browser handles the download.

## Out of scope
- Real-time dashboards.
- OLAP / Power BI. A natural next step is to point Power BI at the `reporting` schema.
