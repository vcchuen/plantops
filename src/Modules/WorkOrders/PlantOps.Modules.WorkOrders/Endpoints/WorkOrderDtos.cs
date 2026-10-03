using PlantOps.Modules.WorkOrders.Domain;

namespace PlantOps.Modules.WorkOrders.Endpoints;

internal sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

internal sealed record PersonRef(string Id, string Name);

internal sealed record WorkOrderListItem(
    Guid Id,
    string Number,
    Guid AssetId,
    string AssetTag,
    string AssetName,
    string Title,
    WorkOrderPriority Priority,
    WorkOrderStatus Status,
    SlaState? SlaState,
    PersonRef? AssignedTo,
    DateTimeOffset SubmittedAt,
    DateTimeOffset DueAt,
    WorkOrderSource Source,
    DateTimeOffset? EscalatedAt);

internal sealed record WorkOrderDetail(
    Guid Id,
    string Number,
    Guid AssetId,
    string AssetTag,
    string AssetName,
    string Title,
    string Description,
    WorkOrderPriority Priority,
    bool AssetDown,
    WorkOrderStatus Status,
    SlaState? SlaState,
    PersonRef ReportedBy,
    PersonRef? ApprovedBy,
    PersonRef? AssignedTo,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset DueAt,
    string? Resolution,
    string? RejectionReason,
    string? CancellationReason,
    WorkOrderSource Source,
    DateTimeOffset? EscalatedAt,
    Guid? PmScheduleId,
    DateOnly? PmDueOn,
    IReadOnlyList<string> AllowedActions);

internal sealed record ListWorkOrdersQuery(
    WorkOrderStatus? Status,
    WorkOrderPriority? Priority,
    Guid? AssetId,
    bool Mine = false,
    bool Escalated = false,
    int Page = 1,
    int PageSize = 25);

internal sealed record CreateWorkOrderRequest(
    Guid AssetId,
    string Title,
    string? Description,
    WorkOrderPriority Priority,
    bool AssetDown);

internal sealed record ApproveRequest(WorkOrderPriority? Priority);

internal sealed record ReasonRequest(string Reason);

internal sealed record AssignRequest(string TechnicianId);

internal sealed record CompleteRequest(string Resolution);
