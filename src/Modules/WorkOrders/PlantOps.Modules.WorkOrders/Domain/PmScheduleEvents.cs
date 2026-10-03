using PlantOps.SharedKernel;

namespace PlantOps.Modules.WorkOrders.Domain;

// Audit-only payloads for PM schedules (none becomes an integration event).
internal sealed record PmScheduleCreated(
    Guid PmScheduleId,
    Guid AssetId,
    string AssetTag,
    string Title,
    int IntervalDays,
    int LeadDays,
    WorkOrderPriority Priority,
    DateOnly NextDueOn) : IDomainEvent;

internal sealed record PmScheduleUpdated(
    Guid PmScheduleId,
    string Title,
    int IntervalDays,
    int LeadDays,
    WorkOrderPriority Priority,
    DateOnly NextDueOn) : IDomainEvent;

internal sealed record PmScheduleDeactivated(Guid PmScheduleId) : IDomainEvent;

internal sealed record PmScheduleActivated(Guid PmScheduleId, DateOnly NextDueOn) : IDomainEvent;

internal sealed record PmWorkOrderGenerated(Guid PmScheduleId, Guid WorkOrderId, DateOnly DueOn, DateOnly NextDueOn) : IDomainEvent;

// The schedule moved past an occurrence whose work order already existed: the NextDueOn change must still be auditable.
internal sealed record PmOccurrenceAlreadyGenerated(Guid PmScheduleId, DateOnly DueOn, DateOnly NextDueOn) : IDomainEvent;
