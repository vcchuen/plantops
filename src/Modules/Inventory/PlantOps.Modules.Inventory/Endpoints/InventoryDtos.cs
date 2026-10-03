using PlantOps.Modules.Inventory.Domain;

namespace PlantOps.Modules.Inventory.Endpoints;

internal sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

internal sealed record PartListItem(
    Guid Id,
    string PartNumber,
    string Name,
    string Unit,
    string BinLocation,
    int QuantityOnHand,
    int QuantityReserved,
    int QuantityAvailable,
    int ReorderLevel,
    bool IsLowStock);

/// <summary>One active reservation as the part detail shows it.</summary>
internal sealed record PartReservationItem(
    Guid Id,
    Guid WorkOrderId,
    string WorkOrderNumber,
    int Quantity,
    ReservationStatus Status,
    DateTimeOffset ReservedAt,
    string ReservedByName);

// The list item's fields plus the active reservations. Spelled out (not inherited) so the JSON shape is flat and explicit.
internal sealed record PartDetail(
    Guid Id,
    string PartNumber,
    string Name,
    string Unit,
    string BinLocation,
    int QuantityOnHand,
    int QuantityReserved,
    int QuantityAvailable,
    int ReorderLevel,
    bool IsLowStock,
    IReadOnlyList<PartReservationItem> Reservations);

internal sealed record ReservationItem(
    Guid Id,
    Guid PartId,
    string PartNumber,
    string PartName,
    string Unit,
    int Quantity,
    ReservationStatus Status,
    DateTimeOffset ReservedAt,
    string ReservedByName);

internal sealed record ListPartsQuery(string? Search, bool LowStock = false, int Page = 1, int PageSize = 25);

internal sealed record ListReservationsQuery(Guid? WorkOrderId);

internal sealed record CreatePartRequest(string PartNumber, string Name, string Unit, string BinLocation, int ReorderLevel);

internal sealed record ReceiveRequest(int Quantity);

internal sealed record ReserveRequest(Guid PartId, Guid WorkOrderId, int Quantity);
