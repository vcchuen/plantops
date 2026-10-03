using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PlantOps.BuildingBlocks.Infrastructure;
using PlantOps.Modules.Inventory.Contracts;
using PlantOps.Modules.WorkOrders.Domain;
using PlantOps.Modules.WorkOrders.Infrastructure;

namespace PlantOps.Modules.WorkOrders.Endpoints;

internal static class QueueEndpoints
{
    private const int MaxQueueSize = 100;

    public static void Map(RouteGroupBuilder group) =>
        // The signed-in user's own work, so any signed-in user may ask (the fallback policy).
        group.MapGet("/queue", Queue);

    private static async Task<Ok<IReadOnlyList<QueueItem>>> Queue(
        [AsParameters] QueueQuery query,
        WorkOrdersDbContext db,
        ICurrentUser user,
        IReservationQueries reservations,
        TimeProvider time,
        CancellationToken ct)
    {
        if (user.Id is not { } me)
        {
            return TypedResults.Ok<IReadOnlyList<QueueItem>>([]);
        }

        var open = db.WorkOrders
            .AsNoTracking()
            .Where(w => w.AssignedToId == me && (w.Status == WorkOrderStatus.Assigned || w.Status == WorkOrderStatus.InProgress));

        if (query.DueToday)
        {
            var (start, end) = DueWindow.Today(time);
            open = open.Where(w => w.DueAt >= start && w.DueAt < end);
        }

        var rows = await open
            .OrderBy(w => w.DueAt)
            .ThenBy(w => w.Number)
            .Take(MaxQueueSize)
            .Select(w => new
            {
                Id = w.Id.Value,
                w.Number,
                w.Title,
                w.AssetTag,
                w.Priority,
                w.Status,
                w.DueAt,
                w.CompletedAt,
            })
            .ToListAsync(ct);

        var now = time.GetUtcNow();
        var items = new List<QueueItem>(rows.Count);
        foreach (var r in rows)
        {
            var partsReserved = await reservations.CountActiveAsync(r.Id, ct);
            items.Add(new QueueItem(
                r.Id,
                WorkOrder.FormatNumber(r.Number),
                r.Title,
                r.AssetTag,
                r.Priority,
                r.Status,
                r.DueAt,
                WorkOrder.ComputeSlaState(r.Status, r.Priority, r.DueAt, r.CompletedAt, now),
                partsReserved));
        }

        return TypedResults.Ok<IReadOnlyList<QueueItem>>(items);
    }
}
