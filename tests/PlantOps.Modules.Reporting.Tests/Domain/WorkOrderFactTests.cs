using PlantOps.BuildingBlocks.Infrastructure;
using PlantOps.Modules.Reporting.Domain;

namespace PlantOps.Modules.Reporting.Tests.Domain;

public class WorkOrderFactTests
{
    private static readonly TimeZoneInfo Penang = FactoryClock.FindZone(null);

    private static readonly DateTimeOffset Submitted = new(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);

    private static readonly AssetRef Pnp = new("SMT1-PNP-01", Guid.Parse("11111111-1111-1111-1111-111111111111"), "SMT Line 1");

    private static CompletedWork Work(
        DateTimeOffset? completed = null,
        DateTimeOffset? due = null,
        bool assetDown = true,
        string priority = "P2",
        Guid? id = null) => new(
            id ?? Guid.NewGuid(),
            "WO-000042",
            Guid.NewGuid(),
            priority,
            "Reactive",
            Submitted,
            Submitted.AddMinutes(20),
            completed ?? Submitted.AddMinutes(110),
            due ?? Submitted.AddHours(8),
            assetDown);

    [Fact]
    public void Project_copies_the_identity_and_the_resolved_line()
    {
        var work = Work();

        var fact = WorkOrderFact.Project(work, Pnp, Penang);

        Assert.Equal(work.WorkOrderId, fact.WorkOrderId);
        Assert.Equal("WO-000042", fact.Number);
        Assert.Equal(work.AssetId, fact.AssetId);
        Assert.Equal("SMT1-PNP-01", fact.AssetTag);
        Assert.Equal(Pnp.LineId, fact.LineId);
        Assert.Equal("SMT Line 1", fact.LineName);
        Assert.Equal("P2", fact.Priority);
        Assert.Equal("Reactive", fact.Source);
    }

    [Fact]
    public void Repair_minutes_run_from_start_to_completion_in_whole_minutes()
    {
        // Started 00:20, completed 01:50:59: 90 minutes and 59 seconds is 90 whole minutes, not 91.
        var fact = WorkOrderFact.Project(Work(completed: Submitted.AddMinutes(110).AddSeconds(59)), Pnp, Penang);

        Assert.Equal(90, fact.RepairMinutes);
    }

    [Fact]
    public void Downtime_runs_from_the_report_to_completion_when_the_asset_was_down()
    {
        var fact = WorkOrderFact.Project(Work(assetDown: true), Pnp, Penang);

        Assert.Equal(110, fact.DowntimeMinutes);
    }

    [Fact]
    public void Downtime_is_null_not_zero_when_the_asset_was_not_down()
    {
        var fact = WorkOrderFact.Project(Work(assetDown: false), Pnp, Penang);

        Assert.Null(fact.DowntimeMinutes);
    }

    [Fact]
    public void Finishing_exactly_at_the_deadline_meets_the_sla()
    {
        var due = Submitted.AddHours(8);

        Assert.True(WorkOrderFact.Project(Work(completed: due.AddSeconds(-1), due: due), Pnp, Penang).MetSla);
        Assert.True(WorkOrderFact.Project(Work(completed: due, due: due), Pnp, Penang).MetSla);
        Assert.False(WorkOrderFact.Project(Work(completed: due.AddSeconds(1), due: due), Pnp, Penang).MetSla);
    }

    [Fact]
    public void The_month_is_the_factory_month_not_the_utc_month()
    {
        // 17:00 UTC on 31 July is 01:00 on 1 August in Penang (UTC+8): the August report owns it.
        var fact = WorkOrderFact.Project(Work(completed: new DateTimeOffset(2026, 7, 31, 17, 0, 0, TimeSpan.Zero), due: new DateTimeOffset(2026, 8, 5, 0, 0, 0, TimeSpan.Zero)), Pnp, Penang);

        Assert.Equal(new DateOnly(2026, 8, 1), fact.CompletedMonth);
    }

    [Theory]
    [InlineData(2026, 7, 31, 15, 59, 59, 2026, 7)] // 23:59:59 local on 31 July
    [InlineData(2026, 7, 31, 16, 0, 0, 2026, 8)] // 00:00:00 local on 1 August
    [InlineData(2026, 12, 31, 16, 0, 0, 2027, 1)] // the year rolls over in factory time too
    [InlineData(2026, 8, 15, 4, 0, 0, 2026, 8)]
    public void Month_boundaries_follow_the_factory_calendar(int y, int mo, int d, int h, int mi, int s, int expectedYear, int expectedMonth)
    {
        var completed = new DateTimeOffset(y, mo, d, h, mi, s, TimeSpan.Zero);

        var fact = WorkOrderFact.Project(Work(completed: completed, due: completed.AddDays(1)), Pnp, Penang);

        Assert.Equal(new DateOnly(expectedYear, expectedMonth, 1), fact.CompletedMonth);
    }

    [Fact]
    public void An_unknown_asset_keeps_the_fact_on_no_line()
    {
        var fact = WorkOrderFact.Project(Work(), AssetRef.Unknown, Penang);

        Assert.Null(fact.LineId);
        Assert.Equal("Unknown line", fact.LineName);
        Assert.Equal("Unknown asset", fact.AssetTag);
    }

    [Fact]
    public void A_missing_priority_or_source_is_recorded_as_unknown_rather_than_failing()
    {
        var work = Work(priority: " ") with { Source = null! };

        var fact = WorkOrderFact.Project(work, Pnp, Penang);

        Assert.Equal("Unknown", fact.Priority);
        Assert.Equal("Unknown", fact.Source);
    }

    [Fact]
    public void Overwrite_replaces_the_numbers_and_takes_the_new_line_by_default()
    {
        var id = Guid.NewGuid();
        var fact = WorkOrderFact.Project(Work(id: id), Pnp, Penang);
        var moved = new AssetRef("SMT1-PNP-01", Guid.NewGuid(), "SMT Line 2");

        fact.Overwrite(WorkOrderFact.Project(Work(id: id, completed: Submitted.AddHours(9), assetDown: false), moved, Penang), keepKnownLine: false);

        Assert.Equal("SMT Line 2", fact.LineName);
        Assert.Equal(moved.LineId, fact.LineId);
        Assert.False(fact.MetSla);
        Assert.Null(fact.DowntimeMinutes);
    }

    [Fact]
    public void A_rebuild_keeps_a_line_that_was_already_recorded_but_fills_a_missing_one()
    {
        var id = Guid.NewGuid();
        var known = WorkOrderFact.Project(Work(id: id), Pnp, Penang);
        var unknown = WorkOrderFact.Project(Work(id: id), AssetRef.Unknown, Penang);
        var today = new AssetRef("SMT1-PNP-01", Guid.NewGuid(), "SMT Line 2");

        known.Overwrite(WorkOrderFact.Project(Work(id: id), today, Penang), keepKnownLine: true);
        unknown.Overwrite(WorkOrderFact.Project(Work(id: id), today, Penang), keepKnownLine: true);

        Assert.Equal("SMT Line 1", known.LineName); // historical line survives
        Assert.Equal("SMT Line 2", unknown.LineName); // nothing to preserve, so today's line is the best we have
    }

    [Fact]
    public void Overwrite_refuses_the_projection_of_another_work_order()
    {
        var fact = WorkOrderFact.Project(Work(), Pnp, Penang);
        var other = WorkOrderFact.Project(Work(), Pnp, Penang);

        Assert.Throws<ArgumentException>(() => fact.Overwrite(other, keepKnownLine: false));
    }
}
