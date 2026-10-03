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

/// <summary>A work order was cancelled. Inventory releases whatever it had reserved.</summary>
public sealed record WorkOrderCancelledIntegrationEvent(
    Guid WorkOrderId,
    string Number,
    string Reason,
    DateTimeOffset CancelledAt) : IIntegrationEvent;
