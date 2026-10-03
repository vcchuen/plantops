using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PlantOps.Modules.Assets.Domain;
using PlantOps.Modules.Assets.Infrastructure;
using PlantOps.Modules.WorkOrders.Domain;
using PlantOps.Modules.WorkOrders.Infrastructure;

namespace PlantOps.Perf;

/// <summary>Counts every SQL command EF executes (same idea as the CommandCounter in the integration tests).</summary>
internal sealed class CommandCounter : DbCommandInterceptor
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public void Reset() => Interlocked.Exchange(ref _count, 0);

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Interlocked.Increment(ref _count);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _count);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}

/// <summary>
/// Experiment 2: N+1 against the real list projection, for one page of 100 work orders.
/// The question the naive version answers: "what would the list cost if each row had to ask the Assets module for the
/// asset name?". That is what the list does NOT do, because the work order stores a snapshot of the asset tag and name
/// (ADR-0002: no cross-schema join, and a rename must not rewrite history). Without the snapshot, a loop over
/// entities issuing one lookup per row is the natural (and classic N+1) way to write it.
/// </summary>
internal static class NPlusOneExperiment
{
    private const int PageSize = 100;
    private const int WarmUps = 3;
    private const int Runs = 10;

    private sealed record Outcome(string Variant, int Commands, double MedianMs, double MinMs, double MaxMs, int Rows);

    public static async Task<string> RunAsync(PerfDatabase database)
    {
        var counter = new CommandCounter();
        var workOrderOptions = database.Options<WorkOrdersDbContext>(WorkOrdersDbContext.Schema, counter);
        var assetOptions = database.Options<AssetsDbContext>(AssetsDbContext.Schema, counter);

        var outcomes = new[]
        {
            await MeasureAsync("naive: page of entities + 1 asset lookup per row", counter, () => NaiveAsync(workOrderOptions, assetOptions)),
            await MeasureAsync("real: single projection with snapshot columns", counter, () => ProjectionAsync(workOrderOptions)),
        };

        var text = new StringBuilder();
        text.AppendLine("## Experiment 2: N+1 versus projection");
        text.AppendLine();
        text.AppendLine($"One page of {PageSize} work orders (newest first, like the list endpoint) over {outcomes[0].Rows} rows returned. Commands are counted by a DbCommandInterceptor; time is a Stopwatch median of {Runs} runs after {WarmUps} warm-ups, each run with fresh DbContexts (as one HTTP request would have). Every variant includes the endpoint's COUNT query.");
        text.AppendLine();
        text.AppendLine(MarkdownTable.Render(
            ["variant", "SQL commands", "median ms", "min ms", "max ms"],
            outcomes.Select(o => (IReadOnlyList<string>)
            [
                o.Variant,
                o.Commands.ToString(CultureInfo.InvariantCulture),
                o.MedianMs.ToString("0.0", CultureInfo.InvariantCulture),
                o.MinMs.ToString("0.0", CultureInfo.InvariantCulture),
                o.MaxMs.ToString("0.0", CultureInfo.InvariantCulture),
            ])));
        text.AppendLine($"Ratio (naive / real): {outcomes[0].MedianMs / outcomes[1].MedianMs:0.0}x in time, {outcomes[0].Commands}:{outcomes[1].Commands} in commands.");
        text.AppendLine();
        return text.ToString();
    }

    private static async Task<Outcome> MeasureAsync(string variant, CommandCounter counter, Func<Task<int>> run)
    {
        for (var i = 0; i < WarmUps; i++)
        {
            await run();
        }

        var times = new List<double>();
        var commands = new HashSet<int>();
        var rows = 0;
        for (var i = 0; i < Runs; i++)
        {
            counter.Reset();
            var watch = Stopwatch.StartNew();
            rows = await run();
            watch.Stop();
            times.Add(watch.Elapsed.TotalMilliseconds);
            commands.Add(counter.Count);
        }

        // The count is a property of the code shape, so every run must agree; a mismatch would mean the measurement is broken.
        if (commands.Count != 1)
        {
            throw new InvalidOperationException($"'{variant}' issued a varying number of commands: {string.Join(", ", commands)}.");
        }

        Console.WriteLine($"  {variant}: {commands.Single()} commands, median {Stats.Median(times):0.0} ms");
        return new Outcome(variant, commands.Single(), Stats.Median(times), times.Min(), times.Max(), rows);
    }

    // Entities, then the asset name per row from the Assets module's own table.
    private static async Task<int> NaiveAsync(DbContextOptions<WorkOrdersDbContext> workOrderOptions, DbContextOptions<AssetsDbContext> assetOptions)
    {
        await using var workOrders = new WorkOrdersDbContext(workOrderOptions);
        await using var assets = new AssetsDbContext(assetOptions);

        _ = await workOrders.WorkOrders.AsNoTracking().CountAsync();
        var page = await PageOfEntities(workOrders, 0).ToListAsync();

        var names = new List<string>(page.Count);
        foreach (var workOrder in page)
        {
            names.Add(await AssetName(assets, new AssetId(workOrder.AssetId)).FirstAsync());
        }

        return names.Count;
    }

    // The same two queries as WorkOrderEndpoints.List: COUNT, then the page as one projection.
    private static async Task<int> ProjectionAsync(DbContextOptions<WorkOrdersDbContext> workOrderOptions)
    {
        await using var workOrders = new WorkOrdersDbContext(workOrderOptions);

        _ = await workOrders.WorkOrders.AsNoTracking().CountAsync();
        var rows = await PageProjection(workOrders, 0).ToListAsync();
        return rows.Count;
    }

    // The query builders are internal so a unit test can check their SQL without a database (ToQueryString needs no connection).
    // The skip is a parameter, not a literal: the endpoint computes it from the page number, so it is a SQL parameter there too.
    internal static IQueryable<WorkOrder> PageOfEntities(WorkOrdersDbContext db, int skip) => db.WorkOrders.AsNoTracking()
        .OrderByDescending(w => w.SubmittedAt)
        .ThenByDescending(w => w.Number)
        .Skip(skip)
        .Take(PageSize);

    internal static IQueryable<string> AssetName(AssetsDbContext db, AssetId id) => db.Assets.AsNoTracking()
        .Where(a => a.Id == id)
        .Select(a => a.Name);

    internal static IQueryable<PageRow> PageProjection(WorkOrdersDbContext db, int skip) => db.WorkOrders.AsNoTracking()
        .OrderByDescending(w => w.SubmittedAt)
        .ThenByDescending(w => w.Number)
        .Skip(skip)
        .Take(PageSize)
        .Select(w => new PageRow(
            w.Id.Value,
            w.Number,
            w.AssetId,
            w.AssetTag,
            w.AssetName,
            w.Title,
            w.Priority,
            w.Status,
            w.AssignedToId,
            w.AssignedToName,
            w.SubmittedAt,
            w.CompletedAt,
            w.DueAt,
            w.Source,
            w.EscalatedAt));

    /// <summary>The columns WorkOrderEndpoints.List selects (it uses an anonymous type; the SQL is the same).</summary>
    internal sealed record PageRow(
        Guid Id,
        int Number,
        Guid AssetId,
        string AssetTag,
        string AssetName,
        string Title,
        WorkOrderPriority Priority,
        WorkOrderStatus Status,
        string? AssignedToId,
        string? AssignedToName,
        DateTimeOffset SubmittedAt,
        DateTimeOffset? CompletedAt,
        DateTimeOffset DueAt,
        WorkOrderSource Source,
        DateTimeOffset? EscalatedAt);
}
