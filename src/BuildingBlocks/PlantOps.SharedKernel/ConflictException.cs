namespace PlantOps.SharedKernel;

/// <summary>The request conflicts with current state, e.g. a uniqueness violation. Mapped to HTTP 409.</summary>
public class ConflictException(string message) : Exception(message);
