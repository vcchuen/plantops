namespace PlantOps.Modules.Inventory.Domain;

internal enum ReservationStatus
{
    /// <summary>Stock is held for a work order that is still open.</summary>
    Active,

    /// <summary>The work order was completed; the stock left the shelf.</summary>
    Consumed,

    /// <summary>Given back (released by a person, or the work order was cancelled).</summary>
    Released,
}
