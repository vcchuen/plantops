using PlantOps.Modules.WorkOrders.Domain;

namespace PlantOps.Modules.WorkOrders.Authorization;

// The single place that says which buttons exist. The detail endpoint sends the result as allowedActions and the
// SPA renders exactly those, so the rules are never re-implemented client-side.
internal static class WorkOrderAccess
{
    public const string Approve = "approve";
    public const string Reject = "reject";
    public const string Assign = "assign";
    public const string Start = "start";
    public const string Complete = "complete";
    public const string Close = "close";
    public const string Cancel = "cancel";

    /// <summary>The resource rule behind "Start" and "Complete": the assigned technician, or an admin.</summary>
    public static bool CanWork(string? userId, bool isAdmin, string? assignedToId) =>
        isAdmin || (!string.IsNullOrEmpty(userId) && userId == assignedToId);

    /// <param name="canSupervise">Holds the supervise policy (supervisor or admin).</param>
    /// <param name="canWork">Passes <see cref="CanWork"/> for this work order.</param>
    public static IReadOnlyList<string> AllowedActions(WorkOrderStatus status, bool canSupervise, bool canWork)
    {
        var actions = new List<string>();
        switch (status)
        {
            case WorkOrderStatus.Submitted when canSupervise:
                actions.AddRange([Approve, Reject, Cancel]);
                break;
            case WorkOrderStatus.Approved when canSupervise:
                actions.AddRange([Assign, Cancel]);
                break;
            case WorkOrderStatus.Assigned:
                // Reassign is supervisory; starting is the technician's.
                if (canWork)
                {
                    actions.Add(Start);
                }

                if (canSupervise)
                {
                    actions.AddRange([Assign, Cancel]);
                }

                break;
            case WorkOrderStatus.InProgress when canWork:
                actions.Add(Complete);
                break;
            case WorkOrderStatus.Completed when canSupervise:
                actions.Add(Close);
                break;
        }

        return actions;
    }
}
