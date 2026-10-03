using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PlantOps.BuildingBlocks.Infrastructure;
using PlantOps.Modules.WorkOrders.Domain;
using PlantOps.Modules.WorkOrders.Infrastructure;
using PlantOps.SharedKernel;

namespace PlantOps.Modules.WorkOrders.Endpoints;

internal static class CommentEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        // Any signed-in user (the fallback policy), so no RequireAuthorization.
        group.MapPost("/{id:guid}/comments", Add).ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/{id:guid}/comments", List).ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPut("/{id:guid}/comments/{commentId:guid}", Edit).ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Created<WorkOrderCommentItem>> Add(
        Guid id,
        CommentRequest request,
        WorkOrdersDbContext db,
        ICurrentUser user,
        TimeProvider time,
        CancellationToken ct)
    {
        var workOrderId = new WorkOrderId(id);
        if (!await db.WorkOrders.AnyAsync(w => w.Id == workOrderId, ct))
        {
            throw WorkOrderNotFound(id);
        }

        var userId = user.Id ?? throw new InvalidOperationException("No signed-in user.");
        var author = new Actor(userId, user.Name ?? userId);

        var last = await db.WorkOrderComments
            .Where(c => c.WorkOrderId == workOrderId)
            .MaxAsync(c => (int?)c.Sequence, ct);

        var comment = new WorkOrderComment(workOrderId, (last ?? 0) + 1, author, request.Body, time.GetUtcNow());
        db.WorkOrderComments.Add(comment);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/work-orders/{id}/comments/{comment.Id}", Describe(comment));
    }

    private static async Task<Ok<IReadOnlyList<WorkOrderCommentItem>>> List(
        Guid id,
        [AsParameters] ListCommentsQuery query,
        WorkOrdersDbContext db,
        CancellationToken ct)
    {
        var workOrderId = new WorkOrderId(id);
        if (!await db.WorkOrders.AnyAsync(w => w.Id == workOrderId, ct))
        {
            throw WorkOrderNotFound(id);
        }

        var comments = await db.WorkOrderComments
            .AsNoTracking()
            .Where(c => c.WorkOrderId == workOrderId)
            .OrderBy(c => c.Sequence)
            .Skip(Math.Max(query.Skip, 0))
            .Take(query.Take)
            .ToListAsync(ct);

        return TypedResults.Ok<IReadOnlyList<WorkOrderCommentItem>>([.. comments.Select(Describe)]);
    }

    private static async Task<NoContent> Edit(
        Guid id,
        Guid commentId,
        CommentRequest request,
        WorkOrdersDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var workOrderId = new WorkOrderId(id);
        var comment = await db.WorkOrderComments.FirstOrDefaultAsync(c => c.Id == commentId && c.WorkOrderId == workOrderId, ct)
            ?? throw new NotFoundException($"Comment '{commentId}' was not found on work order '{id}'.");

        comment.Edit(request.Body, time.GetUtcNow());
        await db.SaveChangesAsync(ct);

        return TypedResults.NoContent();
    }

    private static WorkOrderCommentItem Describe(WorkOrderComment c) => new(
        c.Id,
        c.Sequence,
        new PersonRef(c.AuthorId, c.AuthorName),
        c.Body,
        c.CreatedAt,
        c.EditedAt);

    private static NotFoundException WorkOrderNotFound(Guid id) => new($"Work order '{id}' was not found.");
}
