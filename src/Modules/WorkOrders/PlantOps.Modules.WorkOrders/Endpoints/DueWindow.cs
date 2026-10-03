namespace PlantOps.Modules.WorkOrders.Endpoints;

internal static class DueWindow
{
    /// <summary>The current day as a half-open range: from 00:00 (inclusive) to 00:00 the next day (exclusive).</summary>
    public static (DateTimeOffset Start, DateTimeOffset End) Today(TimeProvider time)
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var start = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return (start, start.AddDays(1));
    }
}
