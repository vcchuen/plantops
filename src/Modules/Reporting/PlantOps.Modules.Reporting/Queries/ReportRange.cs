using PlantOps.SharedKernel;

namespace PlantOps.Modules.Reporting.Queries;

/// <summary>
/// A report's date window. The caller speaks factory-local dates, half-open [From, To): "1 Jan to 1 Feb" is exactly
/// January, whatever the length of the month. The window is converted to UTC instants because CompletedAt is an instant.
/// </summary>
internal readonly record struct ReportRange(DateOnly From, DateOnly To, DateTimeOffset FromUtc, DateTimeOffset ToUtc)
{
    public const int MaxDays = 366;
    public const int DefaultDays = 90;

    /// <summary>Applies the defaults and the rules; throws <see cref="DomainException"/> (HTTP 400) when the window is not acceptable.</summary>
    /// <param name="today">The factory's current date. An absent <c>to</c> is tomorrow, so the default window includes all of today.</param>
    public static ReportRange Resolve(DateOnly? from, DateOnly? to, DateOnly today, TimeZoneInfo zone)
    {
        var end = to ?? today.AddDays(1);
        var start = from ?? end.AddDays(-DefaultDays);

        if (start >= end)
        {
            throw new DomainException("'from' must be before 'to' (the range is [from, to)).");
        }

        if (end.DayNumber - start.DayNumber > MaxDays)
        {
            throw new DomainException($"The range may span at most {MaxDays} days.");
        }

        return new ReportRange(start, end, StartOfDayUtc(start, zone), StartOfDayUtc(end, zone));
    }

    private static DateTimeOffset StartOfDayUtc(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        // GetUtcOffset (unlike ConvertTimeToUtc) does not throw for a wall time that DST skips; Penang has no DST,
        // but the zone is configuration.
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }
}
