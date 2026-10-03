namespace PlantOps.SharedKernel;

/// <summary>
/// Marker for something that happened to an aggregate. Implement as an immutable record: the payload is
/// serialized into the audit trail (ADR-0007), so it is a contract with auditors, not an internal detail.
/// </summary>
public interface IDomainEvent;
