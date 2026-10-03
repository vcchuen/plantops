using System.Text.RegularExpressions;
using PlantOps.Api.Seeding;

namespace PlantOps.Api.Tests;

// No database: the plan is plain data, so the invariants the seeder (and the E2E journeys) rely on are checked here.
public partial class DemoSeedPlanTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 9, 30, 0, TimeSpan.Zero);

    private static DemoSeedPlan Plan() => DemoSeedPlan.Generate(Now);

    private static TimeSpan Target(string priority) => priority switch
    {
        "P1" => TimeSpan.FromHours(4),
        "P2" => TimeSpan.FromHours(8),
        "P3" => TimeSpan.FromHours(24),
        _ => TimeSpan.FromHours(72),
    };

    // The same rule as the domain: reactive orders are due at submission + target; preventive ones at the end of the due date.
    private static DateTimeOffset DueAt(SeedWorkOrder w) => w.PmDueOn is { } due
        ? new DateTimeOffset(due.ToDateTime(TimeOnly.MaxValue), DemoSeedPlan.FactoryOffset).ToUniversalTime()
        : w.SubmittedAt + Target(w.Priority);

    [Fact]
    public void The_same_clock_and_seed_give_an_identical_plan()
    {
        var first = Plan();
        var second = Plan();

        Assert.Equal(first.Assets, second.Assets);
        Assert.Equal(first.Parts, second.Parts);
        Assert.Equal(first.Schedules.Count, second.Schedules.Count);
        for (var i = 0; i < first.Schedules.Count; i++)
        {
            Assert.Equal(first.Schedules[i].PastDueDates, second.Schedules[i].PastDueDates);
            Assert.Equal(first.Schedules[i] with { PastDueDates = [] }, second.Schedules[i] with { PastDueDates = [] });
        }

        Assert.Equal(first.WorkOrders.Count, second.WorkOrders.Count);
        for (var i = 0; i < first.WorkOrders.Count; i++)
        {
            Assert.Equal(first.WorkOrders[i] with { Parts = [] }, second.WorkOrders[i] with { Parts = [] });
            Assert.Equal(first.WorkOrders[i].Parts, second.WorkOrders[i].Parts);
        }
    }

    [Fact]
    public void A_different_seed_gives_a_different_history()
    {
        var other = DemoSeedPlan.Generate(Now, seed: 7);

        Assert.NotEqual(Plan().WorkOrders.Select(w => w.SubmittedAt), other.WorkOrders.Select(w => w.SubmittedAt));
    }

    [Fact]
    public void The_dataset_has_the_promised_size()
    {
        var plan = Plan();

        Assert.Equal(4, plan.Users.Count);
        Assert.InRange(plan.Assets.Count, 28, 34);
        Assert.Equal(25, plan.Parts.Count);
        Assert.Equal(8, plan.Schedules.Count);
        Assert.Equal(80, plan.WorkOrders.Count);
        Assert.Equal(16, plan.WorkOrders.Count(w => w.IsPreventive));
        Assert.Equal(["SMT-1", "SMT-2", "FA-1", "TEST-1"], plan.Assets.Select(a => a.LineCode).Distinct());
    }

    [Fact]
    public void Every_status_is_represented_and_a_supervisor_has_an_approved_unassigned_order_to_act_on()
    {
        var plan = Plan();

        foreach (var status in Enum.GetValues<SeedWorkOrderStatus>())
        {
            Assert.Contains(plan.WorkOrders, w => w.Status == status);
        }

        var approved = plan.WorkOrders.Where(w => w.Status == SeedWorkOrderStatus.Approved).ToList();
        Assert.NotEmpty(approved);
        Assert.All(approved, w => Assert.Null(w.AssignedAt));
        Assert.All(approved, w => Assert.False(w.IsPreventive));
    }

    [Fact]
    public void Assets_have_unique_valid_tags_and_the_in_service_showcase_asset_exists()
    {
        var plan = Plan();

        Assert.Equal(plan.Assets.Count, plan.Assets.Select(a => a.Tag).Distinct().Count());
        Assert.All(plan.Assets, a =>
        {
            Assert.Matches(TagPattern(), a.Tag);
            Assert.InRange(a.Tag.Length, 3, 20);
            Assert.InRange(a.Name.Length, 1, 100);
            Assert.InRange(a.Model.Length, 1, 100);
            Assert.InRange(a.Station.Length, 1, 50);
            Assert.Contains(a.Criticality, new[] { "A", "B", "C" });
            Assert.True(a.CommissionedOn < DateOnly.FromDateTime(Now.UtcDateTime));
        });

        var pnp = Assert.Single(plan.Assets, a => a.Tag == "SMT1-PNP-01");
        Assert.Equal("Fuji NXT III pick-and-place", pnp.Name);
        Assert.Equal("SMT-1", pnp.LineCode);
    }

    [Fact]
    public void Spare_parts_are_unique_and_the_nozzle_the_e2e_journey_reserves_has_plenty_free()
    {
        var plan = Plan();

        Assert.Equal(plan.Parts.Count, plan.Parts.Select(p => p.PartNumber).Distinct().Count());
        var nozzle = Assert.Single(plan.Parts, p => p.PartNumber == "NZL-CN040");
        Assert.Equal("CN040 nozzle", nozzle.Name);
        Assert.Equal("pcs", nozzle.Unit);
        Assert.True(nozzle.InitialOnHand >= 20);

        // What the seeder leaves on the shelf: initial stock minus what history consumed (held stock stays on hand).
        foreach (var part in plan.Parts)
        {
            var consumed = plan.WorkOrders.Where(w => w.IsDone).SelectMany(w => w.Parts).Where(u => u.PartNumber == part.PartNumber).Sum(u => u.Quantity);
            var held = plan.WorkOrders.Where(w => w.Status == SeedWorkOrderStatus.InProgress).SelectMany(w => w.Parts).Where(u => u.PartNumber == part.PartNumber).Sum(u => u.Quantity);
            var onHand = part.InitialOnHand - consumed;

            Assert.True(onHand >= held, $"{part.PartNumber}: on hand {onHand} cannot cover the {held} held for open work");
            if (part.PartNumber == DemoSeedPlan.KeyPartNumber)
            {
                Assert.True(onHand >= DemoSeedPlan.KeyPartMinimumAvailable, $"on hand {onHand}");
                Assert.True(onHand - held >= DemoSeedPlan.KeyPartMinimumAvailable, $"available {onHand - held}");
            }
        }

        // A few parts are low on stock so the demo shows the reorder warning.
        Assert.True(plan.Parts.Count(p => PlanAvailable(plan, p) <= p.ReorderLevel) >= 2);
    }

    [Fact]
    public void Every_reference_resolves_and_every_timeline_runs_forwards_and_ends_in_the_past()
    {
        var plan = Plan();
        var userIds = plan.Users.Select(u => u.Id).ToHashSet();
        var tags = plan.Assets.Select(a => a.Tag).ToHashSet();
        var parts = plan.Parts.Select(p => p.PartNumber).ToHashSet();

        Assert.Equal(["11111111-1111-4111-8111-111111111111", "22222222-2222-4222-8222-222222222222", "33333333-3333-4333-8333-333333333333", "44444444-4444-4444-8444-444444444444"], plan.Users.Select(u => u.Id));
        Assert.All(plan.Schedules, s => Assert.Contains(s.AssetTag, tags));
        Assert.All(plan.WorkOrders, w =>
        {
            Assert.Contains(w.AssetTag, tags);
            Assert.Contains(w.SupervisorId, userIds);
            Assert.Contains(w.TechnicianId, userIds);
            Assert.True(w.IsPreventive || userIds.Contains(w.ReporterId));
            Assert.All(w.Parts, p => Assert.Contains(p.PartNumber, parts));
            Assert.InRange(w.Title.Length, 1, 200);
            Assert.InRange(w.Description.Length, 1, 2000);

            var stages = new[] { w.SubmittedAt, w.ApprovedAt, w.AssignedAt, w.StartedAt, w.CompletedAt, w.ClosedAt }.Where(t => t is not null).Select(t => t!.Value).ToList();
            Assert.Equal(stages.Order(), stages);
            Assert.All(stages, t => Assert.True(t <= Now, $"WO {w.Key} has a stage after now"));
            Assert.True(w.EndedAt is null || (w.EndedAt <= Now && w.EndedAt >= stages[^1]));
            Assert.All(w.Parts, p => Assert.InRange(p.At, w.StartedAt!.Value, w.CompletedAt ?? Now));
        });
    }

    [Fact]
    public void Each_status_carries_exactly_the_facts_the_domain_needs_to_reach_it()
    {
        foreach (var w in Plan().WorkOrders)
        {
            switch (w.Status)
            {
                case SeedWorkOrderStatus.Submitted:
                    Assert.Null(w.ApprovedAt);
                    break;
                case SeedWorkOrderStatus.Approved:
                    Assert.NotNull(w.ApprovedAt);
                    Assert.Null(w.AssignedAt);
                    break;
                case SeedWorkOrderStatus.Assigned:
                    Assert.NotNull(w.AssignedAt);
                    Assert.Null(w.StartedAt);
                    break;
                case SeedWorkOrderStatus.InProgress:
                    Assert.NotNull(w.StartedAt);
                    Assert.Null(w.CompletedAt);
                    break;
                case SeedWorkOrderStatus.Completed:
                    Assert.NotNull(w.CompletedAt);
                    Assert.NotNull(w.Resolution);
                    Assert.Null(w.ClosedAt);
                    break;
                case SeedWorkOrderStatus.Closed:
                    Assert.NotNull(w.ClosedAt);
                    Assert.NotNull(w.Resolution);
                    break;
                case SeedWorkOrderStatus.Rejected:
                    Assert.NotNull(w.EndedAt);
                    Assert.NotNull(w.Reason);
                    Assert.Null(w.ApprovedAt);
                    break;
                case SeedWorkOrderStatus.Cancelled:
                    Assert.NotNull(w.EndedAt);
                    Assert.NotNull(w.Reason);
                    Assert.NotNull(w.ApprovedAt);
                    Assert.Null(w.StartedAt);
                    break;
            }
        }
    }

    [Fact]
    public void Preventive_orders_have_one_per_schedule_occurrence_and_the_schedule_moves_on_after_them()
    {
        var plan = Plan();
        var today = DateOnly.FromDateTime((Now + DemoSeedPlan.FactoryOffset).DateTime);

        var occurrences = plan.WorkOrders.Where(w => w.IsPreventive).Select(w => (w.PmScheduleIndex, w.PmDueOn)).ToList();
        Assert.Equal(occurrences.Count, occurrences.Distinct().Count());

        for (var i = 0; i < plan.Schedules.Count; i++)
        {
            var schedule = plan.Schedules[i];
            Assert.InRange(schedule.IntervalDays, 1, 365);
            Assert.InRange(schedule.LeadDays, 0, 30);
            Assert.True(schedule.PastDueDates.All(d => d < today));
            Assert.Equal(schedule.PastDueDates.Max().AddDays(schedule.IntervalDays), schedule.NextDueOn);
            // Not due yet, so the PM runner does not immediately raise a duplicate of work already in the history.
            Assert.True(today < schedule.NextDueOn.AddDays(-schedule.LeadDays));
            Assert.Equal(schedule.PastDueDates.Count, occurrences.Count(o => o.PmScheduleIndex == i));
        }
    }

    [Fact]
    public void History_includes_missed_deadlines_open_breaches_planned_work_and_downtime_completions()
    {
        var plan = Plan();

        var missed = plan.WorkOrders.Where(w => w.IsDone && w.CompletedAt > DueAt(w)).ToList();
        var met = plan.WorkOrders.Where(w => w.IsDone && w.CompletedAt <= DueAt(w)).ToList();
        var openBreached = plan.WorkOrders.Where(w => w.Status is SeedWorkOrderStatus.Submitted or SeedWorkOrderStatus.Approved or SeedWorkOrderStatus.Assigned or SeedWorkOrderStatus.InProgress)
            .Where(w => Now > DueAt(w))
            .ToList();

        Assert.True(missed.Count >= 3, $"missed {missed.Count}");
        Assert.True(met.Count >= 20, $"met {met.Count}");
        Assert.True(openBreached.Count >= 2, $"open and breached {openBreached.Count}");
        Assert.True(plan.WorkOrders.Count(w => w.IsDone && w.AssetDown) >= 3);
        Assert.Contains(plan.WorkOrders, w => w.IsPreventive && w.IsDone);
        // Raised across the whole six months, not bunched at the end.
        Assert.True(plan.WorkOrders.Min(w => w.SubmittedAt) < Now.AddDays(-120));
    }

    private static int PlanAvailable(DemoSeedPlan plan, SeedPart part)
    {
        var consumed = plan.WorkOrders.Where(w => w.IsDone).SelectMany(w => w.Parts).Where(u => u.PartNumber == part.PartNumber).Sum(u => u.Quantity);
        var held = plan.WorkOrders.Where(w => w.Status == SeedWorkOrderStatus.InProgress).SelectMany(w => w.Parts).Where(u => u.PartNumber == part.PartNumber).Sum(u => u.Quantity);
        return part.InitialOnHand - consumed - held;
    }

    [GeneratedRegex("^[A-Z0-9]+(-[A-Z0-9]+)*$")]
    private static partial Regex TagPattern();
}
