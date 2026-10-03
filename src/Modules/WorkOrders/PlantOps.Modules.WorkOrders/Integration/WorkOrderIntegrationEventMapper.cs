using PlantOps.BuildingBlocks.Infrastructure;
using PlantOps.Modules.WorkOrders.Contracts;
using PlantOps.Modules.WorkOrders.Domain;
using PlantOps.Modules.WorkOrders.Infrastructure;
using PlantOps.SharedKernel;

namespace PlantOps.Modules.WorkOrders.Integration;

// The one place that decides which internal facts become public contracts. Every other domain event (submitted,
// approved, ...) stays audit-only: nobody outside WorkOrders needs to hear about it yet.
internal sealed class WorkOrderIntegrationEventMapper : IIntegrationEventMapper
{
    public Type ContextType => typeof(WorkOrdersDbContext);

    public IIntegrationEvent? Map(IDomainEvent domainEvent) => domainEvent switch
    {
        WorkOrderCompleted e => new WorkOrderCompletedIntegrationEvent(
            e.WorkOrderId,
            e.Number,
            e.AssetId,
            e.Title,
            e.Resolution,
            e.SubmittedAt,
            e.StartedAt,
            e.CompletedAt,
            e.TechnicianName,
            e.AssetDown,
            e.Priority.ToString(),
            e.Source.ToString(),
            e.DueAt),
        WorkOrderSlaBreached e => new WorkOrderSlaBreachedIntegrationEvent(
            e.WorkOrderId,
            e.Number,
            e.Title,
            e.AssetTag,
            e.Priority.ToString(),
            e.DueAt,
            e.EscalatedAt,
            e.AssignedToName),
        WorkOrderCancelled e => new WorkOrderCancelledIntegrationEvent(e.WorkOrderId, e.Number, e.Reason, e.CancelledAt),
        _ => null,
    };
}
