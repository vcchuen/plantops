using PlantOps.SharedKernel;

namespace PlantOps.Modules.WorkOrders.Contracts;

// Public, versioned contracts (ADR-0009): fields may be added, never repurposed or removed.
// They carry everything a consumer needs so consumers never call back into WorkOrders.

/// <summary>A work order was completed. Inventory consumes its reserved parts; Assets records the maintenance.</summary>
public sealed record WorkOrderCompletedIntegrationEvent(
    Guid WorkOrderId,
    string Number,
    Guid AssetId,
    string Title,
    string Resolution,
    DateTimeOffset SubmittedAt,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string TechnicianName,
    bool AssetDown) : IIntegrationEvent;

/// <summary>
/// An open work order passed its deadline and was escalated (once). Forwarded to the Service Bus queue that feeds the
/// notification function. Priority is the name ("P1".."P4") so the contract does not leak the internal enum.
/// </summary>
public sealed record WorkOrderSlaBreachedIntegrationEvent(
    Guid WorkOrderId,
    string Number,
    string Title,
    string AssetTag,
    string Priority,
    DateTimeOffset DueAt,
    DateTimeOffset EscalatedAt,
    string? AssignedToName) : IIntegrationEvent;

/// <summary>A work order was cancelled. Inventory releases whatever it had reserved.</summary>
public sealed record WorkOrderCancelledIntegrationEvent(
    Guid WorkOrderId,
    string Number,
    string Reason,
    DateTimeOffset CancelledAt) : IIntegrationEvent;
