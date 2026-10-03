namespace PlantOps.Modules.Inventory.Contracts;

/// <summary>What other modules may ask about stock reservations. Read-only: Inventory owns the data.</summary>
public interface IReservationQueries
{
    /// <summary>How many Active reservations (parts being held) the work order has.</summary>
    Task<int> CountActiveAsync(Guid workOrderId, CancellationToken cancellationToken);
}
