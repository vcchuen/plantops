using PlantOps.SharedKernel;

namespace PlantOps.Modules.Inventory.Domain;

/// <summary>
/// Not enough free stock. A ConflictException (HTTP 409), not a DomainException (400): the request was well-formed
/// and would have succeeded a moment ago; it conflicts with the stock as it is now.
/// </summary>
internal sealed class InsufficientStockException(int available, string unit, string partNumber)
    : ConflictException($"Only {available} {unit} of {partNumber} available");
