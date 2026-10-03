using PlantOps.SharedKernel;

namespace PlantOps.Modules.WorkOrders.Domain;

// Audit payloads: ids and the facts that changed. Who and when come from the audit row itself.
internal sealed record WorkOrderSubmitted(
    Guid WorkOrderId,
    Guid AssetId,
    string AssetTag,
    string Title,
    WorkOrderPriority Priority,
    bool AssetDown,
    DateTimeOffset DueAt) : IDomainEvent;

internal sealed record WorkOrderApproved(Guid WorkOrderId, WorkOrderPriority Priority, DateTimeOffset DueAt) : IDomainEvent;

internal sealed record WorkOrderRejected(Guid WorkOrderId, string Reason) : IDomainEvent;

internal sealed record WorkOrderAssigned(
    Guid WorkOrderId,
    string TechnicianId,
    string TechnicianName,
    string? PreviousTechnicianId) : IDomainEvent;

internal sealed record WorkOrderStarted(Guid WorkOrderId) : IDomainEvent;

internal sealed record WorkOrderCompleted(Guid WorkOrderId, string Resolution) : IDomainEvent;

internal sealed record WorkOrderClosed(Guid WorkOrderId) : IDomainEvent;

internal sealed record WorkOrderCancelled(Guid WorkOrderId, string Reason) : IDomainEvent;
