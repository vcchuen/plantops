using Microsoft.EntityFrameworkCore;
using PlantOps.Modules.WorkOrders.Contracts;
using PlantOps.Modules.WorkOrders.Domain;

namespace PlantOps.Modules.WorkOrders.Infrastructure;

internal sealed class WorkOrderDirectory(WorkOrdersDbContext db) : IWorkOrderDirectory
{
    public async Task<WorkOrderSummary?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var workOrderId = new WorkOrderId(id);

        // Project to scalars first: formatting the number is not SQL.
        var row = await db.WorkOrders
            .AsNoTracking()
            .Where(w => w.Id == workOrderId)
            .Select(w => new { w.Number, w.Status, w.AssignedToId })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new WorkOrderSummary(id, WorkOrder.FormatNumber(row.Number), row.Status.ToString(), row.AssignedToId);
    }
}
