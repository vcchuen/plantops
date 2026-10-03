using Microsoft.EntityFrameworkCore;
using PlantOps.Modules.Inventory.Domain;

namespace PlantOps.Modules.Inventory.Infrastructure;

internal static class InventoryQueries
{
    /// <summary>
    /// Every part holding an Active reservation for the work order, with just those reservations loaded (the
    /// aggregate methods only need the active ones).
    /// </summary>
    public static IQueryable<SparePart> PartsWithActiveReservationFor(InventoryDbContext db, Guid workOrderId) =>
        db.SpareParts
            .Include(p => p.Reservations.Where(r => r.Status == ReservationStatus.Active))
            .Where(p => p.Reservations.Any(r => r.WorkOrderId == workOrderId && r.Status == ReservationStatus.Active));
}
