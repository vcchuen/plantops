namespace PlantOps.SharedKernel;

/// <summary>The addressed resource does not exist. Mapped to HTTP 404.</summary>
public class NotFoundException(string message) : Exception(message);
