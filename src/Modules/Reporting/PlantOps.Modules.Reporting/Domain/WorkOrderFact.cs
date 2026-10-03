namespace PlantOps.Modules.Reporting.Domain;

/// <summary>What Reporting knows about an asset at the moment a work order completes (resolved through IAssetDirectory).</summary>
internal sealed record AssetRef(string Tag, Guid? LineId, string LineName)
{
    /// <summary>The asset was not found (deleted, or the directory is out of step): the fact is kept, on no line.</summary>
    public static AssetRef Unknown { get; } = new(WorkOrderFact.UnknownAssetTag, null, WorkOrderFact.UnknownLineName);
}

/// <summary>The completion facts needed to project a fact row: the same data in an event and in a rebuild.</summary>
internal sealed record CompletedWork(
    Guid WorkOrderId,
    string Number,
    Guid AssetId,
    string Priority,
    string Source,
    DateTimeOffset SubmittedAt,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    DateTimeOffset DueAt,
    bool AssetDown);

/// <summary>
/// One row per completed work order (ADR-0011): the denormalised read model every report aggregates. It is a
/// projection, not an aggregate, so it has no behaviour beyond deriving its numbers once, when it is built.
/// </summary>
internal sealed class WorkOrderFact
{
    public const string UnknownLineName = "Unknown line";
    public const string UnknownAssetTag = "Unknown asset";
    public const string UnknownPriority = "Unknown";

    public const int NumberMaxLength = 20;
    public const int AssetTagMaxLength = 20;
    public const int LineNameMaxLength = 100;
    public const int PriorityMaxLength = 10;
    public const int SourceMaxLength = 20;

    // For EF Core only.
    private WorkOrderFact()
    {
    }

    public Guid WorkOrderId { get; private set; }

    public string Number { get; private set; } = null!;

    public Guid AssetId { get; private set; }

    public string AssetTag { get; private set; } = null!;

    /// <summary>The line the asset was on when the work order completed (historical); null when the asset was unknown.</summary>
    public Guid? LineId { get; private set; }

    public string LineName { get; private set; } = null!;

    public string Priority { get; private set; } = null!;

    public string Source { get; private set; } = null!;

    public DateTimeOffset SubmittedAt { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset CompletedAt { get; private set; }

    public DateTimeOffset DueAt { get; private set; }

    public bool MetSla { get; private set; }

    /// <summary>Whole minutes from start to completion (the "repair" in mean time to repair).</summary>
    public int RepairMinutes { get; private set; }

    /// <summary>Whole minutes from the report to completion, only when the asset was down; otherwise null (not zero).</summary>
    public int? DowntimeMinutes { get; private set; }

    /// <summary>The first day of the month of <see cref="CompletedAt"/> in FACTORY time, stored so month grouping is a plain column.</summary>
    public DateOnly CompletedMonth { get; private set; }

    /// <summary>
    /// Derives the stored numbers. The month is decided here, in the factory's zone, once: 17:00 UTC on 31 July is
    /// 01:00 on 1 August in Penang, and the plant manager's August report must include it.
    /// </summary>
    public static WorkOrderFact Project(CompletedWork work, AssetRef asset, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(zone);

        var local = TimeZoneInfo.ConvertTime(work.CompletedAt, zone);
        return new WorkOrderFact
        {
            WorkOrderId = work.WorkOrderId,
            Number = Truncate(work.Number, NumberMaxLength),
            AssetId = work.AssetId,
            AssetTag = Truncate(asset.Tag, AssetTagMaxLength),
            LineId = asset.LineId,
            LineName = Truncate(asset.LineName, LineNameMaxLength),
            Priority = Truncate(string.IsNullOrWhiteSpace(work.Priority) ? UnknownPriority : work.Priority, PriorityMaxLength),
            Source = Truncate(string.IsNullOrWhiteSpace(work.Source) ? UnknownPriority : work.Source, SourceMaxLength),
            SubmittedAt = work.SubmittedAt,
            StartedAt = work.StartedAt,
            CompletedAt = work.CompletedAt,
            DueAt = work.DueAt,
            // Inclusive: finishing exactly at the deadline still meets it (same rule as the SLA states in WorkOrders).
            MetSla = work.CompletedAt <= work.DueAt,
            RepairMinutes = WholeMinutes(work.StartedAt, work.CompletedAt),
            DowntimeMinutes = work.AssetDown ? WholeMinutes(work.SubmittedAt, work.CompletedAt) : null,
            CompletedMonth = new DateOnly(local.Year, local.Month, 1),
        };
    }

    /// <summary>
    /// Replaces this row's data with a freshly projected one (same work order). With <paramref name="keepKnownLine"/>
    /// a rebuild keeps an already-recorded line: the rebuild only knows today's line, the event knew the line at completion.
    /// </summary>
    public void Overwrite(WorkOrderFact fresh, bool keepKnownLine)
    {
        ArgumentNullException.ThrowIfNull(fresh);
        if (fresh.WorkOrderId != WorkOrderId)
        {
            throw new ArgumentException("A fact can only be overwritten by the projection of the same work order.", nameof(fresh));
        }

        Number = fresh.Number;
        AssetId = fresh.AssetId;
        if (!(keepKnownLine && LineId is not null))
        {
            AssetTag = fresh.AssetTag;
            LineId = fresh.LineId;
            LineName = fresh.LineName;
        }

        Priority = fresh.Priority;
        Source = fresh.Source;
        SubmittedAt = fresh.SubmittedAt;
        StartedAt = fresh.StartedAt;
        CompletedAt = fresh.CompletedAt;
        DueAt = fresh.DueAt;
        MetSla = fresh.MetSla;
        RepairMinutes = fresh.RepairMinutes;
        DowntimeMinutes = fresh.DowntimeMinutes;
        CompletedMonth = fresh.CompletedMonth;
    }

    // Floor, like Assets' maintenance record: 59.9 minutes of downtime is 59 whole minutes, never rounded up.
    private static int WholeMinutes(DateTimeOffset from, DateTimeOffset to) =>
        (int)Math.Max(0, Math.Floor((to - from).TotalMinutes));

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
