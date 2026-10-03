using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PlantOps.BuildingBlocks.Infrastructure;
using PlantOps.Modules.Assets.Contracts;
using PlantOps.Modules.Identity.Contracts;
using PlantOps.Modules.WorkOrders.Authorization;
using PlantOps.Modules.WorkOrders.Domain;
using PlantOps.Modules.WorkOrders.Infrastructure;
using PlantOps.SharedKernel;
using CommandResult = Microsoft.AspNetCore.Http.HttpResults.Results<
    Microsoft.AspNetCore.Http.HttpResults.NoContent,
    Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult,
    Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult>;

namespace PlantOps.Modules.WorkOrders.Endpoints;

internal static class WorkOrderEndpoints
{
    private const int MaxPageSize = 100;

    public static void Map(RouteGroupBuilder group)
    {
        group.ProducesProblem(StatusCodes.Status400BadRequest);

        // Raise: any signed-in user (the fallback policy), so no RequireAuthorization.
        group.MapPost("", Create);
        group.MapGet("", List);
        group.MapGet("/{id:guid}", Get).ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/{id:guid}/history", History).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/approve", Approve).WithCommandProblems().RequireAuthorization(Policies.SuperviseWorkOrders);
        group.MapPost("/{id:guid}/reject", Reject).WithCommandProblems().RequireAuthorization(Policies.SuperviseWorkOrders);
        group.MapPost("/{id:guid}/assign", Assign).WithCommandProblems().RequireAuthorization(Policies.SuperviseWorkOrders);
        group.MapPost("/{id:guid}/close", Close).WithCommandProblems().RequireAuthorization(Policies.SuperviseWorkOrders);
        group.MapPost("/{id:guid}/cancel", Cancel).WithCommandProblems().RequireAuthorization(Policies.SuperviseWorkOrders);

        // Start and complete are the assigned technician's: the resource rule runs inside the command.
        group.MapPost("/{id:guid}/start", Start).WithCommandProblems();
        group.MapPost("/{id:guid}/complete", Complete).WithCommandProblems();
    }

    private static RouteHandlerBuilder WithCommandProblems(this RouteHandlerBuilder builder) => builder
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status412PreconditionFailed)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

    // ---- raise and read ----

    private static async Task<Created<WorkOrderDetail>> Create(
        CreateWorkOrderRequest request,
        HttpContext http,
        WorkOrdersDbContext db,
        IAssetDirectory assets,
        ICurrentUser user,
        IAuthorizationService authorization,
        TimeProvider time,
        CancellationToken ct)
    {
        var asset = await assets.FindAsync(request.AssetId, ct)
            ?? throw new DomainException($"Asset '{request.AssetId}' does not exist.");
        if (!asset.IsInService)
        {
            throw new DomainException($"Asset {asset.Tag} is decommissioned; work cannot be raised on it.");
        }

        var workOrder = WorkOrder.Submit(
            asset.Id,
            asset.Tag,
            asset.Name,
            request.Title,
            request.Description,
            request.Priority,
            request.AssetDown,
            ActorOf(user),
            time.GetUtcNow());

        db.WorkOrders.Add(workOrder);
        await db.SaveChangesAsync(ct);

        http.Response.Headers.ETag = IfMatch.ETag(workOrder.RowVersion);
        var detail = await Describe(workOrder, http.User, authorization, time);
        return TypedResults.Created($"/api/work-orders/{workOrder.Id.Value}", detail);
    }

    private static async Task<Ok<PagedResponse<WorkOrderListItem>>> List(
        [AsParameters] ListWorkOrdersQuery query,
        WorkOrdersDbContext db,
        ICurrentUser user,
        TimeProvider time,
        CancellationToken ct)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        var workOrders = db.WorkOrders.AsNoTracking();
        if (query.Status is { } status)
        {
            workOrders = workOrders.Where(w => w.Status == status);
        }

        if (query.Priority is { } priority)
        {
            workOrders = workOrders.Where(w => w.Priority == priority);
        }

        if (query.AssetId is { } assetId)
        {
            workOrders = workOrders.Where(w => w.AssetId == assetId);
        }

        if (query.Mine)
        {
            var me = user.Id;
            workOrders = me is null ? workOrders.Where(_ => false) : workOrders.Where(w => w.AssignedToId == me);
        }

        var totalCount = await workOrders.CountAsync(ct);

        // One query for the page: the snapshots (asset tag, people names) live on the row, so nothing is joined or
        // lazy-loaded per item. Number is formatted and SlaState derived below, not in SQL.
        var rows = await workOrders
            .OrderByDescending(w => w.SubmittedAt)
            .ThenByDescending(w => w.Number)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(w => new
            {
                Id = w.Id.Value,
                w.Number,
                w.AssetId,
                w.AssetTag,
                w.AssetName,
                w.Title,
                w.Priority,
                w.Status,
                w.AssignedToId,
                w.AssignedToName,
                w.SubmittedAt,
                w.CompletedAt,
                w.DueAt,
            })
            .ToListAsync(ct);

        // Computed in memory because it depends on "now" and on the 25 % rule: a SQL version would duplicate the
        // domain rule (and its boundary cases) in a CASE expression that no unit test could reach, and a stored
        // column would be stale the moment the clock moves.
        var now = time.GetUtcNow();
        var items = rows.Select(r => new WorkOrderListItem(
                r.Id,
                WorkOrder.FormatNumber(r.Number),
                r.AssetId,
                r.AssetTag,
                r.AssetName,
                r.Title,
                r.Priority,
                r.Status,
                WorkOrder.ComputeSlaState(r.Status, r.Priority, r.SubmittedAt, r.CompletedAt, now),
                r.AssignedToId is null ? null : new PersonRef(r.AssignedToId, r.AssignedToName ?? r.AssignedToId),
                r.SubmittedAt,
                r.DueAt))
            .ToList();

        return TypedResults.Ok(new PagedResponse<WorkOrderListItem>(items, page, pageSize, totalCount));
    }

    private static async Task<Ok<WorkOrderDetail>> Get(
        Guid id,
        HttpContext http,
        WorkOrdersDbContext db,
        IAuthorizationService authorization,
        TimeProvider time,
        CancellationToken ct)
    {
        var workOrder = await db.WorkOrders.AsNoTracking().FirstOrDefaultAsync(w => w.Id == new WorkOrderId(id), ct)
            ?? throw NotFound(id);

        http.Response.Headers.ETag = IfMatch.ETag(workOrder.RowVersion);
        return TypedResults.Ok(await Describe(workOrder, http.User, authorization, time));
    }

    private static async Task<Ok<IReadOnlyList<AuditHistoryItem>>> History(Guid id, WorkOrdersDbContext db, CancellationToken ct)
    {
        if (!await db.WorkOrders.AnyAsync(w => w.Id == new WorkOrderId(id), ct))
        {
            throw NotFound(id);
        }

        return TypedResults.Ok(await db.ForAggregateAsync(id.ToString(), ct));
    }

    // ---- commands ----

    private static Task<CommandResult> Approve(
        Guid id,
        ApproveRequest? request,
        HttpContext http,
        WorkOrdersDbContext db,
        ICurrentUser user,
        IAuthorizationService authorization,
        TimeProvider time,
        CancellationToken ct) =>
        Execute(id, http, db, user, authorization, time, requiresWorkRule: false, ct,
            (w, actor, now) =>
            {
                w.Approve(actor, now, request?.Priority);
                return Task.CompletedTask;
            });

    private static Task<CommandResult> Reject(
        Guid id,
        ReasonRequest request,
        HttpContext http,
        WorkOrdersDbContext db,
        ICurrentUser user,
        IAuthorizationService authorization,
        TimeProvider time,
        CancellationToken ct) =>
        Execute(id, http, db, user, authorization, time, requiresWorkRule: false, ct,
            (w, actor, now) =>
            {
                w.Reject(actor, now, request.Reason);
                return Task.CompletedTask;
            });

    private static Task<CommandResult> Assign(
        Guid id,
        AssignRequest request,
        HttpContext http,
        WorkOrdersDbContext db,
        ICurrentUser user,
        IAuthorizationService authorization,
        IUserDirectory users,
        TimeProvider time,
        CancellationToken ct) =>
        Execute(id, http, db, user, authorization, time, requiresWorkRule: false, ct,
            async (w, actor, now) =>
            {
                // Validated against the directory, not trusted from the body: only a known technician can be assigned.
                var technician = string.IsNullOrWhiteSpace(request.TechnicianId)
                    ? null
                    : await users.FindAsync(request.TechnicianId, ct);
                if (technician is null || !technician.Roles.Contains(Roles.Technician))
                {
                    throw new DomainException($"'{request.TechnicianId}' is not a known technician.");
                }

                w.Assign(actor, new Actor(technician.Id, technician.Name), now);
            });

    private static Task<CommandResult> Start(
        Guid id,
        HttpContext http,
        WorkOrdersDbContext db,
        ICurrentUser user,
        IAuthorizationService authorization,
        TimeProvider time,
        CancellationToken ct) =>
        Execute(id, http, db, user, authorization, time, requiresWorkRule: true, ct,
            (w, actor, now) =>
            {
                w.Start(actor, now);
                return Task.CompletedTask;
            });

    private static Task<CommandResult> Complete(
        Guid id,
        CompleteRequest request,
        HttpContext http,
        WorkOrdersDbContext db,
        ICurrentUser user,
        IAuthorizationService authorization,
        TimeProvider time,
        CancellationToken ct) =>
        Execute(id, http, db, user, authorization, time, requiresWorkRule: true, ct,
            (w, actor, now) =>
            {
                w.Complete(actor, now, request.Resolution);
                return Task.CompletedTask;
            });

    private static Task<CommandResult> Close(
        Guid id,
        HttpContext http,
        WorkOrdersDbContext db,
        ICurrentUser user,
        IAuthorizationService authorization,
        TimeProvider time,
        CancellationToken ct) =>
        Execute(id, http, db, user, authorization, time, requiresWorkRule: false, ct,
            (w, actor, now) =>
            {
                w.Close(actor, now);
                return Task.CompletedTask;
            });

    private static Task<CommandResult> Cancel(
        Guid id,
        ReasonRequest request,
        HttpContext http,
        WorkOrdersDbContext db,
        ICurrentUser user,
        IAuthorizationService authorization,
        TimeProvider time,
        CancellationToken ct) =>
        Execute(id, http, db, user, authorization, time, requiresWorkRule: false, ct,
            (w, actor, now) =>
            {
                w.Cancel(actor, now, request.Reason);
                return Task.CompletedTask;
            });

    // The one pipeline every command shares: precondition header, load, resource rule, stale check, domain method,
    // save with the client's version as the concurrency token, new ETag.
    private static async Task<CommandResult> Execute(
        Guid id,
        HttpContext http,
        WorkOrdersDbContext db,
        ICurrentUser user,
        IAuthorizationService authorization,
        TimeProvider time,
        bool requiresWorkRule,
        CancellationToken ct,
        Func<WorkOrder, Actor, DateTimeOffset, Task> apply)
    {
        var (clientVersion, problem) = IfMatch.Read(http.Request);
        if (clientVersion is null)
        {
            // Read returns exactly one of the two.
            return problem!;
        }

        var workOrder = await db.WorkOrders.FindAsync([new WorkOrderId(id)], ct) ?? throw NotFound(id);

        if (requiresWorkRule && !(await authorization.AuthorizeAsync(http.User, workOrder, WorkOrderOperations.Work)).Succeeded)
        {
            return TypedResults.Forbid();
        }

        // Compared before the domain method on purpose: a stale client re-approving an already approved order should
        // hear "reload" (412), not a confusing business-rule 400. EF's WHERE RowVersion = @original below still
        // covers the race between this check and the UPDATE.
        if (!workOrder.RowVersion.AsSpan().SequenceEqual(clientVersion))
        {
            return PreconditionFailed();
        }

        await apply(workOrder, ActorOf(user), time.GetUtcNow());

        db.Entry(workOrder).Property(w => w.RowVersion).OriginalValue = clientVersion;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return PreconditionFailed();
        }

        // EF reads the bumped rowversion back with the UPDATE, so the client can chain its next command without a GET.
        http.Response.Headers.ETag = IfMatch.ETag(workOrder.RowVersion);
        return TypedResults.NoContent();
    }

    // ---- helpers ----

    private static ProblemHttpResult PreconditionFailed() => TypedResults.Problem(
        statusCode: StatusCodes.Status412PreconditionFailed,
        title: "Precondition failed",
        detail: "The work order was changed by someone else since you read it. Reload and try again.");

    private static NotFoundException NotFound(Guid id) => new($"Work order '{id}' was not found.");

    // The audit interceptor reads the same ICurrentUser, so snapshots and audit rows always name the same person.
    private static Actor ActorOf(ICurrentUser user)
    {
        var id = user.Id ?? throw new InvalidOperationException("No signed-in user.");
        return new Actor(id, user.Name ?? id);
    }

    private static async Task<WorkOrderDetail> Describe(
        WorkOrder w,
        System.Security.Claims.ClaimsPrincipal principal,
        IAuthorizationService authorization,
        TimeProvider time)
    {
        // Reuse the real policy and the real resource handler so what the UI is told matches what the commands enforce.
        var canSupervise = (await authorization.AuthorizeAsync(principal, Policies.SuperviseWorkOrders)).Succeeded;
        var canWork = (await authorization.AuthorizeAsync(principal, w, WorkOrderOperations.Work)).Succeeded;

        return new WorkOrderDetail(
            w.Id.Value,
            WorkOrder.FormatNumber(w.Number),
            w.AssetId,
            w.AssetTag,
            w.AssetName,
            w.Title,
            w.Description,
            w.Priority,
            w.AssetDown,
            w.Status,
            w.SlaStateAt(time.GetUtcNow()),
            new PersonRef(w.ReportedById, w.ReportedByName),
            w.ApprovedById is null ? null : new PersonRef(w.ApprovedById, w.ApprovedByName ?? w.ApprovedById),
            w.AssignedToId is null ? null : new PersonRef(w.AssignedToId, w.AssignedToName ?? w.AssignedToId),
            w.SubmittedAt,
            w.ApprovedAt,
            w.StartedAt,
            w.CompletedAt,
            w.ClosedAt,
            w.DueAt,
            w.Resolution,
            w.RejectionReason,
            w.CancellationReason,
            WorkOrderAccess.AllowedActions(w.Status, canSupervise, canWork));
    }
}
