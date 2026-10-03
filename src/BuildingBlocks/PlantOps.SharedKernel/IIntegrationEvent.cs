namespace PlantOps.SharedKernel;

/// <summary>
/// Marker for a fact one module publishes for others (ADR-0009). Implement as an immutable record in the
/// producer's Contracts project: it is a versioned public contract, so add fields and never repurpose them.
/// Lives in SharedKernel (not Infrastructure) so Contracts projects can implement it without pulling in EF Core.
/// The envelope (message id, type, time) is the outbox row, not part of the event.
/// </summary>
public interface IIntegrationEvent;
