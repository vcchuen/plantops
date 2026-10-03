namespace PlantOps.Modules.WorkOrders.Domain;

internal enum WorkOrderStatus
{
    Submitted,
    Approved,
    Assigned,
    InProgress,
    Completed,
    Closed,
    Rejected,
    Cancelled,
}

/// <summary>P1 is the most urgent. Drives the SLA target (<see cref="SlaPolicy"/>).</summary>
internal enum WorkOrderPriority
{
    P1,
    P2,
    P3,
    P4,
}

internal enum SlaState
{
    OnTrack,
    AtRisk,
    Breached,
    Met,
    Missed,
}
