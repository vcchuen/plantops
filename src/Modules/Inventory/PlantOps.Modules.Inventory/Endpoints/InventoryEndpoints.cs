using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlantOps.BuildingBlocks.Infrastructure;
using PlantOps.Modules.Identity.Contracts;
using PlantOps.Modules.Inventory.Domain;
using PlantOps.Modules.Inventory.Infrastructure;
using PlantOps.Modules.WorkOrders.Contracts;
using PlantOps.SharedKernel;

namespace PlantOps.Modules.Inventory.Endpoints;

internal static class InventoryEndpoints
{
    private const int MaxPageSize = 100;
    private const int MaxReservationRows = 200;

    public static void Map(RouteGroupBuilder group)
    {
        group.ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/parts", ListParts);
        group.MapGet("/parts/{id:guid}", GetPart).ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/parts", CreatePart).ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(Policies.ManageInventory);
        group.MapPost("/parts/{id:guid}/receive", ReceiveStock).ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(Policies.ManageInventory);

        group.MapGet("/reservations", ListReservations);

        // Reserve and release are the assigned technician's or a supervisor's: the rule needs the work order,
        // so it runs inside the handler (resource-based), not as a role policy.
        group.MapPost("/reservations", Reserve)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPost("/reservations/{id:guid}/release", Release)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    // ---- reads ----

    private static async Task<Ok<PagedResponse<PartListItem>>> ListParts(
        [AsParameters] ListPartsQuery query,
        InventoryDbContext db,
        CancellationToken ct)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        var parts = db.SpareParts.AsNoTracking();
        if (query.Search?.Trim() is { Length: > 0 } search)
        {
            // Part numbers are stored upper-case, so the prefix is upper-cased to stay index-friendly.
            var prefix = search.ToUpperInvariant();
            parts = parts.Where(p => p.PartNumber.StartsWith(prefix) || p.Name.Contains(search));
        }

        if (query.LowStock)
        {
            // Written out, not via IsLowStock: derived properties are not columns, so EF cannot translate them.
            parts = parts.Where(p => p.QuantityOnHand - p.QuantityReserved <= p.ReorderLevel);
        }

        var totalCount = await parts.CountAsync(ct);
        var items = await parts
            .OrderBy(p => p.PartNumber)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new PartListItem(
                p.Id.Value,
                p.PartNumber,
                p.Name,
                p.Unit,
                p.BinLocation,
                p.QuantityOnHand,
                p.QuantityReserved,
                p.QuantityOnHand - p.QuantityReserved,
                p.ReorderLevel,
                p.QuantityOnHand - p.QuantityReserved <= p.ReorderLevel))
            .ToListAsync(ct);

        return TypedResults.Ok(new PagedResponse<PartListItem>(items, page, pageSize, totalCount));
    }

    private static async Task<Ok<PartDetail>> GetPart(Guid id, InventoryDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await QueryDetail(db, new SparePartId(id), ct) ?? throw PartNotFound(id));

    // A plain array, as the API contract (design 05) says: the SPA reads it as ReservationItem[]. It once returned a
    // paged envelope; backend and frontend tests each passed against their own assumption and only E2E caught it.
    private static async Task<Ok<List<ReservationItem>>> ListReservations(
        [AsParameters] ListReservationsQuery query,
        InventoryDbContext db,
        CancellationToken ct)
    {
        var reservations = db.Reservations.AsNoTracking();
        reservations = query.WorkOrderId is { } workOrderId
            ? reservations.Where(r => r.WorkOrderId == workOrderId)
            : reservations.Where(r => r.Status == ReservationStatus.Active);

        var items = await (
            from r in reservations
            join p in db.SpareParts on r.SparePartId equals p.Id
            orderby r.ReservedAt
            select new ReservationItem(
                r.Id,
                p.Id.Value,
                p.PartNumber,
                p.Name,
                p.Unit,
                r.Quantity,
                r.Status,
                r.ReservedAt,
                r.ReservedByName))
            .Take(MaxReservationRows)
            .ToListAsync(ct);

        return TypedResults.Ok(items);
    }

    // ---- stock commands (managers) ----

    private static async Task<Created<PartDetail>> CreatePart(
        CreatePartRequest request,
        InventoryDbContext db,
        CancellationToken ct)
    {
        var part = SparePart.Register(request.PartNumber, request.Name, request.Unit, request.BinLocation, request.ReorderLevel);

        db.SpareParts.Add(part);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (UniqueViolation.Is(ex))
        {
            // No "does it exist?" pre-check: two concurrent requests would both pass it. The unique index decides.
            throw new ConflictException($"Part number '{part.PartNumber}' already exists");
        }

        var detail = (await QueryDetail(db, part.Id, ct))!;
        return TypedResults.Created($"/api/inventory/parts/{part.Id.Value}", detail);
    }

    private static async Task<NoContent> ReceiveStock(
        Guid id,
        ReceiveRequest request,
        IServiceScopeFactory scopes,
        CancellationToken ct)
    {
        // Receiving adds to the shelf regardless of what else changed it meanwhile, so a concurrent update is
        // retried silently (same policy as reserving), never surfaced as a conflict.
        await ConcurrencyRetry.RunAsync(scopes, async (sp, token) =>
        {
            var db = sp.GetRequiredService<InventoryDbContext>();
            var part = await db.SpareParts.FirstOrDefaultAsync(p => p.Id == new SparePartId(id), token)
                ?? throw PartNotFound(id);
            part.Receive(request.Quantity);
            await db.SaveChangesAsync(token);
            return true;
        }, ct);

        return TypedResults.NoContent();
    }

    // ---- reservations ----

    private static async Task<Results<Created<ReservationItem>, ForbidHttpResult>> Reserve(
        ReserveRequest request,
        HttpContext http,
        IWorkOrderDirectory workOrders,
        IAuthorizationService authorization,
        ICurrentUser user,
        IServiceScopeFactory scopes,
        TimeProvider time,
        CancellationToken ct)
    {
        var workOrder = await workOrders.FindAsync(request.WorkOrderId, ct)
            ?? throw new DomainException($"Work order '{request.WorkOrderId}' does not exist.");

        if (!await MayWork(http, authorization, user, workOrder))
        {
            return TypedResults.Forbid();
        }

        if (workOrder.Status is not ("Assigned" or "InProgress"))
        {
            throw new DomainException(
                $"Parts can only be reserved for an assigned or in-progress work order; {workOrder.Number} is {workOrder.Status}.");
        }

        var actor = ActorOf(user);

        // Automatic retry on a concurrent update (RowVersion), up to 3 attempts. Contrast with M4's 412: there a human
        // decided from stale data and must look again; here the server decides ("is there stock?"), so it can simply
        // re-decide on fresh data. The loser of a last-unit race re-runs, sees Available = 0 and gets a clean 409.
        var item = await ConcurrencyRetry.RunAsync(scopes, async (sp, token) =>
        {
            var db = sp.GetRequiredService<InventoryDbContext>();
            var partId = new SparePartId(request.PartId);

            // Only Active reservations are loaded: the part may have years of consumed/released history.
            var part = await db.SpareParts
                .Include(p => p.Reservations.Where(r => r.Status == ReservationStatus.Active))
                .FirstOrDefaultAsync(p => p.Id == partId, token)
                ?? throw PartNotFound(request.PartId);

            var reservation = part.Reserve(request.WorkOrderId, workOrder.Number, request.Quantity, actor, time.GetUtcNow());
            await db.SaveChangesAsync(token);

            return new ReservationItem(
                reservation.Id,
                part.Id.Value,
                part.PartNumber,
                part.Name,
                part.Unit,
                reservation.Quantity,
                reservation.Status,
                reservation.ReservedAt,
                reservation.ReservedByName);
        }, ct);

        return TypedResults.Created($"/api/inventory/reservations?workOrderId={request.WorkOrderId}", item);
    }

    private static async Task<Results<NoContent, ForbidHttpResult>> Release(
        Guid id,
        HttpContext http,
        InventoryDbContext db,
        IWorkOrderDirectory workOrders,
        IAuthorizationService authorization,
        ICurrentUser user,
        IServiceScopeFactory scopes,
        TimeProvider time,
        CancellationToken ct)
    {
        var target = await db.Reservations
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new { r.WorkOrderId, r.SparePartId })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException($"Reservation '{id}' was not found.");

        // A vanished work order leaves only the managers able to release.
        var workOrder = await workOrders.FindAsync(target.WorkOrderId, ct);
        if (!await MayWork(http, authorization, user, workOrder))
        {
            return TypedResults.Forbid();
        }

        await ConcurrencyRetry.RunAsync(scopes, async (sp, token) =>
        {
            var scoped = sp.GetRequiredService<InventoryDbContext>();
            var part = await scoped.SpareParts
                .Include(p => p.Reservations.Where(r => r.Id == id))
                .FirstAsync(p => p.Id == target.SparePartId, token);
            part.Release(id, time.GetUtcNow());
            await scoped.SaveChangesAsync(token);
            return true;
        }, ct);

        return TypedResults.NoContent();
    }

    // ---- helpers ----

    // Managers (supervisor/admin) or the technician the work order is assigned to.
    private static async Task<bool> MayWork(
        HttpContext http,
        IAuthorizationService authorization,
        ICurrentUser user,
        WorkOrderSummary? workOrder)
    {
        if ((await authorization.AuthorizeAsync(http.User, Policies.ManageInventory)).Succeeded)
        {
            return true;
        }

        return workOrder?.AssignedToId is { } assignee && !string.IsNullOrEmpty(user.Id) && assignee == user.Id;
    }

    private static NotFoundException PartNotFound(Guid id) => new($"Spare part '{id}' was not found.");

    private static Actor ActorOf(ICurrentUser user)
    {
        var id = user.Id ?? throw new InvalidOperationException("No signed-in user.");
        return new Actor(id, user.Name ?? id);
    }

    private static async Task<PartDetail?> QueryDetail(InventoryDbContext db, SparePartId id, CancellationToken ct)
    {
        var part = await db.SpareParts
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new PartListItem(
                p.Id.Value,
                p.PartNumber,
                p.Name,
                p.Unit,
                p.BinLocation,
                p.QuantityOnHand,
                p.QuantityReserved,
                p.QuantityOnHand - p.QuantityReserved,
                p.ReorderLevel,
                p.QuantityOnHand - p.QuantityReserved <= p.ReorderLevel))
            .FirstOrDefaultAsync(ct);
        if (part is null)
        {
            return null;
        }

        var reservations = await db.Reservations
            .AsNoTracking()
            .Where(r => r.SparePartId == id && r.Status == ReservationStatus.Active)
            .OrderBy(r => r.ReservedAt)
            .Select(r => new PartReservationItem(r.Id, r.WorkOrderId, r.WorkOrderNumber, r.Quantity, r.Status, r.ReservedAt, r.ReservedByName))
            .ToListAsync(ct);

        return new PartDetail(
            part.Id,
            part.PartNumber,
            part.Name,
            part.Unit,
            part.BinLocation,
            part.QuantityOnHand,
            part.QuantityReserved,
            part.QuantityAvailable,
            part.ReorderLevel,
            part.IsLowStock,
            reservations);
    }
}
