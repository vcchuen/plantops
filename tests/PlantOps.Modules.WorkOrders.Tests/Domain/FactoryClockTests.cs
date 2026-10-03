using Microsoft.Extensions.Time.Testing;
using PlantOps.BuildingBlocks.Infrastructure;

namespace PlantOps.Modules.WorkOrders.Tests.Domain;

// "Today" for the factory is the Penang calendar date (UTC+8, no DST), not the UTC date.
public class FactoryClockTests
{
    private static FactoryClock Penang(FakeTimeProvider time) => new(time, FactoryClock.FindZone("Asia/Kuala_Lumpur"));

    [Theory]
    [InlineData("2026-10-03T15:59:59Z", "2026-10-03")] // 23:59:59 in Penang: still the 3rd
    [InlineData("2026-10-03T16:00:00Z", "2026-10-04")] // midnight in Penang: already the 4th, though UTC says the 3rd
    [InlineData("2026-10-03T23:30:00Z", "2026-10-04")] // 07:30 in Penang on the 4th
    [InlineData("2026-10-04T00:00:00Z", "2026-10-04")]
    [InlineData("2026-10-03T00:00:00Z", "2026-10-03")] // 08:00 in Penang
    [InlineData("2026-12-31T16:00:00Z", "2027-01-01")] // the year boundary
    public void Today_is_the_penang_calendar_date(string utc, string expected)
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse(utc));

        Assert.Equal(DateOnly.Parse(expected), Penang(time).Today);
    }

    [Fact]
    public void At_0730_in_penang_utc_still_says_yesterday_but_the_factory_says_today()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-03T23:30:00Z"));

        Assert.Equal(new DateOnly(2026, 10, 3), DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime));
        Assert.Equal(new DateOnly(2026, 10, 4), Penang(time).Today);
    }

    [Fact]
    public void Today_follows_the_clock()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-03T15:59:59Z"));
        var clock = Penang(time);
        Assert.Equal(new DateOnly(2026, 10, 3), clock.Today);

        time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(new DateOnly(2026, 10, 4), clock.Today);
    }

    [Fact]
    public void End_of_day_is_the_last_instant_of_the_penang_day_in_utc()
    {
        var clock = Penang(new FakeTimeProvider());

        var end = clock.EndOfDay(new DateOnly(2026, 10, 3));

        // 23:59:59.9999999 at UTC+8 is 15:59:59.9999999 UTC the same date.
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 15, 59, 59, TimeSpan.Zero).AddTicks(TimeSpan.TicksPerSecond - 1), end);
        Assert.Equal(TimeSpan.Zero, end.Offset);
    }

    [Fact]
    public void A_deadline_at_end_of_day_is_met_by_work_finished_at_2359_local()
    {
        var clock = Penang(new FakeTimeProvider());
        var due = clock.EndOfDay(new DateOnly(2026, 10, 3));

        Assert.True(DateTimeOffset.Parse("2026-10-03T15:59:00Z") <= due);
        Assert.True(DateTimeOffset.Parse("2026-10-03T16:00:00Z") > due); // already the next day locally
    }

    [Fact]
    public void A_utc_factory_zone_has_the_utc_date_and_midnight_deadline()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-03T23:30:00Z"));
        var clock = new FactoryClock(time, TimeZoneInfo.Utc);

        Assert.Equal(new DateOnly(2026, 10, 3), clock.Today);
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 23, 59, 59, TimeSpan.Zero).AddTicks(TimeSpan.TicksPerSecond - 1), clock.EndOfDay(new DateOnly(2026, 10, 3)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void The_default_zone_is_kuala_lumpur(string? id) =>
        Assert.Equal(TimeSpan.FromHours(8), FactoryClock.FindZone(id).BaseUtcOffset);

    [Fact]
    public void An_unknown_zone_fails_loudly() =>
        Assert.Throws<TimeZoneNotFoundException>(() => FactoryClock.FindZone("Mars/Olympus_Mons"));
}
