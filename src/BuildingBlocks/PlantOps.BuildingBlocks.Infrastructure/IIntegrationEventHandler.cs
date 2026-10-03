using PlantOps.SharedKernel;

namespace PlantOps.BuildingBlocks.Infrastructure;

/// <summary>
/// A consumer of an integration event. Delivery is at-least-once, so implementations MUST be idempotent:
/// record the effect and an <see cref="InboxMessage"/> in one SaveChanges (see <see cref="InboxGuard"/>).
/// </summary>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IIntegrationEvent
{
    /// <param name="messageId">The outbox row id; stable across redeliveries, so it is the idempotency key.</param>
    Task HandleAsync(TEvent integrationEvent, Guid messageId, CancellationToken cancellationToken);
}
