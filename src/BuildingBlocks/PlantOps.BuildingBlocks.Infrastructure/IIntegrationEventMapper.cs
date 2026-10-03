using PlantOps.SharedKernel;

namespace PlantOps.BuildingBlocks.Infrastructure;

/// <summary>
/// Chooses which domain events (internal, audit-shaped) become integration events (public contracts) for one
/// module's DbContext. <see cref="DomainEventInterceptor"/> calls it and writes the outbox row in the same SaveChanges.
/// </summary>
public interface IIntegrationEventMapper
{
    /// <summary>The producing module's DbContext; the interceptor is shared, so this picks the right mapper.</summary>
    Type ContextType { get; }

    /// <summary>The integration event for this domain event, or null when no other module needs to hear about it.</summary>
    IIntegrationEvent? Map(IDomainEvent domainEvent);
}
