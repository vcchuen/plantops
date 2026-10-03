using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlantOps.BuildingBlocks.Infrastructure;
using PlantOps.Modules.Inventory.Domain;
using PlantOps.Modules.Inventory.Infrastructure;
using PlantOps.Modules.WorkOrders.Contracts;

namespace PlantOps.Modules.Inventory.Handlers;

/// <summary>A completed work order uses up the parts reserved for it (stock goes down).</summary>
internal sealed class WorkOrderCompletedHandler(IServiceScopeFactory scopes, TimeProvider time)
    : IIntegrationEventHandler<WorkOrderCompletedIntegrationEvent>
{
    public Task HandleAsync(WorkOrderCompletedIntegrationEvent integrationEvent, Guid messageId, CancellationToken cancellationToken) =>
        // Fresh scope per attempt (ConcurrencyRetry): a reservation or receipt may be changing the same part right now.
        ConcurrencyRetry.RunAsync(scopes, (sp, ct) =>
        {
            var db = sp.GetRequiredService<InventoryDbContext>();
            var now = time.GetUtcNow();
            return db.RunOnceAsync(messageId, nameof(WorkOrderCompletedHandler), time, async token =>
            {
                var parts = await InventoryQueries.PartsWithActiveReservationFor(db, integrationEvent.WorkOrderId).ToListAsync(token);
                foreach (var part in parts)
                {
                    part.ConsumeFor(integrationEvent.WorkOrderId, now);
                }
            }, ct);
        }, cancellationToken);
}
