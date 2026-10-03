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
        // Appended for Reporting (design 07): names, not enums, and the deadline in force at completion.
        Assert.Equal("P2", mapped.Priority);
        Assert.Equal("Reactive", mapped.Source);
        Assert.Equal(w.DueAt, mapped.DueAt);
    }

    [Fact]
    public void A_priority_changed_at_approval_is_the_priority_in_the_completed_event()
    {
        var w = WorkOrderBuilder.Submitted(WorkOrderPriority.P4);
        w.Approve(WorkOrderBuilder.Sam, WorkOrderBuilder.T0.AddMinutes(5), WorkOrderPriority.P1);
        w.Assign(WorkOrderBuilder.Sam, WorkOrderBuilder.Tom, WorkOrderBuilder.T0.AddMinutes(10));
        w.Start(WorkOrderBuilder.Tom, WorkOrderBuilder.T0.AddMinutes(20));
        w.ClearDomainEvents();

        w.Complete(WorkOrderBuilder.Tom, WorkOrderBuilder.T0.AddMinutes(60), "Done.");

        var mapped = Assert.IsType<WorkOrderCompletedIntegrationEvent>(Mapper.Map(Assert.Single(w.DomainEvents)));
        Assert.Equal("P1", mapped.Priority);
        Assert.Equal(WorkOrderBuilder.T0.AddHours(4), mapped.DueAt);
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
    public void Escalating_maps_to_a_self_contained_breach_event()
    {
        var w = WorkOrderBuilder.InStatus(WorkOrderStatus.Assigned, WorkOrderPriority.P1);
        var now = WorkOrderBuilder.T0.AddHours(5);

        w.Escalate(now);

        var mapped = Assert.IsType<WorkOrderSlaBreachedIntegrationEvent>(Mapper.Map(Assert.Single(w.DomainEvents)));
        Assert.Equal(w.Id.Value, mapped.WorkOrderId);
        Assert.Equal(WorkOrder.FormatNumber(w.Number), mapped.Number);
        Assert.Equal("Feeder jam", mapped.Title);
        Assert.Equal("SMT1-PNP-01", mapped.AssetTag);
        Assert.Equal("P1", mapped.Priority);
        Assert.Equal(WorkOrderBuilder.T0.AddHours(4), mapped.DueAt);
        Assert.Equal(now, mapped.EscalatedAt);
        Assert.Equal("Tom", mapped.AssignedToName);
    }

    [Fact]
    public void Generating_a_preventive_work_order_stays_audit_only()
    {
        var w = WorkOrder.RaisePreventive(
            Guid.NewGuid(), "RF-02", "Reflow oven", "Clean oven", null, WorkOrderPriority.P3,
            Guid.NewGuid(), new DateOnly(2026, 10, 10), SystemActor.Instance, WorkOrderBuilder.T0, WorkOrderBuilder.T0.AddDays(7));

        Assert.All(w.DomainEvents, e => Assert.Null(Mapper.Map(e)));
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
