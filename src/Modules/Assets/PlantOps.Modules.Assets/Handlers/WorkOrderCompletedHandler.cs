using PlantOps.BuildingBlocks.Infrastructure;
using PlantOps.Modules.Assets.Domain;
using PlantOps.Modules.Assets.Infrastructure;
using PlantOps.Modules.WorkOrders.Contracts;

namespace PlantOps.Modules.Assets.Handlers;

/// <summary>A completed work order becomes a line in the asset's maintenance history.</summary>
internal sealed class WorkOrderCompletedHandler(AssetsDbContext db, TimeProvider time)
    : IIntegrationEventHandler<WorkOrderCompletedIntegrationEvent>
{
    public async Task HandleAsync(WorkOrderCompletedIntegrationEvent integrationEvent, Guid messageId, CancellationToken cancellationToken) =>
        // The record and its inbox row commit in one SaveChanges; a redelivery is recognised and skipped. The unique
        // index on WorkOrderId would also stop a duplicate (RunOnceAsync treats that violation as "already done").
        await db.RunOnceAsync(messageId, nameof(WorkOrderCompletedHandler), time, _ =>
        {
            db.Set<MaintenanceRecord>().Add(new MaintenanceRecord(
                integrationEvent.AssetId,
                integrationEvent.WorkOrderId,
                integrationEvent.Number,
                integrationEvent.Title,
                integrationEvent.Resolution,
                integrationEvent.TechnicianName,
                integrationEvent.CompletedAt,
                MaintenanceRecord.DowntimeFor(integrationEvent.AssetDown, integrationEvent.SubmittedAt, integrationEvent.CompletedAt)));
            return Task.CompletedTask;
        }, cancellationToken);
}
