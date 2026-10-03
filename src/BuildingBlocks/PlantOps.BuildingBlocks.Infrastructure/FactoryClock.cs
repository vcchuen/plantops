namespace PlantOps.BuildingBlocks.Infrastructure;

/// <summary>
/// The factory's wall calendar. "Which day is it?" is a business question with a place attached: at 07:30 in Penang
/// it is still yesterday in UTC, so a UTC date would make a PM job due "today" fire a day late (design 06).
/// Built on <see cref="TimeProvider"/> so tests drive it with a fake clock, and on <see cref="TimeZoneInfo"/> so DST
/// rules (none in Penang, but the code does not assume that) come from the platform's tz database.
/// </summary>
public sealed class FactoryClock(TimeProvider time, TimeZoneInfo zone)
{
    /// <summary>The configuration key; the value is an IANA id such as "Asia/Kuala_Lumpur".</summary>
    public const string ConfigurationKey = "Factory:TimeZone";

    public const string DefaultTimeZoneId = "Asia/Kuala_Lumpur";

    /// <summary>
    /// Resolves an IANA id. .NET on Linux and macOS reads the OS tz database directly, and on Windows (.NET 6+, with
    /// ICU) maps IANA ids to Windows ones, so the same id works everywhere. A minimal container image without tzdata
    /// throws <see cref="TimeZoneNotFoundException"/> here, which is the loud failure we want at first use.
    /// </summary>
    public static TimeZoneInfo FindZone(string? id) =>
        TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(id) ? DefaultTimeZoneId : id.Trim());

    public TimeZoneInfo Zone => zone;

    /// <summary>The factory's calendar date right now.</summary>
    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone).DateTime);

    /// <summary>
    /// The last representable instant of <paramref name="date"/> in factory local time, as UTC. A deadline "on the
    /// 5th" means the whole of the 5th, so work finished at 23:59 local still meets it.
    /// </summary>
    public DateTimeOffset EndOfDay(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Unspecified);
        // Offset for that wall time; for an ambiguous or skipped DST time the platform picks the standard offset,
        // which is good enough for "end of day" (a deadline cannot be more than an hour off).
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }
}
