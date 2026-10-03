using PlantOps.Modules.WorkOrders.Domain;

namespace PlantOps.Modules.WorkOrders.Endpoints;

// NextDueOn is a DateOnly: System.Text.Json writes and reads it as "YYYY-MM-DD", with no time or zone to misread.
internal sealed record PmScheduleListItem(
    Guid Id,
    Guid AssetId,
    string AssetTag,
    string AssetName,
    string Title,
    int IntervalDays,
    int LeadDays,
    WorkOrderPriority Priority,
    DateOnly NextDueOn,
    bool IsActive);

internal sealed record PmScheduleDetail(
    Guid Id,
    Guid AssetId,
    string AssetTag,
    string AssetName,
    string Title,
    int IntervalDays,
    int LeadDays,
    WorkOrderPriority Priority,
    DateOnly NextDueOn,
    bool IsActive,
    string Instructions);

internal sealed record ListPmSchedulesQuery(Guid? AssetId, bool? Active);

internal sealed record CreatePmScheduleRequest(
    Guid AssetId,
    string Title,
    string? Instructions,
    int IntervalDays,
    int LeadDays,
    WorkOrderPriority Priority,
    DateOnly NextDueOn);

internal sealed record UpdatePmScheduleRequest(
    string Title,
    string? Instructions,
    int IntervalDays,
    int LeadDays,
    WorkOrderPriority Priority,
    DateOnly NextDueOn);
