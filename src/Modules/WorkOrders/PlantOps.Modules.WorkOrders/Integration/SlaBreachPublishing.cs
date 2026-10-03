using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using PlantOps.BuildingBlocks.Infrastructure;
using PlantOps.Modules.WorkOrders.Contracts;

namespace PlantOps.Modules.WorkOrders.Integration;

/// <summary>Sends an SLA breach to wherever notifications are produced from (a queue, or just the log).</summary>
internal interface ISlaBreachPublisher
{
    /// <param name="messageId">The outbox row id: the same value on every redelivery, so a broker can drop duplicates.</param>
    Task PublishAsync(WorkOrderSlaBreachedIntegrationEvent breach, Guid messageId, CancellationToken cancellationToken);
}

/// <summary>
/// The outbox handler for the breach event: hands it to the configured publisher. No inbox row on purpose. The only
/// effect is an external send, which a local database transaction cannot make atomic; the guard against a duplicate
/// after a crash-and-redeliver is the broker's duplicate detection on MessageId (see <see cref="ServiceBusSlaBreachPublisher"/>).
/// </summary>
internal sealed class SlaBreachForwarder(ISlaBreachPublisher publisher) : IIntegrationEventHandler<WorkOrderSlaBreachedIntegrationEvent>
{
    public Task HandleAsync(WorkOrderSlaBreachedIntegrationEvent integrationEvent, Guid messageId, CancellationToken cancellationToken) =>
        publisher.PublishAsync(integrationEvent, messageId, cancellationToken);
}

/// <summary>Used when no Service Bus is configured (a developer laptop, tests): the breach is only logged.</summary>
internal sealed class LoggingSlaBreachPublisher(ILogger<LoggingSlaBreachPublisher> logger) : ISlaBreachPublisher
{
    public Task PublishAsync(WorkOrderSlaBreachedIntegrationEvent breach, Guid messageId, CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "SLA breach {Number} ({Priority}, asset {AssetTag}) was due {DueAt:O}; no Service Bus configured, not forwarding. Message {MessageId}",
            breach.Number,
            breach.Priority,
            breach.AssetTag,
            breach.DueAt,
            messageId);
        return Task.CompletedTask;
    }
}

/// <summary>Sends the breach to the Service Bus queue (default "sla-breaches"). Singleton: the sender is thread-safe and expensive to create.</summary>
internal sealed class ServiceBusSlaBreachPublisher(ServiceBusSender sender) : ISlaBreachPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public Task PublishAsync(WorkOrderSlaBreachedIntegrationEvent breach, Guid messageId, CancellationToken cancellationToken) =>
        sender.SendMessageAsync(ToMessage(breach, messageId), cancellationToken);

    /// <summary>
    /// Pure so it is unit-tested without a broker. MessageId is the outbox id: if we crash after the send but before
    /// the outbox row is marked processed, the dispatcher re-sends with the SAME id and Service Bus duplicate
    /// detection (enabled on the queue, within its detection window) drops it. Subject carries the event type so a
    /// consumer can route or ignore without parsing the body.
    /// </summary>
    internal static ServiceBusMessage ToMessage(WorkOrderSlaBreachedIntegrationEvent breach, Guid messageId) =>
        new(BinaryData.FromString(JsonSerializer.Serialize(breach, JsonOptions)))
        {
            MessageId = messageId.ToString(),
            ContentType = "application/json",
            Subject = IntegrationEventRegistration.NameOf(typeof(WorkOrderSlaBreachedIntegrationEvent)),
        };
}
