using Microsoft.EntityFrameworkCore;
using PlantOps.BuildingBlocks.Infrastructure;
using PlantOps.Modules.Assets.Contracts;
using PlantOps.Modules.Reporting.Domain;
using PlantOps.Modules.Reporting.Infrastructure;
using PlantOps.Modules.WorkOrders.Contracts;

namespace PlantOps.Modules.Reporting.Projection;

/// <summary>
/// Re-projects the read model from the WorkOrders module's completed orders (ADR-0011). Needed once for work that was
/// completed before Reporting existed, and any time the projection must be repaired. Idempotent: it upserts by work order id.
/// </summary>
internal sealed class ReportRebuilder(
    ReportingDbContext db,
    IWorkOrderDirectory workOrders,
    IAssetDirectory assets,
    FactoryClock clock)
{
    // Small enough to keep the change tracker and the IN (...) list modest, large enough to avoid one round trip per row.
    internal const int BatchSize = 200;

    public async Task<int> RebuildAsync(CancellationToken cancellationToken)
    {
        var assetCache = new Dictionary<Guid, AssetRef>();
        var batch = new List<CompletedWorkOrder>(BatchSize);
        var projected = 0;

        // Streamed from WorkOrders (its own context and connection), so the whole history is never in memory at once.
        await foreach (var workOrder in workOrders.CompletedSinceAsync(DateTimeOffset.MinValue, cancellationToken))
        {
            batch.Add(workOrder);
            if (batch.Count == BatchSize)
            {
                projected += await UpsertAsync(batch, assetCache, cancellationToken);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            projected += await UpsertAsync(batch, assetCache, cancellationToken);
        }

        return projected;
    }

    private async Task<int> UpsertAsync(List<CompletedWorkOrder> batch, Dictionary<Guid, AssetRef> assetCache, CancellationToken cancellationToken)
    {
        var ids = batch.Select(w => w.WorkOrderId).ToList();
        var existing = await db.WorkOrderFacts
            .Where(f => ids.Contains(f.WorkOrderId))
            .ToDictionaryAsync(f => f.WorkOrderId, cancellationToken);

        foreach (var workOrder in batch)
        {
            if (!assetCache.TryGetValue(workOrder.AssetId, out var asset))
            {
                // The line as of TODAY: a rebuild cannot know the historical line, which is why facts that already
                // have a line keep it (Overwrite with keepKnownLine).
                var summary = await assets.FindAsync(workOrder.AssetId, cancellationToken);
                asset = summary is null ? AssetRef.Unknown : new AssetRef(summary.Tag, summary.LineId, summary.LineName);
                assetCache[workOrder.AssetId] = asset;
            }

            var fresh = WorkOrderFact.Project(
                new CompletedWork(
                    workOrder.WorkOrderId,
                    workOrder.Number,
                    workOrder.AssetId,
                    workOrder.Priority,
                    workOrder.Source,
                    workOrder.SubmittedAt,
                    workOrder.StartedAt,
                    workOrder.CompletedAt,
                    workOrder.DueAt,
                    workOrder.AssetDown),
                asset,
                clock.Zone);

            if (existing.TryGetValue(workOrder.WorkOrderId, out var fact))
            {
                fact.Overwrite(fresh, keepKnownLine: true);
            }
            else
            {
                db.WorkOrderFacts.Add(fresh);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return batch.Count;
    }
}
