using PlantOps.Modules.WorkOrders.Domain;
using PlantOps.SharedKernel;
using static PlantOps.Modules.WorkOrders.Tests.Domain.WorkOrderBuilder;

namespace PlantOps.Modules.WorkOrders.Tests.Domain;

public class WorkOrderEscalationTests
{
    // P2 is an 8 hour target: due at T0 + 8h.
    private static readonly DateTimeOffset Due = T0.AddHours(8);

    // Names, not enum values: the enum is internal and a public test method cannot take it as a parameter.
    public static TheoryData<string> OpenStatuses() => new("Submitted", "Approved", "Assigned", "InProgress");

    public static TheoryData<string> ClosedOutStatuses() => new("Completed", "Closed", "Rejected", "Cancelled");

    [Theory]
    [MemberData(nameof(OpenStatuses))]
    public void An_open_work_order_past_its_deadline_is_escalated_and_raises_the_breach_event(string statusName)
    {
        var w = InStatus(Enum.Parse<WorkOrderStatus>(statusName));
        var now = Due.AddMinutes(1);

        var escalated = w.Escalate(now);

        Assert.True(escalated);
        Assert.Equal(now, w.EscalatedAt);
        var raised = Assert.IsType<WorkOrderSlaBreached>(Assert.Single(w.DomainEvents));
        Assert.Equal(w.Id.Value, raised.WorkOrderId);
        Assert.Equal("Feeder jam", raised.Title);
        Assert.Equal("SMT1-PNP-01", raised.AssetTag);
        Assert.Equal(WorkOrderPriority.P2, raised.Priority);
        Assert.Equal(Due, raised.DueAt);
        Assert.Equal(now, raised.EscalatedAt);
    }

    [Fact]
    public void The_breach_event_names_the_assignee_when_there_is_one()
    {
        var assigned = InStatus(WorkOrderStatus.Assigned);
        var unassigned = InStatus(WorkOrderStatus.Approved);

        assigned.Escalate(Due.AddMinutes(1));
        unassigned.Escalate(Due.AddMinutes(1));

        Assert.Equal("Tom", Assert.IsType<WorkOrderSlaBreached>(Assert.Single(assigned.DomainEvents)).AssignedToName);
        Assert.Null(Assert.IsType<WorkOrderSlaBreached>(Assert.Single(unassigned.DomainEvents)).AssignedToName);
    }

    [Fact]
    public void Exactly_at_the_deadline_is_not_yet_a_breach()
    {
        var w = InStatus(WorkOrderStatus.Approved);

        Assert.Throws<DomainException>(() => w.Escalate(Due));
        Assert.False(w.IsEscalatable(Due));
        Assert.True(w.IsEscalatable(Due.AddTicks(1)));
        Assert.Null(w.EscalatedAt);
        Assert.Empty(w.DomainEvents);
    }

    [Fact]
    public void Before_the_deadline_it_cannot_be_escalated() =>
        Assert.Throws<DomainException>(() => InStatus(WorkOrderStatus.Assigned).Escalate(Due.AddHours(-1)));

    [Theory]
    [MemberData(nameof(ClosedOutStatuses))]
    public void A_work_order_that_is_not_open_cannot_be_escalated(string statusName)
    {
        var w = InStatus(Enum.Parse<WorkOrderStatus>(statusName));
        var now = T0.AddDays(30);

        Assert.False(w.IsEscalatable(now));
        Assert.Throws<DomainException>(() => w.Escalate(now));
        Assert.Null(w.EscalatedAt);
    }

    [Fact]
    public void Escalating_twice_does_nothing_the_second_time()
    {
        var w = InStatus(WorkOrderStatus.InProgress);
        var first = Due.AddMinutes(1);
        Assert.True(w.Escalate(first));
        w.ClearDomainEvents();

        var again = w.Escalate(first.AddHours(1));

        Assert.False(again);
        Assert.Equal(first, w.EscalatedAt);
        Assert.Empty(w.DomainEvents);
        Assert.False(w.IsEscalatable(first.AddHours(1)));
    }

    [Fact]
    public void An_escalated_order_that_is_then_completed_stays_escalated()
    {
        var w = InStatus(WorkOrderStatus.InProgress);
        w.Escalate(Due.AddMinutes(1));

        w.Complete(Tom, Due.AddMinutes(30), "Fixed late.");

        Assert.NotNull(w.EscalatedAt);
        Assert.Equal(SlaState.Missed, w.SlaStateAt(Due.AddHours(1)));
    }

    // ---- preventive work orders ----

    [Fact]
    public void A_preventive_work_order_starts_approved_and_is_raised_by_the_system_with_the_due_date_as_deadline()
    {
        var scheduleId = Guid.NewGuid();
        var dueAt = new DateTimeOffset(2026, 10, 10, 15, 59, 59, TimeSpan.Zero);

        var w = WorkOrder.RaisePreventive(
            Guid.NewGuid(), "RF-02", "Reflow oven", "Clean oven", "Remove flux residue.",
            WorkOrderPriority.P3, scheduleId, new DateOnly(2026, 10, 10), SystemActor.Instance, T0, dueAt);

        Assert.Equal(WorkOrderStatus.Approved, w.Status);
        Assert.Equal(WorkOrderSource.Preventive, w.Source);
        Assert.Equal(scheduleId, w.PmScheduleId);
        Assert.Equal(new DateOnly(2026, 10, 10), w.PmDueOn);
        Assert.Equal(dueAt, w.DueAt);
        Assert.Equal("system", w.ReportedById);
        Assert.Equal("system", w.ApprovedById);
        Assert.Equal(T0, w.ApprovedAt);
        Assert.False(w.AssetDown);
        Assert.Equal("Remove flux residue.", w.Description);
        Assert.Collection(
            w.DomainEvents,
            e => Assert.IsType<WorkOrderSubmitted>(e),
            e => Assert.IsType<WorkOrderApproved>(e));
    }

    [Fact]
    public void A_preventive_work_order_is_judged_against_its_due_date_not_the_priority_target()
    {
        // P3 would be due 24h after raising; the PM deadline is 10 days out.
        var dueAt = T0.AddDays(10);
        var w = WorkOrder.RaisePreventive(
            Guid.NewGuid(), "RF-02", "Reflow oven", "Clean oven", null,
            WorkOrderPriority.P3, Guid.NewGuid(), DateOnly.FromDateTime(dueAt.UtcDateTime), SystemActor.Instance, T0, dueAt);

        Assert.Equal(SlaState.OnTrack, w.SlaStateAt(T0.AddDays(2)));
        Assert.Equal(SlaState.Breached, w.SlaStateAt(dueAt.AddTicks(1)));
        Assert.False(w.IsEscalatable(T0.AddDays(2)));
        Assert.True(w.IsEscalatable(dueAt.AddTicks(1)));
    }

    [Fact]
    public void A_reactive_work_order_has_the_reactive_source_and_no_pm_link()
    {
        var w = Submitted();

        Assert.Equal(WorkOrderSource.Reactive, w.Source);
        Assert.Null(w.PmScheduleId);
        Assert.Null(w.PmDueOn);
        Assert.Null(w.EscalatedAt);
    }
}
