namespace PlantOps.SharedKernel;

/// <summary>A business rule or invariant was violated. Mapped to HTTP 400.</summary>
public class DomainException(string message) : Exception(message);
