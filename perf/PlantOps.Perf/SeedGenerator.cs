using PlantOps.BuildingBlocks.Infrastructure;
using PlantOps.Modules.Assets.Infrastructure;
using PlantOps.Modules.Reporting.Domain;

namespace PlantOps.Perf;

internal sealed record SeedOptions(
    int Seed = 20261003,
    int Assets = 200,
    int WorkOrders = 20_000,
    int Facts = 20_000,
    int Months = 24)
{
    /// <summary>The seeded history ends here (exclusive). A fixed date, not "now", so a run is reproducible.</summary>
    public static DateTimeOffset End { get; } = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    public DateTimeOffset Start => End.AddMonths(-Months);
}

internal sealed record LineSeed(Guid Id, string Code, string Name);

internal sealed record AssetSeed(Guid Id, string Tag, string Name, string Manufacturer, string Model, string Station, string Criticality, DateTime CommissionedOn, LineSeed Line);

internal sealed record WorkOrderSeed(
    Guid Id,
    int Number,
    AssetSeed Asset,
    string Title,
    string Priority,
    string Status,
    bool AssetDown,
    string Source,
    string ReportedById,
    DateTimeOffset SubmittedAt,
    DateTimeOffset DueAt,
    DateTimeOffset? ApprovedAt,
    string? AssignedToId,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ClosedAt);

internal sealed record SeedSet(IReadOnlyList<AssetSeed> Assets, IReadOnlyList<WorkOrderSeed> WorkOrders, IReadOnlyList<WorkOrderFact> Facts);

/// <summary>
/// Builds the same data for the same <see cref="SeedOptions.Seed"/>, with no database involved (so it is unit-testable).
/// The facts are generated independently of the work orders: each experiment reads only one of the two tables, and
/// the point is volume and spread (4 lines, 200 assets, 24 months), not that a fact row points at a seeded work order.
/// The facts go through the real <see cref="WorkOrderFact.Project"/>, so derived columns (MetSla, minutes, month) follow the production rules.
/// </summary>
internal static class SeedGenerator
{
    private static readonly string[] PriorityByWeight = [.. Repeat("P1", 5), .. Repeat("P2", 20), .. Repeat("P3", 50), .. Repeat("P4", 25)];

    // Percent of work orders per status; sums to 100.
    private static readonly string[] StatusByWeight =
    [
        .. Repeat("Submitted", 6), .. Repeat("Approved", 5), .. Repeat("Assigned", 6), .. Repeat("InProgress", 8),
        .. Repeat("Completed", 8), .. Repeat("Closed", 57), .. Repeat("Rejected", 5), .. Repeat("Cancelled", 5),
    ];

    private static readonly string[] Manufacturers = ["Yamaha", "Fuji", "Panasonic", "ASM", "Koh Young", "Heller"];

    public static SeedSet Generate(SeedOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var random = new Random(options.Seed);

        // The same four lines the Assets migration seeds (fixed ids), so the foreign key holds.
        LineSeed[] lines =
        [
            new(ProductionLineConfiguration.Smt1.Value, "SMT-1", "SMT Line 1"),
            new(ProductionLineConfiguration.Smt2.Value, "SMT-2", "SMT Line 2"),
            new(ProductionLineConfiguration.Fa1.Value, "FA-1", "Final Assembly 1"),
            new(ProductionLineConfiguration.Test1.Value, "TEST-1", "Functional Test 1"),
        ];

        var assets = GenerateAssets(options, lines, random);
        var totalMinutes = (int)(SeedOptions.End - options.Start).TotalMinutes;
        var workOrders = GenerateWorkOrders(options, assets, totalMinutes, random);
        var facts = GenerateFacts(options, assets, totalMinutes, random);
        return new SeedSet(assets, workOrders, facts);
    }

    /// <summary>The SLA target by priority; the numbers only shape the data, the real policy lives in WorkOrders.</summary>
    internal static TimeSpan TargetFor(string priority) => priority switch
    {
        "P1" => TimeSpan.FromHours(4),
        "P2" => TimeSpan.FromHours(8),
        "P3" => TimeSpan.FromHours(24),
        _ => TimeSpan.FromHours(72),
    };

    private static List<AssetSeed> GenerateAssets(SeedOptions options, LineSeed[] lines, Random random)
    {
        var assets = new List<AssetSeed>(options.Assets);
        for (var i = 0; i < options.Assets; i++)
        {
            var line = lines[i % lines.Length];
            assets.Add(new AssetSeed(
                GuidFrom(random),
                $"{line.Code.Replace("-", string.Empty, StringComparison.Ordinal)}-EQ-{i:D3}",
                $"Machine {i:D3}",
                Manufacturers[random.Next(Manufacturers.Length)],
                $"M-{random.Next(100, 999)}",
                $"ST{i % 10:D2}",
                "ABC"[random.Next(3)].ToString(),
                new DateTime(2020, 1, 1).AddDays(random.Next(0, 1400)),
                line));
        }

        return assets;
    }

    private static List<WorkOrderSeed> GenerateWorkOrders(SeedOptions options, List<AssetSeed> assets, int totalMinutes, Random random)
    {
        var workOrders = new List<WorkOrderSeed>(options.WorkOrders);
        for (var i = 1; i <= options.WorkOrders; i++)
        {
            var asset = assets[random.Next(assets.Count)];
            var priority = PriorityByWeight[random.Next(PriorityByWeight.Length)];
            var status = StatusByWeight[random.Next(StatusByWeight.Length)];
            var submitted = options.Start.AddMinutes(random.Next(0, totalMinutes));

            // Each later milestone exists only for statuses that got that far, like real rows.
            DateTimeOffset? approved = status is "Approved" or "Assigned" or "InProgress" or "Completed" or "Closed"
                ? submitted.AddMinutes(random.Next(5, 240))
                : null;
            var assigned = status is "Assigned" or "InProgress" or "Completed" or "Closed" ? $"tech-{random.Next(1, 13)}" : null;
            DateTimeOffset? started = status is "InProgress" or "Completed" or "Closed" ? approved!.Value.AddMinutes(random.Next(10, 600)) : null;
            DateTimeOffset? completed = status is "Completed" or "Closed" ? started!.Value.AddMinutes(random.Next(10, 480)) : null;
            DateTimeOffset? closed = status is "Closed" ? completed!.Value.AddMinutes(random.Next(5, 1000)) : null;

            workOrders.Add(new WorkOrderSeed(
                GuidFrom(random),
                i,
                asset,
                $"Fault on {asset.Tag} ({i})",
                priority,
                status,
                random.Next(100) < 40,
                random.Next(100) < 85 ? "Reactive" : "Preventive",
                $"user-{random.Next(1, 40)}",
                submitted,
                submitted + TargetFor(priority),
                approved,
                assigned,
                started,
                completed,
                closed));
        }

        return workOrders;
    }

    private static List<WorkOrderFact> GenerateFacts(SeedOptions options, List<AssetSeed> assets, int totalMinutes, Random random)
    {
        var zone = FactoryClock.FindZone(null);
        var facts = new List<WorkOrderFact>(options.Facts);
        for (var i = 1; i <= options.Facts; i++)
        {
            var asset = assets[random.Next(assets.Count)];
            var priority = PriorityByWeight[random.Next(PriorityByWeight.Length)];
            var completed = options.Start.AddMinutes(random.Next(0, totalMinutes));

            // Repair time is skewed (many short jobs, a long tail), like real maintenance data.
            var repairMinutes = Math.Min(10 + (int)(-Math.Log(1 - random.NextDouble()) * 90), 2000);
            var started = completed.AddMinutes(-repairMinutes);
            var submitted = started.AddMinutes(-random.Next(5, 2000));

            facts.Add(WorkOrderFact.Project(
                new CompletedWork(
                    GuidFrom(random),
                    $"WO-{i:D6}",
                    asset.Id,
                    priority,
                    random.Next(100) < 85 ? "Reactive" : "Preventive",
                    submitted,
                    started,
                    completed,
                    submitted + TargetFor(priority),
                    random.Next(100) < 45),
                new AssetRef(asset.Tag, asset.Line.Id, asset.Line.Name),
                zone));
        }

        return facts;
    }

    // Guid.NewGuid would differ on every run; this takes the bytes from the seeded generator instead.
    private static Guid GuidFrom(Random random)
    {
        Span<byte> bytes = stackalloc byte[16];
        random.NextBytes(bytes);
        return new Guid(bytes);
    }

    private static IEnumerable<string> Repeat(string value, int count) => Enumerable.Repeat(value, count);
}
