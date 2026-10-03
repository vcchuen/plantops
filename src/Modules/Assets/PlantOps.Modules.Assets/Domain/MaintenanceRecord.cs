namespace PlantOps.Modules.Assets.Domain;

/// <summary>
/// One finished repair in an asset's maintenance history. Written by the handler for the WorkOrders "completed"
/// event; a plain entity, not an aggregate, because nothing ever changes it and it raises no events of its own.
/// </summary>
internal sealed class MaintenanceRecord
{
    public const int NumberMaxLength = 20;
    public const int TitleMaxLength = 200;
    public const int ResolutionMaxLength = 2000;
    public const int TechnicianNameMaxLength = 200;

    // For EF Core only.
    private MaintenanceRecord()
    {
    }

    public MaintenanceRecord(
        Guid assetId,
        Guid workOrderId,
        string number,
        string title,
        string resolution,
        string technicianName,
        DateTimeOffset completedAt,
        int? downtimeMinutes)
    {
        Id = Guid.CreateVersion7();
        AssetId = assetId;
        WorkOrderId = workOrderId;
        Number = number;
        Title = title;
        Resolution = resolution;
        TechnicianName = technicianName;
        CompletedAt = completedAt;
        DowntimeMinutes = downtimeMinutes;
    }

    public Guid Id { get; private set; }

    public Guid AssetId { get; private set; }

    /// <summary>Unique: a second line of defence behind the inbox, so redelivery can never record a repair twice.</summary>
    public Guid WorkOrderId { get; private set; }

    public string Number { get; private set; } = null!;

    public string Title { get; private set; } = null!;

    public string Resolution { get; private set; } = null!;

    public string TechnicianName { get; private set; } = null!;

    public DateTimeOffset CompletedAt { get; private set; }

    /// <summary>Report to completion, in minutes, when the asset was down; null when production kept running.</summary>
    public int? DowntimeMinutes { get; private set; }

    /// <summary>Downtime is only meaningful if the asset stopped; never negative even if clocks disagree.</summary>
    public static int? DowntimeFor(bool assetDown, DateTimeOffset submittedAt, DateTimeOffset completedAt) =>
        assetDown ? (int)Math.Max(0, Math.Floor((completedAt - submittedAt).TotalMinutes)) : null;
}
