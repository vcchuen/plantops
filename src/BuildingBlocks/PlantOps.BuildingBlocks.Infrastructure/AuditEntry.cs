namespace PlantOps.BuildingBlocks.Infrastructure;

/// <summary>One business fact ("WorkOrderApproved") about one aggregate, with who did it and when (ADR-0007).</summary>
public sealed class AuditEntry
{
    public const int TypeMaxLength = 100;
    public const int AggregateIdMaxLength = 100;
    public const int ActorMaxLength = 200;

    // For EF Core only.
    private AuditEntry()
    {
    }

    internal AuditEntry(
        string aggregateType,
        string aggregateId,
        string eventType,
        string payload,
        string actorId,
        string actorName,
        DateTimeOffset occurredAt)
    {
        Id = Guid.CreateVersion7();
        AggregateType = aggregateType;
        AggregateId = aggregateId;
        EventType = eventType;
        Payload = payload;
        ActorId = actorId;
        ActorName = actorName;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; private set; }

    public string AggregateType { get; private set; } = null!;

    public string AggregateId { get; private set; } = null!;

    public string EventType { get; private set; } = null!;

    /// <summary>The event serialized as JSON (nvarchar(max)).</summary>
    public string Payload { get; private set; } = null!;

    public string ActorId { get; private set; } = null!;

    public string ActorName { get; private set; } = null!;

    public DateTimeOffset OccurredAt { get; private set; }
}
