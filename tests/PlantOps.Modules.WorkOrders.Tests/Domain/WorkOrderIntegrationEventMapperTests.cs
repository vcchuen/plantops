using PlantOps.Modules.WorkOrders.Contracts;
using PlantOps.Modules.WorkOrders.Domain;
using PlantOps.Modules.WorkOrders.Infrastructure;
using PlantOps.Modules.WorkOrders.Integration;

namespace PlantOps.Modules.WorkOrders.Tests.Domain;

public class WorkOrderIntegrationEventMapperTests
{
    private static readonly WorkOrderIntegrationEventMapper Mapper = new();

    [Fact]
    public void The_mapper_serves_the_work_orders_context() =>
        Assert.Equal(typeof(WorkOrdersDbContext), Mapper.ContextType);

    [Fact]
    public void Completing_maps_to_a_self_contained_completed_event()
    {
        var w = WorkOrderBuilder.InStatus(WorkOrderStatus.InProgress);

        w.Complete(WorkOrderBuilder.Tom, WorkOrderBuilder.T0.AddMinutes(90), "Replaced the feeder spring.");

        var mapped = Assert.IsType<WorkOrderCompletedIntegrationEvent>(Mapper.Map(Assert.Single(w.DomainEvents)));
        Assert.Equal(w.Id.Value, mapped.WorkOrderId);
        Assert.Equal(WorkOrder.FormatNumber(w.Number), mapped.Number);
        Assert.Equal(w.AssetId, mapped.AssetId);
        Assert.Equal("Feeder jam", mapped.Title);
        Assert.Equal("Replaced the feeder spring.", mapped.Resolution);
        Assert.Equal(WorkOrderBuilder.T0, mapped.SubmittedAt);
        Assert.Equal(WorkOrderBuilder.T0.AddMinutes(20), mapped.StartedAt);
        Assert.Equal(WorkOrderBuilder.T0.AddMinutes(90), mapped.CompletedAt);
        Assert.Equal("Tom", mapped.TechnicianName);
        Assert.True(mapped.AssetDown);
    }

    [Fact]
    public void The_technician_is_the_assignee_even_when_someone_else_completes()
    {
        var w = WorkOrderBuilder.InStatus(WorkOrderStatus.InProgress);

        w.Complete(WorkOrderBuilder.Sam, WorkOrderBuilder.T0.AddMinutes(90), "Done on Tom's behalf.");

        var mapped = Assert.IsType<WorkOrderCompletedIntegrationEvent>(Mapper.Map(Assert.Single(w.DomainEvents)));
        Assert.Equal("Tom", mapped.TechnicianName);
    }

    [Fact]
    public void Cancelling_maps_to_a_cancelled_event()
    {
        var w = WorkOrderBuilder.InStatus(WorkOrderStatus.Assigned);

        w.Cancel(WorkOrderBuilder.Sam, WorkOrderBuilder.T0.AddMinutes(30), "Machine scrapped");

        var mapped = Assert.IsType<WorkOrderCancelledIntegrationEvent>(Mapper.Map(Assert.Single(w.DomainEvents)));
        Assert.Equal(w.Id.Value, mapped.WorkOrderId);
        Assert.Equal(WorkOrder.FormatNumber(w.Number), mapped.Number);
        Assert.Equal("Machine scrapped", mapped.Reason);
        Assert.Equal(WorkOrderBuilder.T0.AddMinutes(30), mapped.CancelledAt);
    }

    [Fact]
    public void Every_other_domain_event_stays_audit_only()
    {
        var w = WorkOrderBuilder.Submitted();
        w.Approve(WorkOrderBuilder.Sam, WorkOrderBuilder.T0.AddMinutes(5));
        w.Assign(WorkOrderBuilder.Sam, WorkOrderBuilder.Tom, WorkOrderBuilder.T0.AddMinutes(10));
        w.Start(WorkOrderBuilder.Tom, WorkOrderBuilder.T0.AddMinutes(20));
        w.Complete(WorkOrderBuilder.Tom, WorkOrderBuilder.T0.AddMinutes(60), "Done.");
        w.Close(WorkOrderBuilder.Sam, WorkOrderBuilder.T0.AddMinutes(90));

        var mapped = w.DomainEvents.Select(Mapper.Map).Where(m => m is not null).ToList();

        // Of Submitted, Approved, Assigned, Started, Completed, Closed only Completed crosses the module boundary.
        Assert.IsType<WorkOrderCompletedIntegrationEvent>(Assert.Single(mapped));
    }
}
