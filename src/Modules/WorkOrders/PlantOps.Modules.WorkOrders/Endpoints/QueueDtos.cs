using PlantOps.Modules.WorkOrders.Domain;

namespace PlantOps.Modules.WorkOrders.Endpoints;

internal sealed record QueueQuery(bool DueToday = false);

internal sealed record QueueItem(
    Guid Id,
    string Number,
    string Title,
    string AssetTag,
    WorkOrderPriority Priority,
    WorkOrderStatus Status,
    DateTimeOffset DueAt,
    SlaState? SlaState,
    int PartsReserved);
