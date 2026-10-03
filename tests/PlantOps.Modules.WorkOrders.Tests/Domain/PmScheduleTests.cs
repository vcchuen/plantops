using PlantOps.Modules.WorkOrders.Domain;
using PlantOps.SharedKernel;

namespace PlantOps.Modules.WorkOrders.Tests.Domain;

public class PmScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 0, 30, 0, TimeSpan.Zero);

    private static readonly DateOnly Today = new(2026, 10, 3);

    // The deadline is the end of the due day; the tests use a fixed +8h shape so the value is easy to recognise.
    private static DateTimeOffset DueAtOf(DateOnly date) => new DateTimeOffset(date.ToDateTime(new TimeOnly(23, 59, 59)), TimeSpan.FromHours(8)).ToUniversalTime();

    private static PmSchedule Schedule(
        int intervalDays = 30,
        int leadDays = 3,
        DateOnly? nextDueOn = null,
        WorkOrderPriority priority = WorkOrderPriority.P3)
    {
        var s = PmSchedule.Create(
            Guid.NewGuid(), "RF-02", "Reflow oven", "Clean oven", "Remove flux residue.",
            intervalDays, leadDays, priority, nextDueOn ?? new DateOnly(2026, 10, 10));
        s.ClearDomainEvents();
        return s;
    }

    // ---- create and validate ----

    [Fact]
    public void Create_makes_an_active_schedule_and_raises_the_created_event()
    {
        var assetId = Guid.NewGuid();

        var s = PmSchedule.Create(assetId, " RF-02 ", "Reflow oven", "  Clean oven ", " Step 1 ", 30, 3, WorkOrderPriority.P3, new DateOnly(2026, 10, 10));

        Assert.True(s.IsActive);
        Assert.Equal(assetId, s.AssetId);
        Assert.Equal("RF-02", s.AssetTag);
        Assert.Equal("Clean oven", s.Title);
        Assert.Equal("Step 1", s.Instructions);
        var created = Assert.IsType<PmScheduleCreated>(Assert.Single(s.DomainEvents));
        Assert.Equal(s.Id.Value, created.PmScheduleId);
        Assert.Equal(new DateOnly(2026, 10, 10), created.NextDueOn);
    }

    [Fact]
    public void Blank_instructions_collapse_to_empty() =>
        Assert.Equal(string.Empty, PmSchedule.Create(Guid.NewGuid(), "RF-02", "Oven", "Clean", "   ", 7, 0, WorkOrderPriority.P4, Today).Instructions);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(366)]
    public void Interval_outside_1_to_365_days_is_rejected(int interval) =>
        Assert.Throws<DomainException>(() => Schedule(intervalDays: interval));

    [Theory]
    [InlineData(1)]
    [InlineData(365)]
    public void Interval_at_the_limits_is_accepted(int interval) => Assert.Equal(interval, Schedule(intervalDays: interval, leadDays: 0).IntervalDays);

    [Theory]
    [InlineData(-1)]
    [InlineData(31)]
    public void Lead_time_outside_0_to_30_days_is_rejected(int lead) =>
        Assert.Throws<DomainException>(() => Schedule(leadDays: lead));

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    public void Lead_time_at_the_limits_is_accepted(int lead) => Assert.Equal(lead, Schedule(leadDays: lead).LeadDays);

    [Fact]
    public void A_blank_or_overlong_title_is_rejected()
    {
        Assert.Throws<DomainException>(() => PmSchedule.Create(Guid.NewGuid(), "RF-02", "Oven", " ", null, 7, 0, WorkOrderPriority.P4, Today));
        Assert.Throws<DomainException>(() => PmSchedule.Create(Guid.NewGuid(), "RF-02", "Oven", new string('x', PmSchedule.TitleMaxLength + 1), null, 7, 0, WorkOrderPriority.P4, Today));
        Assert.Throws<DomainException>(() => PmSchedule.Create(Guid.NewGuid(), "RF-02", "Oven", "Clean", new string('x', PmSchedule.InstructionsMaxLength + 1), 7, 0, WorkOrderPriority.P4, Today));
    }

    [Fact]
    public void An_undefined_priority_and_a_missing_due_date_are_rejected()
    {
        Assert.Throws<DomainException>(() => Schedule(priority: (WorkOrderPriority)99));
        Assert.Throws<DomainException>(() => Schedule(nextDueOn: default(DateOnly)));
    }

    [Fact]
    public void Update_replaces_the_editable_fields_and_raises_the_updated_event()
    {
        var s = Schedule();

        s.Update("Deep clean", "New steps", 14, 2, WorkOrderPriority.P2, new DateOnly(2026, 11, 1));

        Assert.Equal("Deep clean", s.Title);
        Assert.Equal(14, s.IntervalDays);
        Assert.Equal(2, s.LeadDays);
        Assert.Equal(WorkOrderPriority.P2, s.Priority);
        Assert.Equal(new DateOnly(2026, 11, 1), s.NextDueOn);
        Assert.IsType<PmScheduleUpdated>(Assert.Single(s.DomainEvents));
    }

    [Fact]
    public void A_rejected_update_changes_nothing()
    {
        var s = Schedule();

        Assert.Throws<DomainException>(() => s.Update("Deep clean", null, 0, 2, WorkOrderPriority.P2, new DateOnly(2026, 11, 1)));

        Assert.Equal("Clean oven", s.Title);
        Assert.Equal(30, s.IntervalDays);
        Assert.Empty(s.DomainEvents);
    }

    [Fact]
    public void Deactivate_and_activate_toggle_and_each_raises_an_event_and_repeating_is_rejected()
    {
        var s = Schedule();

        s.Deactivate();
        Assert.False(s.IsActive);
        Assert.IsType<PmScheduleDeactivated>(Assert.Single(s.DomainEvents));
        Assert.Throws<DomainException>(s.Deactivate);

        s.ClearDomainEvents();
        s.Activate();
        Assert.True(s.IsActive);
        Assert.IsType<PmScheduleActivated>(Assert.Single(s.DomainEvents));
        Assert.Throws<DomainException>(s.Activate);
    }

    // ---- generation ----

    [Fact]
    public void Before_the_lead_window_nothing_is_generated()
    {
        // Due 10 Oct, lead 3 days: the window opens on 7 Oct, so 6 Oct is too early.
        var s = Schedule(nextDueOn: new DateOnly(2026, 10, 10));

        var w = s.GenerateIfDue(new DateOnly(2026, 10, 6), Now, DueAtOf);

        Assert.Null(w);
        Assert.Equal(new DateOnly(2026, 10, 10), s.NextDueOn);
        Assert.Empty(s.DomainEvents);
    }

    [Fact]
    public void On_the_first_day_of_the_lead_window_a_work_order_is_generated_for_the_due_date_and_the_schedule_advances()
    {
        var s = Schedule(intervalDays: 30, leadDays: 3, nextDueOn: new DateOnly(2026, 10, 10));

        var w = s.GenerateIfDue(new DateOnly(2026, 10, 7), Now, DueAtOf);

        Assert.NotNull(w);
        Assert.Equal(WorkOrderSource.Preventive, w.Source);
        Assert.Equal(WorkOrderStatus.Approved, w.Status);
        Assert.Equal(s.Id.Value, w.PmScheduleId);
        Assert.Equal(new DateOnly(2026, 10, 10), w.PmDueOn);
        Assert.Equal(DueAtOf(new DateOnly(2026, 10, 10)), w.DueAt);
        Assert.Equal("Clean oven", w.Title);
        Assert.Equal(WorkOrderPriority.P3, w.Priority);
        Assert.Equal("system", w.ReportedById);
        Assert.Equal(new DateOnly(2026, 11, 9), s.NextDueOn);
        var generated = Assert.IsType<PmWorkOrderGenerated>(Assert.Single(s.DomainEvents));
        Assert.Equal(w.Id.Value, generated.WorkOrderId);
        Assert.Equal(new DateOnly(2026, 10, 10), generated.DueOn);
        Assert.Equal(new DateOnly(2026, 11, 9), generated.NextDueOn);
    }

    [Fact]
    public void A_second_run_the_same_day_generates_nothing()
    {
        var s = Schedule(nextDueOn: new DateOnly(2026, 10, 10));
        Assert.NotNull(s.GenerateIfDue(new DateOnly(2026, 10, 8), Now, DueAtOf));

        Assert.Null(s.GenerateIfDue(new DateOnly(2026, 10, 8), Now, DueAtOf));
    }

    [Fact]
    public void After_missing_three_intervals_it_generates_one_work_order_for_the_oldest_due_date_and_advances_past_today()
    {
        // Every 30 days, lead 3. The job was down since the 10 Oct occurrence; it is now 12 Jan 2027 (4 occurrences later).
        var s = Schedule(intervalDays: 30, leadDays: 3, nextDueOn: new DateOnly(2026, 10, 10));
        var today = new DateOnly(2027, 1, 12);

        var w = s.GenerateIfDue(today, Now, DueAtOf);

        Assert.NotNull(w);
        Assert.Equal(new DateOnly(2026, 10, 10), w.PmDueOn); // the oldest missed one
        Assert.True(s.NextDueOn.AddDays(-s.LeadDays) > today, "the next occurrence's lead window must start after today");
        Assert.True(s.NextDueOn > today);
        Assert.Equal(0, (s.NextDueOn.DayNumber - new DateOnly(2026, 10, 10).DayNumber) % 30); // still on the 30-day grid

        // Only one was generated, so the same day finds nothing more.
        Assert.Null(s.GenerateIfDue(today, Now, DueAtOf));
    }

    [Fact]
    public void When_the_interval_is_shorter_than_the_lead_window_the_advance_still_ends_after_the_window()
    {
        var s = Schedule(intervalDays: 7, leadDays: 14, nextDueOn: Today);

        var w = s.GenerateIfDue(Today, Now, DueAtOf);

        Assert.NotNull(w);
        Assert.True(s.NextDueOn.AddDays(-s.LeadDays) > Today);
        Assert.Null(s.GenerateIfDue(Today, Now, DueAtOf));
    }

    [Fact]
    public void An_inactive_schedule_generates_nothing_however_overdue()
    {
        var s = Schedule(nextDueOn: new DateOnly(2026, 1, 1));
        s.Deactivate();
        s.ClearDomainEvents();

        Assert.False(s.IsDue(Today));
        Assert.Null(s.GenerateIfDue(Today, Now, DueAtOf));
        Assert.Equal(new DateOnly(2026, 1, 1), s.NextDueOn);
        Assert.Empty(s.DomainEvents);
    }

    [Fact]
    public void Zero_lead_days_means_due_on_the_day()
    {
        var s = Schedule(leadDays: 0, nextDueOn: new DateOnly(2026, 10, 10));

        Assert.False(s.IsDue(new DateOnly(2026, 10, 9)));
        Assert.True(s.IsDue(new DateOnly(2026, 10, 10)));
    }

    [Fact]
    public void Skipping_an_already_generated_occurrence_advances_without_a_work_order_and_is_audited()
    {
        var s = Schedule(intervalDays: 30, leadDays: 3, nextDueOn: new DateOnly(2026, 10, 10));

        s.SkipAlreadyGenerated(new DateOnly(2026, 10, 8));

        Assert.Equal(new DateOnly(2026, 11, 9), s.NextDueOn);
        var skipped = Assert.IsType<PmOccurrenceAlreadyGenerated>(Assert.Single(s.DomainEvents));
        Assert.Equal(new DateOnly(2026, 10, 10), skipped.DueOn);
    }

    [Fact]
    public void Skipping_when_not_due_does_nothing()
    {
        var s = Schedule(nextDueOn: new DateOnly(2026, 10, 10));

        s.SkipAlreadyGenerated(new DateOnly(2026, 10, 1));

        Assert.Equal(new DateOnly(2026, 10, 10), s.NextDueOn);
        Assert.Empty(s.DomainEvents);
    }
}
