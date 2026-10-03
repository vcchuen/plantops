using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PlantOps.Modules.WorkOrders.Contracts;
using PlantOps.Modules.WorkOrders.Domain;
using PlantOps.Modules.WorkOrders.Infrastructure;

namespace PlantOps.Modules.WorkOrders.Jobs;

/// <summary>
/// Finds open work orders past their deadline and escalates each one (design 06, decision 1). Scheduled by a host
/// (Functions timer or the in-process job); all the rules live here and in <see cref="WorkOrder.Escalate"/>.
/// </summary>
internal sealed class SlaEscalationRunner(
    WorkOrdersDbContext db,
    TimeProvider time,
    ILogger<SlaEscalationRunner> logger) : ISlaEscalationRunner
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();

        // Served by IX_WorkOrders_DueAt_NotEscalated. Only ids: each order is reloaded and saved on its own below.
        var candidates = await db.WorkOrders
            .AsNoTracking()
            .Where(w => WorkOrder.OpenStatuses.Contains(w.Status) && w.DueAt < now && w.EscalatedAt == null)
            .OrderBy(w => w.DueAt)
            .Select(w => w.Id)
            .ToListAsync(cancellationToken);

        var escalated = 0;
        foreach (var id in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Fresh tracker per order: a failed save must not leave a poisoned entity behind for the next one.
            db.ChangeTracker.Clear();
            var workOrder = await db.WorkOrders.FindAsync([id], cancellationToken);

            // Re-checked on the loaded row: between the query and now a technician may have completed it.
            if (workOrder is null || !workOrder.IsEscalatable(now))
            {
                continue;
            }

            try
            {
                workOrder.Escalate(now);
                await db.SaveChangesAsync(cancellationToken);
                escalated++;
            }
            catch (DbUpdateConcurrencyException)
            {
                // Someone changed the order after we loaded it (a technician completing it, or a second runner). Not an
                // error: skip it. If it is still breached and unescalated, the next run picks it up again.
                logger.LogInformation("Work order {WorkOrderId} changed during escalation; skipped, the next run re-evaluates it", id.Value);
            }
        }

        db.ChangeTracker.Clear();
        if (escalated > 0)
        {
            logger.LogInformation("Escalated {Count} work order(s) past their SLA", escalated);
        }

        return escalated;
    }
}
