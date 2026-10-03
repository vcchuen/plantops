namespace PlantOps.Modules.WorkOrders.Domain;

internal readonly record struct WorkOrderId(Guid Value)
{
    public static WorkOrderId New() => new(Guid.CreateVersion7());
}
