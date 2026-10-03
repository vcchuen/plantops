using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlantOps.BuildingBlocks.Infrastructure;
using PlantOps.Modules.Inventory.Domain;
using PlantOps.Modules.Inventory.Infrastructure;
using PlantOps.Modules.WorkOrders.Contracts;

namespace PlantOps.Modules.Inventory.Handlers;

/// <summary>A cancelled work order gives its reserved parts back.</summary>
internal sealed class WorkOrderCancelledHandler(IServiceScopeFactory scopes, TimeProvider time)
    : IIntegrationEventHandler<WorkOrderCancelledIntegrationEvent>
{
    public Task HandleAsync(WorkOrderCancelledIntegrationEvent integrationEvent, Guid messageId, CancellationToken cancellationToken) =>
        ConcurrencyRetry.RunAsync(scopes, (sp, ct) =>
        {
            var db = sp.GetRequiredService<InventoryDbContext>();
            var now = time.GetUtcNow();
            return db.RunOnceAsync(messageId, nameof(WorkOrderCancelledHandler), time, async token =>
            {
                var parts = await InventoryQueries.PartsWithActiveReservationFor(db, integrationEvent.WorkOrderId).ToListAsync(token);
                foreach (var part in parts)
                {
                    part.ReleaseFor(integrationEvent.WorkOrderId, now);
                }
            }, ct);
        }, cancellationToken);
}
