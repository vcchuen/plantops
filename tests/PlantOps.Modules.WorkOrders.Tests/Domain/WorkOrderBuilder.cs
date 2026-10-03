using PlantOps.Modules.WorkOrders.Domain;
using PlantOps.SharedKernel;

namespace PlantOps.Modules.WorkOrders.Tests.Domain;

// Walks a real aggregate through its legal transitions to reach a status, so tests never poke private state.
internal static class WorkOrderBuilder
{
    public static readonly DateTimeOffset T0 = new(2026, 10, 3, 8, 0, 0, TimeSpan.Zero);

    public static readonly Actor Oscar = new("oscar", "Oscar");
    public static readonly Actor Sam = new("sam", "Sam");
    public static readonly Actor Tom = new("tom", "Tom");
    public static readonly Actor Lee = new("lee", "Lee");

    public static WorkOrder Submitted(WorkOrderPriority priority = WorkOrderPriority.P2) => WorkOrder.Submit(
        Guid.NewGuid(),
        "SMT1-PNP-01",
        "Pick and place",
        "Feeder jam",
        "Feeder 4 jams every few minutes.",
        priority,
        assetDown: true,
        Oscar,
        T0);

    public static WorkOrder InStatus(WorkOrderStatus status, WorkOrderPriority priority = WorkOrderPriority.P2)
    {
        var w = Submitted(priority);
        switch (status)
        {
            case WorkOrderStatus.Submitted:
                break;
            case WorkOrderStatus.Rejected:
                w.Reject(Sam, T0.AddMinutes(5), "Duplicate of WO-000001");
                break;
            case WorkOrderStatus.Cancelled:
                w.Cancel(Sam, T0.AddMinutes(5), "Reported by mistake");
                break;
            default:
                w.Approve(Sam, T0.AddMinutes(5));
                if (status >= WorkOrderStatus.Assigned)
                {
                    w.Assign(Sam, Tom, T0.AddMinutes(10));
                }

                if (status >= WorkOrderStatus.InProgress)
                {
                    w.Start(Tom, T0.AddMinutes(20));
                }

                if (status >= WorkOrderStatus.Completed)
                {
                    w.Complete(Tom, T0.AddMinutes(60), "Replaced the feeder spring.");
                }

                if (status >= WorkOrderStatus.Closed)
                {
                    w.Close(Sam, T0.AddMinutes(90));
                }

                break;
        }

        w.ClearDomainEvents();
        return w;
    }
}
