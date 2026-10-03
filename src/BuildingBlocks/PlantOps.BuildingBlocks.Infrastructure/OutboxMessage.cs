namespace PlantOps.BuildingBlocks.Infrastructure;

/// <summary>
/// An integration event waiting for delivery, written in the same transaction as the change that caused it (ADR-0009).
/// ProcessedAt stays null until every handler succeeded; a message that keeps failing is parked (left unprocessed).
/// </summary>
public sealed class OutboxMessage
{
    public const int MaxAttempts = 5;
    public const int TypeMaxLength = 300;
    public const int LastErrorMaxLength = 2000;

    // For EF Core only.
    private OutboxMessage()
    {
    }

    internal OutboxMessage(string type, string payload, DateTimeOffset occurredAt)
    {
        Id = Guid.CreateVersion7();
        Type = type;
        Payload = payload;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; private set; }

    /// <summary>The event's full CLR name; only ever resolved through <see cref="IntegrationEventRegistry"/>.</summary>
    public string Type { get; private set; } = null!;

    /// <summary>The event serialized as JSON (nvarchar(max)).</summary>
    public string Payload { get; private set; } = null!;

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    public bool IsParked => ProcessedAt is null && Attempts >= MaxAttempts;

    internal void MarkProcessed(DateTimeOffset now)
    {
        ProcessedAt = now;
        LastError = null;
    }

    internal void MarkFailed(string error)
    {
        Attempts++;
        LastError = error.Length > LastErrorMaxLength ? error[..LastErrorMaxLength] : error;
    }
}
