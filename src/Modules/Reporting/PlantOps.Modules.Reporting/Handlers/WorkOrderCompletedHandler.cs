using Microsoft.EntityFrameworkCore;
using PlantOps.BuildingBlocks.Infrastructure;
using PlantOps.Modules.Assets.Contracts;
using PlantOps.Modules.Reporting.Domain;
using PlantOps.Modules.Reporting.Infrastructure;
using PlantOps.Modules.WorkOrders.Contracts;

namespace PlantOps.Modules.Reporting.Handlers;

/// <summary>A completed work order becomes one row of the reporting read model (ADR-0011).</summary>
internal sealed class WorkOrderCompletedHandler(
    ReportingDbContext db,
    IAssetDirectory assets,
    FactoryClock clock,
    TimeProvider time) : IIntegrationEventHandler<WorkOrderCompletedIntegrationEvent>
{
    public async Task HandleAsync(WorkOrderCompletedIntegrationEvent integrationEvent, Guid messageId, CancellationToken cancellationToken)
    {
        // Resolved NOW, at completion time, so the fact records the line the machine was on when the work was done.
        // Done before RunOnceAsync: the lookup reads another module's schema and is harmless to repeat on redelivery.
        var asset = await assets.FindAsync(integrationEvent.AssetId, cancellationToken);
        var assetRef = asset is null ? AssetRef.Unknown : new AssetRef(asset.Tag, asset.LineId, asset.LineName);

        await db.RunOnceAsync(messageId, nameof(WorkOrderCompletedHandler), time, async ct =>
        {
            // A message written before M7 lacks DueAt (it deserializes to the default). Nothing true can be derived
            // from it, so no fact is invented: the inbox row is still recorded and POST /api/reports/rebuild
            // fills the row from the WorkOrders module.
            if (integrationEvent.DueAt == default)
            {
                return;
            }

            var fresh = WorkOrderFact.Project(
                new CompletedWork(
                    integrationEvent.WorkOrderId,
                    integrationEvent.Number,
                    integrationEvent.AssetId,
                    integrationEvent.Priority,
                    integrationEvent.Source,
                    integrationEvent.SubmittedAt,
                    integrationEvent.StartedAt,
                    integrationEvent.CompletedAt,
                    integrationEvent.DueAt,
                    integrationEvent.AssetDown),
                assetRef,
                clock.Zone);

            // A rebuild may have projected this work order before its event arrived. The event is the better source
            // for the line (it was resolved at completion), so it overwrites rather than colliding on the key.
            var existing = await db.WorkOrderFacts.FirstOrDefaultAsync(f => f.WorkOrderId == fresh.WorkOrderId, ct);
            if (existing is null)
            {
                db.WorkOrderFacts.Add(fresh);
            }
            else
            {
                existing.Overwrite(fresh, keepKnownLine: false);
            }
        }, cancellationToken);
    }
}
