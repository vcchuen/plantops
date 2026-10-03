using System.Runtime.CompilerServices;
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

    public async IAsyncEnumerable<CompletedWorkOrder> CompletedSinceAsync(
        DateTimeOffset since,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Completed or Closed with a StartedAt: both are set by the lifecycle, the null checks only satisfy the types.
        var rows = db.WorkOrders
            .AsNoTracking()
            .Where(w => (w.Status == WorkOrderStatus.Completed || w.Status == WorkOrderStatus.Closed)
                        && w.CompletedAt != null && w.StartedAt != null && w.CompletedAt >= since)
            .OrderBy(w => w.CompletedAt)
            .Select(w => new
            {
                w.Id,
                w.Number,
                w.AssetId,
                w.Title,
                w.SubmittedAt,
                w.StartedAt,
                w.CompletedAt,
                w.AssetDown,
                w.Priority,
                w.Source,
                w.DueAt,
            })
            .AsAsyncEnumerable();

        await foreach (var w in rows.WithCancellation(cancellationToken))
        {
            yield return new CompletedWorkOrder(
                w.Id.Value,
                WorkOrder.FormatNumber(w.Number),
                w.AssetId,
                w.Title,
                w.SubmittedAt,
                w.StartedAt!.Value,
                w.CompletedAt!.Value,
                w.AssetDown,
                w.Priority.ToString(),
                w.Source.ToString(),
                w.DueAt);
        }
    }
}
