using Microsoft.Extensions.Time.Testing;
using PlantOps.Modules.WorkOrders.Endpoints;

namespace PlantOps.Modules.WorkOrders.Tests.Domain;

public class DueWindowTests
{
    [Theory]
    [InlineData("2026-10-03T00:00:00Z", "2026-10-03T00:00:00Z", "2026-10-04T00:00:00Z")]
    [InlineData("2026-10-03T08:30:00Z", "2026-10-03T00:00:00Z", "2026-10-04T00:00:00Z")]
    [InlineData("2026-10-03T23:59:59Z", "2026-10-03T00:00:00Z", "2026-10-04T00:00:00Z")]
    [InlineData("2026-12-31T12:00:00Z", "2026-12-31T00:00:00Z", "2027-01-01T00:00:00Z")]
    public void Today_runs_from_midnight_to_the_next_midnight(string now, string start, string end)
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse(now));

        var window = DueWindow.Today(time);

        Assert.Equal(DateTimeOffset.Parse(start), window.Start);
        Assert.Equal(DateTimeOffset.Parse(end), window.End);
    }

    [Fact]
    public void The_window_follows_the_clock()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-03T23:59:59Z"));
        Assert.Equal(DateTimeOffset.Parse("2026-10-04T00:00:00Z"), DueWindow.Today(time).End);

        time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(DateTimeOffset.Parse("2026-10-04T00:00:00Z"), DueWindow.Today(time).Start);
    }
}
