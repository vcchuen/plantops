using Microsoft.EntityFrameworkCore;
using PlantOps.Modules.Inventory.Contracts;
using PlantOps.Modules.Inventory.Domain;

namespace PlantOps.Modules.Inventory.Infrastructure;

internal sealed class ReservationQueries(InventoryDbContext db) : IReservationQueries
{
    public Task<int> CountActiveAsync(Guid workOrderId, CancellationToken cancellationToken) =>
        db.Reservations
            .AsNoTracking()
            .CountAsync(r => r.WorkOrderId == workOrderId && r.Status == ReservationStatus.Active, cancellationToken);
}
