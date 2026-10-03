using PlantOps.BuildingBlocks.Infrastructure;
using PlantOps.Modules.Reporting.Queries;
using PlantOps.SharedKernel;

namespace PlantOps.Modules.Reporting.Tests.Domain;

public class ReportRangeTests
{
    private static readonly TimeZoneInfo Penang = FactoryClock.FindZone(null);

    private static readonly DateOnly Today = new(2026, 10, 3);

    [Fact]
    public void Absent_dates_default_to_the_last_90_days_ending_tomorrow()
    {
        var range = ReportRange.Resolve(null, null, Today, Penang);

        // "To" is exclusive, so ending tomorrow includes everything completed today.
        Assert.Equal(new DateOnly(2026, 10, 4), range.To);
        Assert.Equal(new DateOnly(2026, 7, 6), range.From);
    }

    [Fact]
    public void Dates_are_factory_local_midnights_converted_to_utc()
    {
        var range = ReportRange.Resolve(new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1), Today, Penang);

        // Midnight in Penang (UTC+8) is 16:00 UTC the evening before.
        Assert.Equal(new DateTimeOffset(2026, 7, 31, 16, 0, 0, TimeSpan.Zero), range.FromUtc);
        Assert.Equal(new DateTimeOffset(2026, 8, 31, 16, 0, 0, TimeSpan.Zero), range.ToUtc);
    }

    [Fact]
    public void An_absent_to_is_tomorrow_and_an_absent_from_is_90_days_before_to()
    {
        var onlyFrom = ReportRange.Resolve(new DateOnly(2026, 9, 1), null, Today, Penang);
        var onlyTo = ReportRange.Resolve(null, new DateOnly(2026, 6, 1), Today, Penang);

        Assert.Equal(new DateOnly(2026, 10, 4), onlyFrom.To);
        Assert.Equal(new DateOnly(2026, 3, 3), onlyTo.From);
    }

    [Fact]
    public void A_range_of_exactly_366_days_is_allowed()
    {
        var range = ReportRange.Resolve(new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 2), Today, Penang);

        Assert.Equal(366, range.To.DayNumber - range.From.DayNumber);
    }

    [Fact]
    public void A_range_of_367_days_is_rejected()
    {
        var ex = Assert.Throws<DomainException>(() =>
            ReportRange.Resolve(new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 3), Today, Penang));

        Assert.Contains("366", ex.Message);
    }

    [Fact]
    public void From_must_be_before_to()
    {
        var day = new DateOnly(2026, 5, 1);

        Assert.Throws<DomainException>(() => ReportRange.Resolve(day, day, Today, Penang));
        Assert.Throws<DomainException>(() => ReportRange.Resolve(day.AddDays(1), day, Today, Penang));
    }
}
