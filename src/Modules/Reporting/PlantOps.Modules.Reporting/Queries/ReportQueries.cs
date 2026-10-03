using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PlantOps.Modules.Reporting.Domain;
using PlantOps.Modules.Reporting.Infrastructure;

namespace PlantOps.Modules.Reporting.Queries;

/// <summary>
/// The three reports (design 07, Decision 2). Every aggregate (COUNT, AVG, SUM) is computed by SQL Server over
/// <c>reporting.WorkOrderFacts</c>; only the small grouped result comes back, where keys and labels are formatted.
/// </summary>
internal sealed class ReportQueries(ReportingDbContext db)
{
    private const string UnknownKey = "unknown";

    public async Task<IReadOnlyList<MttrRow>> MttrAsync(ReportRange range, MttrGroup group, CancellationToken cancellationToken)
    {
        var facts = InRange(range);
        return group switch
        {
            MttrGroup.Line => await Mttr(facts.GroupBy(f => new { f.LineId, f.LineName }), k => Line(k.LineId, k.LineName), false, cancellationToken),
            MttrGroup.Asset => await Mttr(facts.GroupBy(f => new { f.AssetId, f.AssetTag }), k => (k.AssetId.ToString(), k.AssetTag), false, cancellationToken),
            _ => await Mttr(facts.GroupBy(f => f.CompletedMonth), Month, true, cancellationToken),
        };
    }

    public async Task<IReadOnlyList<SlaRow>> SlaAsync(ReportRange range, SlaGroup group, CancellationToken cancellationToken)
    {
        var facts = InRange(range);
        return group switch
        {
            SlaGroup.Priority => await Sla(facts.GroupBy(f => f.Priority), Priority, false, cancellationToken),
            _ => await Sla(facts.GroupBy(f => f.CompletedMonth), Month, true, cancellationToken),
        };
    }

    public async Task<IReadOnlyList<DowntimeRow>> DowntimeAsync(ReportRange range, DowntimeGroup group, CancellationToken cancellationToken)
    {
        // Only work that took the asset down has downtime; a null is "not applicable", and must not count as an event.
        var facts = InRange(range).Where(f => f.DowntimeMinutes != null);
        return group switch
        {
            DowntimeGroup.Line => await Downtime(facts.GroupBy(f => new { f.LineId, f.LineName }), k => Line(k.LineId, k.LineName), false, cancellationToken),
            _ => await Downtime(facts.GroupBy(f => f.CompletedMonth), Month, true, cancellationToken),
        };
    }

    // [from, to) in UTC instants. Month grouping uses the stored CompletedMonth (factory time), the window uses CompletedAt.
    private IQueryable<WorkOrderFact> InRange(ReportRange range) => db.WorkOrderFacts
        .AsNoTracking()
        .Where(f => f.CompletedAt >= range.FromUtc && f.CompletedAt < range.ToUtc);

    private static async Task<IReadOnlyList<MttrRow>> Mttr<TKey>(
        IQueryable<IGrouping<TKey, WorkOrderFact>> groups,
        Func<TKey, (string Key, string Label)> describe,
        bool chronological,
        CancellationToken cancellationToken)
    {
        var aggregates = await groups
            .Select(g => new Aggregate<TKey>
            {
                Key = g.Key,
                Count = g.Count(),
                Mean = g.Average(f => (double)f.RepairMinutes),
            })
            .ToListAsync(cancellationToken);

        return Sort(
            aggregates.Select(a =>
            {
                var (key, label) = describe(a.Key);
                return new MttrRow(key, label, a.Count, RoundOneDecimal(a.Mean));
            }),
            r => r.Key,
            r => r.Label,
            chronological);
    }

    private static async Task<IReadOnlyList<SlaRow>> Sla<TKey>(
        IQueryable<IGrouping<TKey, WorkOrderFact>> groups,
        Func<TKey, (string Key, string Label)> describe,
        bool chronological,
        CancellationToken cancellationToken)
    {
        var aggregates = await groups
            .Select(g => new Aggregate<TKey>
            {
                Key = g.Key,
                Count = g.Count(),
                Met = g.Count(f => f.MetSla),
            })
            .ToListAsync(cancellationToken);

        return Sort(
            aggregates.Select(a =>
            {
                var (key, label) = describe(a.Key);
                return new SlaRow(key, label, a.Count, a.Met, RoundOneDecimal(a.Met * 100.0 / a.Count));
            }),
            r => r.Key,
            r => r.Label,
            chronological);
    }

    private static async Task<IReadOnlyList<DowntimeRow>> Downtime<TKey>(
        IQueryable<IGrouping<TKey, WorkOrderFact>> groups,
        Func<TKey, (string Key, string Label)> describe,
        bool chronological,
        CancellationToken cancellationToken)
    {
        var aggregates = await groups
            .Select(g => new Aggregate<TKey>
            {
                Key = g.Key,
                Count = g.Count(),
                Sum = g.Sum(f => f.DowntimeMinutes ?? 0),
            })
            .ToListAsync(cancellationToken);

        return Sort(
            aggregates.Select(a =>
            {
                var (key, label) = describe(a.Key);
                return new DowntimeRow(key, label, a.Count, a.Sum);
            }),
            r => r.Key,
            r => r.Label,
            chronological);
    }

    // Months run oldest to newest ("2026-07" sorts correctly as text); everything else reads alphabetically by label.
    private static List<T> Sort<T>(IEnumerable<T> rows, Func<T, string> key, Func<T, string> label, bool chronological) =>
        chronological
            ? [.. rows.OrderBy(key, StringComparer.Ordinal)]
            : [.. rows.OrderBy(label, StringComparer.OrdinalIgnoreCase).ThenBy(key, StringComparer.Ordinal)];

    internal static double RoundOneDecimal(double value) => Math.Round(value, 1, MidpointRounding.AwayFromZero);

    internal static (string Key, string Label) Line(Guid? id, string name) => (id?.ToString() ?? UnknownKey, name);

    internal static (string Key, string Label) Month(DateOnly month) =>
        (month.ToString("yyyy-MM", CultureInfo.InvariantCulture), month.ToString("MMM yyyy", CultureInfo.InvariantCulture));

    internal static (string Key, string Label) Priority(string priority) => (priority, priority switch
    {
        "P1" => "P1 Critical",
        "P2" => "P2 High",
        "P3" => "P3 Medium",
        "P4" => "P4 Low",
        _ => priority,
    });

    // One shape for the three reports' SQL projections; each report fills only the aggregates it needs.
    private sealed class Aggregate<TKey>
    {
        public TKey Key { get; init; } = default!;

        public int Count { get; init; }

        public double Mean { get; init; }

        public int Met { get; init; }

        public int Sum { get; init; }
    }
}
