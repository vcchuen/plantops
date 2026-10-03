using PlantOps.SharedKernel;

namespace PlantOps.Modules.Inventory.Domain;

// Audit payloads: ids and the facts that changed. Who and when come from the audit row itself.
internal sealed record PartRegistered(
    Guid PartId,
    string PartNumber,
    string Name,
    string Unit,
    string BinLocation,
    int ReorderLevel) : IDomainEvent;

internal sealed record StockReceived(Guid PartId, int Quantity, int QuantityOnHand) : IDomainEvent;

/// <param name="Quantity">The amount added by this call (a repeat reservation for the same work order adds to it).</param>
/// <param name="ReservedForWorkOrder">The reservation's total after this call.</param>
internal sealed record PartReserved(
    Guid PartId,
    Guid ReservationId,
    Guid WorkOrderId,
    string WorkOrderNumber,
    int Quantity,
    int ReservedForWorkOrder) : IDomainEvent;

internal sealed record ReservationReleased(Guid PartId, Guid ReservationId, Guid WorkOrderId, int Quantity) : IDomainEvent;

internal sealed record ReservationsConsumed(Guid PartId, Guid ReservationId, Guid WorkOrderId, int Quantity) : IDomainEvent;
