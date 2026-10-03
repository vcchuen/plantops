namespace PlantOps.BuildingBlocks.Infrastructure;

/// <summary>
/// "Handler X already handled message Y" (ADR-0009). Inserted in the same SaveChanges as the handler's effect; the
/// composite primary key makes a second delivery fail loudly instead of applying the effect twice.
/// </summary>
public sealed class InboxMessage
{
    public const int HandlerMaxLength = 200;

    // For EF Core only.
    private InboxMessage()
    {
    }

    internal InboxMessage(Guid messageId, string handler, DateTimeOffset processedAt)
    {
        MessageId = messageId;
        Handler = handler;
        ProcessedAt = processedAt;
    }

    public Guid MessageId { get; private set; }

    public string Handler { get; private set; } = null!;

    public DateTimeOffset ProcessedAt { get; private set; }
}
