using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PlantOps.BuildingBlocks.Infrastructure;
using PlantOps.Modules.Reporting.Queries;

namespace PlantOps.Perf;

/// <summary>The SQL text and parameters EF sent for one query, kept so the exact command can be replayed under SET STATISTICS.</summary>
internal sealed class CapturedCommand(string text, IReadOnlyList<SqlParameter> parameters)
{
    public string Text { get; } = text;

    /// <summary>Fresh clones every time: a SqlParameter can belong to only one command.</summary>
    public SqlCommand Create(SqlConnection connection)
    {
        var command = new SqlCommand(Text, connection) { CommandTimeout = 120 };
        foreach (var parameter in parameters)
        {
            command.Parameters.Add((SqlParameter)((ICloneable)parameter).Clone());
        }

        return command;
    }
}

/// <summary>Records the last reader command EF executes (and lets it run). Parameters carry the real values.</summary>
internal sealed class CaptureCommand : DbCommandInterceptor
{
    public CapturedCommand? Last { get; private set; }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Capture(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Capture(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void Capture(DbCommand command) => Last = new CapturedCommand(
        command.CommandText,
        [.. command.Parameters.Cast<SqlParameter>().Select(p => (SqlParameter)((ICloneable)p).Clone())]);
}

internal sealed record IndexMeasurement(string Query, string Variant, long LogicalReads, double CpuMs, double ElapsedMs, PlanSummary Plan);

/// <summary>
/// Experiment 1 (design 07, Decision 3): does a covering index on CompletedAt pay for the report queries?
/// Each report query runs exactly as ReportQueries sends it, replayed over ADO.NET with STATISTICS IO, TIME and XML
/// (the ACTUAL plan) switched on, first with no index, then with each candidate.
/// </summary>
internal static class IndexExperiment
{
    private const string Table = "reporting.WorkOrderFacts";
    private const string TableName = "WorkOrderFacts";
    private const int Runs = 5;
    private const double RequiredDrop = 0.5;

    // The candidate from the design doc, and a wider one that also covers the by-asset and by-month queries.
    private static readonly string[] CandidateInclude = ["LineId", "LineName", "Priority", "MetSla", "RepairMinutes", "DowntimeMinutes"];
    private static readonly string[] ExtendedInclude = [.. CandidateInclude, "AssetId", "AssetTag", "CompletedMonth"];

    private sealed record Query(string Name, Func<ReportQueries, ReportRange, Task> Run, ReportRange Range, bool InRule);

    private sealed record Variant(string Name, string IndexName, IReadOnlyList<string> Include);

    public static async Task<string> RunAsync(PerfDatabase database)
    {
        var zone = FactoryClock.FindZone(null);
        var today = new DateOnly(2026, 10, 1);

        // The last three months of the seeded history (about one eighth of the rows): the UI's default window shape.
        var threeMonths = ReportRange.Resolve(new DateOnly(2026, 7, 1), new DateOnly(2026, 10, 1), today, zone);
        var twelveMonths = ReportRange.Resolve(new DateOnly(2025, 10, 1), new DateOnly(2026, 10, 1), today, zone);

        Query[] queries =
        [
            new("mttr by line", (q, r) => q.MttrAsync(r, MttrGroup.Line, default), threeMonths, true),
            new("sla by priority", (q, r) => q.SlaAsync(r, SlaGroup.Priority, default), threeMonths, true),
            new("downtime by line", (q, r) => q.DowntimeAsync(r, DowntimeGroup.Line, default), threeMonths, true),
            new("mttr by month", (q, r) => q.MttrAsync(r, MttrGroup.Month, default), threeMonths, true),
            new("mttr by asset (info)", (q, r) => q.MttrAsync(r, MttrGroup.Asset, default), threeMonths, false),
            new("mttr by line, 12 months (info)", (q, r) => q.MttrAsync(r, MttrGroup.Line, default), twelveMonths, false),
        ];

        var captured = new Dictionary<string, CapturedCommand>();
        foreach (var query in queries)
        {
            var capture = new CaptureCommand();
            await using var context = database.Reporting(capture);
            await query.Run(new ReportQueries(context), query.Range);
            captured[query.Name] = capture.Last ?? throw new InvalidOperationException($"No SQL was captured for '{query.Name}'.");
        }

        Variant[] variants =
        [
            new("candidate index", "IX_WorkOrderFacts_CompletedAt", CandidateInclude),
            new("wider index", "IX_WorkOrderFacts_CompletedAt_Wide", ExtendedInclude),
        ];

        var results = new List<IndexMeasurement>();
        try
        {
            await DropIndexesAsync(database, variants);
            await MeasureAllAsync(database, queries, captured, "no index", results);

            foreach (var variant in variants)
            {
                // One index at a time, so a variant is only ever compared with the bare table.
                await database.ExecuteAsync(
                    $"CREATE INDEX {variant.IndexName} ON {Table} (CompletedAt) INCLUDE ({string.Join(", ", variant.Include)});");
                try
                {
                    await MeasureAllAsync(database, queries, captured, variant.Name, results);
                }
                finally
                {
                    await DropIndexesAsync(database, variants);
                }
            }
        }
        finally
        {
            await DropIndexesAsync(database, variants);
        }

        return Render(queries, captured, variants, results);
    }

    private static async Task MeasureAllAsync(PerfDatabase database, Query[] queries, Dictionary<string, CapturedCommand> captured, string variant, List<IndexMeasurement> results)
    {
        foreach (var query in queries)
        {
            var measurement = await MeasureAsync(database.ConnectionString, query.Name, variant, captured[query.Name]);
            results.Add(measurement);
            Console.WriteLine($"  [{variant}] {query.Name}: {measurement.LogicalReads} logical reads, cpu {measurement.CpuMs} ms, elapsed {measurement.ElapsedMs} ms");
        }
    }

    private static Task DropIndexesAsync(PerfDatabase database, Variant[] variants) => database.ExecuteAsync(string.Join(
        " ",
        variants.Select(v => $"IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{v.IndexName}' AND object_id = OBJECT_ID('{Table}')) DROP INDEX {v.IndexName} ON {Table};")));

    private static async Task<IndexMeasurement> MeasureAsync(string connectionString, string query, string variant, CapturedCommand command)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var messages = new List<string>();
        connection.InfoMessage += (_, e) => messages.Add(e.Message);

        // Session-scoped: they stay on for every command on this connection. ACTUAL plan, not an estimate.
        await using (var set = new SqlCommand("SET STATISTICS IO ON; SET STATISTICS TIME ON; SET STATISTICS XML ON;", connection))
        {
            await set.ExecuteNonQueryAsync();
        }

        var reads = new List<long>();
        var cpu = new List<int>();
        var elapsed = new List<int>();
        PlanSummary? plan = null;

        // Run 0 is a warm-up (compiles the plan, loads pages into the buffer pool) and is not counted.
        for (var run = 0; run <= Runs; run++)
        {
            messages.Clear();
            string? planXml = null;

            await using (var cmd = command.Create(connection))
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                do
                {
                    // The plan arrives as an extra single-column result set after the data.
                    if (reader.FieldCount == 1 && reader.GetName(0).Contains("XML Showplan", StringComparison.OrdinalIgnoreCase))
                    {
                        while (await reader.ReadAsync())
                        {
                            planXml = reader.GetString(0);
                        }
                    }
                    else
                    {
                        while (await reader.ReadAsync())
                        {
                        }
                    }
                }
                while (await reader.NextResultAsync());
            }

            if (run == 0)
            {
                continue;
            }

            var time = StatisticsMessages.ExecutionTime(messages)
                ?? throw new InvalidOperationException($"No 'SQL Server Execution Times' message for '{query}' [{variant}].");
            reads.Add(StatisticsMessages.LogicalReads(messages));
            cpu.Add(time.CpuMs);
            elapsed.Add(time.ElapsedMs);
            plan = PlanXml.Parse(planXml ?? throw new InvalidOperationException($"No showplan XML for '{query}' [{variant}]."));
        }

        return new IndexMeasurement(query, variant, (long)Stats.Median(reads), Stats.Median(cpu), Stats.Median(elapsed), plan!);
    }

    private static string Render(Query[] queries, Dictionary<string, CapturedCommand> captured, Variant[] variants, List<IndexMeasurement> results)
    {
        var text = new StringBuilder();
        text.AppendLine("## Experiment 1: index selection from real plans");
        text.AppendLine();
        text.AppendLine($"Window: last 3 months of the seeded history (2026-07-01 to 2026-10-01); the 12-month row uses 2025-10-01 to 2026-10-01. Each cell is the median of {Runs} runs after one warm-up, buffer cache warm, ACTUAL plans (STATISTICS XML adds the same overhead to every variant, so CPU/elapsed compare within this table only).");
        text.AppendLine();

        var rows = results.Select(m => (IReadOnlyList<string>)
        [
            m.Query,
            m.Variant,
            m.LogicalReads.ToString(CultureInfo.InvariantCulture),
            m.CpuMs.ToString("0.#", CultureInfo.InvariantCulture),
            m.ElapsedMs.ToString("0.#", CultureInfo.InvariantCulture),
            m.Plan.Describe(),
        ]);
        text.AppendLine(MarkdownTable.Render(["query", "variant", "logical reads", "CPU ms", "elapsed ms", "main operators (data source first)"], rows));

        text.AppendLine("### Columns the baseline plan needed from reporting.WorkOrderFacts");
        text.AppendLine();
        text.AppendLine("Read from the plan's data-access operators (output list plus predicates), so the INCLUDE list is chosen from evidence.");
        text.AppendLine();
        var candidateCovers = new HashSet<string>(["CompletedAt", .. CandidateInclude], StringComparer.OrdinalIgnoreCase);
        var neededRows = queries.Select(q =>
        {
            var needed = results.First(m => m.Query == q.Name && m.Variant == "no index").Plan.NeededColumns(TableName);
            var missing = needed.Where(c => !candidateCovers.Contains(c)).ToList();
            return (IReadOnlyList<string>)[q.Name, string.Join(", ", needed), missing.Count == 0 ? "(none: covered)" : string.Join(", ", missing)];
        });
        text.AppendLine(MarkdownTable.Render(["query", "columns needed", "not covered by the design's candidate"], neededRows));

        text.AppendLine("### Decision rule");
        text.AppendLine();
        text.AppendLine($"Adopt an index if logical reads drop by at least {RequiredDrop:P0} for the date-filtered report queries (the four unmarked queries above).");
        text.AppendLine();
        foreach (var variant in variants)
        {
            var drops = queries.Where(q => q.InRule).Select(q =>
            {
                var before = results.First(m => m.Query == q.Name && m.Variant == "no index").LogicalReads;
                var after = results.First(m => m.Query == q.Name && m.Variant == variant.Name).LogicalReads;
                return (q.Name, before, after, Drop: before == 0 ? 0 : 1.0 - ((double)after / before));
            }).ToList();

            var adopt = drops.All(d => d.Drop >= RequiredDrop);
            text.AppendLine($"- **{variant.Name}** (`{variant.IndexName}` on CompletedAt INCLUDE {string.Join(", ", variant.Include)}): **{(adopt ? "ADOPT" : "DO NOT ADOPT")}**");
            foreach (var d in drops)
            {
                text.AppendLine($"  - {d.Name}: {d.before} to {d.after} reads ({d.Drop:P1} drop)");
            }
        }

        text.AppendLine();
        text.AppendLine("<details><summary>Exact SQL EF generated (parameters replayed with the real values)</summary>");
        text.AppendLine();
        foreach (var query in queries)
        {
            text.AppendLine($"**{query.Name}**");
            text.AppendLine();
            text.AppendLine("```sql");
            text.AppendLine(captured[query.Name].Text);
            text.AppendLine("```");
            text.AppendLine();
        }

        text.AppendLine("</details>");
        text.AppendLine();
        return text.ToString();
    }
}
