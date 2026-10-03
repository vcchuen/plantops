using PlantOps.Modules.Assets.Domain;

namespace PlantOps.Modules.Assets.Tests.Domain;

public class MaintenanceRecordTests
{
    private static readonly DateTimeOffset Submitted = new(2026, 10, 3, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Downtime_is_report_to_completion_in_whole_minutes_when_the_asset_was_down() =>
        Assert.Equal(125, MaintenanceRecord.DowntimeFor(true, Submitted, Submitted.AddMinutes(125).AddSeconds(59)));

    [Fact]
    public void Downtime_is_null_when_production_kept_running() =>
        Assert.Null(MaintenanceRecord.DowntimeFor(false, Submitted, Submitted.AddHours(3)));

    [Fact]
    public void Downtime_is_never_negative_even_if_the_clocks_disagree() =>
        Assert.Equal(0, MaintenanceRecord.DowntimeFor(true, Submitted, Submitted.AddMinutes(-5)));
}
