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

// Completed and Cancelled carry everything their integration events need: the mapper sees only the event, and
// consumers must never have to call back into WorkOrders. The audit payload simply gains these fields.
internal sealed record WorkOrderCompleted(
    Guid WorkOrderId,
    string Resolution,
    string Number,
    Guid AssetId,
    string Title,
    DateTimeOffset SubmittedAt,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string TechnicianName,
    bool AssetDown,
    // Appended for the Reporting read model (design 07). Older audit payloads simply lack these.
    WorkOrderPriority Priority,
    WorkOrderSource Source,
    DateTimeOffset DueAt) : IDomainEvent;

// Carries everything its integration event needs (see the note above): the notifier must not call back into WorkOrders.
internal sealed record WorkOrderSlaBreached(
    Guid WorkOrderId,
    string Number,
    string Title,
    string AssetTag,
    WorkOrderPriority Priority,
    DateTimeOffset DueAt,
    DateTimeOffset EscalatedAt,
    string? AssignedToName) : IDomainEvent;

internal sealed record WorkOrderClosed(Guid WorkOrderId) : IDomainEvent;

internal sealed record WorkOrderCancelled(Guid WorkOrderId, string Number, string Reason, DateTimeOffset CancelledAt) : IDomainEvent;
