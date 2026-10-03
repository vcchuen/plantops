using PlantOps.Modules.WorkOrders.Domain;
using static PlantOps.Modules.WorkOrders.Tests.Domain.WorkOrderBuilder;

namespace PlantOps.Modules.WorkOrders.Tests.Domain;

public class SlaPolicyTests
{
    [Theory]
    [InlineData("P1", 4)]
    [InlineData("P2", 8)]
    [InlineData("P3", 24)]
    [InlineData("P4", 72)]
    public void Target_by_priority(string priorityName, int hours)
    {
        var priority = Enum.Parse<WorkOrderPriority>(priorityName);
        var policy = SlaPolicy.For(priority);

        Assert.Equal(TimeSpan.FromHours(hours), policy.Target);
        Assert.Equal(T0.AddHours(hours), policy.DueAt(T0));
    }

    [Fact]
    public void Unknown_priority_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SlaPolicy.For((WorkOrderPriority)42));
    }

    // P1 = 4 h, so 25 % of the target is exactly 60 minutes.
    private static SlaState Open(TimeSpan afterSubmit) =>
        SlaPolicy.For(WorkOrderPriority.P1).Evaluate(T0, completedAt: null, now: T0 + afterSubmit);

    [Theory]
    [InlineData(0)]
    [InlineData(60)]
    [InlineData(179)]
    public void More_than_a_quarter_remaining_is_on_track(int minutesAfterSubmit)
    {
        Assert.Equal(SlaState.OnTrack, Open(TimeSpan.FromMinutes(minutesAfterSubmit)));
    }

    [Fact]
    public void Exactly_a_quarter_remaining_is_at_risk()
    {
        // 180 min elapsed of 240 leaves exactly 60 min = 25 %.
        Assert.Equal(SlaState.AtRisk, Open(TimeSpan.FromMinutes(180)));
    }

    [Fact]
    public void One_tick_before_the_quarter_mark_is_still_on_track()
    {
        Assert.Equal(SlaState.OnTrack, Open(TimeSpan.FromMinutes(180) - TimeSpan.FromTicks(1)));
    }

    [Fact]
    public void One_tick_after_the_quarter_mark_is_at_risk()
    {
        Assert.Equal(SlaState.AtRisk, Open(TimeSpan.FromMinutes(180) + TimeSpan.FromTicks(1)));
    }

    [Fact]
    public void Exactly_at_the_deadline_is_at_risk_not_breached()
    {
        Assert.Equal(SlaState.AtRisk, Open(TimeSpan.FromHours(4)));
    }

    [Fact]
    public void One_tick_past_the_deadline_is_breached()
    {
        Assert.Equal(SlaState.Breached, Open(TimeSpan.FromHours(4) + TimeSpan.FromTicks(1)));
    }

    [Fact]
    public void Long_overdue_is_breached()
    {
        Assert.Equal(SlaState.Breached, Open(TimeSpan.FromDays(10)));
    }

    [Fact]
    public void Completed_before_the_deadline_is_met()
    {
        var state = SlaPolicy.For(WorkOrderPriority.P1).Evaluate(T0, T0.AddHours(3), T0.AddDays(5));

        Assert.Equal(SlaState.Met, state);
    }

    [Fact]
    public void Completed_exactly_at_the_deadline_is_met()
    {
        var state = SlaPolicy.For(WorkOrderPriority.P1).Evaluate(T0, T0.AddHours(4), T0.AddDays(5));

        Assert.Equal(SlaState.Met, state);
    }

    [Fact]
    public void Completed_one_tick_after_the_deadline_is_missed()
    {
        var state = SlaPolicy.For(WorkOrderPriority.P1).Evaluate(T0, T0.AddHours(4).AddTicks(1), T0.AddDays(5));

        Assert.Equal(SlaState.Missed, state);
    }

    [Fact]
    public void Quarter_boundary_scales_with_the_target()
    {
        // P4 = 72 h: 25 % is 18 h, so the at-risk window opens at 54 h.
        var p4 = SlaPolicy.For(WorkOrderPriority.P4);

        Assert.Equal(SlaState.OnTrack, p4.Evaluate(T0, null, T0.AddHours(53)));
        Assert.Equal(SlaState.AtRisk, p4.Evaluate(T0, null, T0.AddHours(54)));
    }
}
