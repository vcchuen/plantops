using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PlantOps.Modules.Assets.Contracts;
using PlantOps.Modules.Identity.Contracts;
using PlantOps.Modules.WorkOrders.Domain;
using PlantOps.Modules.WorkOrders.Infrastructure;
using PlantOps.SharedKernel;
using CommandResult = Microsoft.AspNetCore.Http.HttpResults.Results<
    Microsoft.AspNetCore.Http.HttpResults.NoContent,
    Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>;

namespace PlantOps.Modules.WorkOrders.Endpoints;

// Same conventions as work orders: strong ETag from the rowversion, If-Match required on every change (412 stale,
// 428 missing), the new ETag returned so a client can chain commands. Reads are open to any signed-in user;
// every write needs the supervise policy.
internal static class PmScheduleEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("", List);
        group.MapGet("/{id:guid}", Get).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("", Create).RequireAuthorization(Policies.SuperviseWorkOrders);
        group.MapPut("/{id:guid}", Update).WithCommandProblems().RequireAuthorization(Policies.SuperviseWorkOrders);
        group.MapPost("/{id:guid}/deactivate", Deactivate).WithCommandProblems().RequireAuthorization(Policies.SuperviseWorkOrders);
        group.MapPost("/{id:guid}/activate", Activate).WithCommandProblems().RequireAuthorization(Policies.SuperviseWorkOrders);
    }

    private static RouteHandlerBuilder WithCommandProblems(this RouteHandlerBuilder builder) => builder
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status412PreconditionFailed)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

    private static async Task<Ok<IReadOnlyList<PmScheduleListItem>>> List(
        [AsParameters] ListPmSchedulesQuery query,
        WorkOrdersDbContext db,
        CancellationToken ct)
    {
        var schedules = db.PmSchedules.AsNoTracking();
        if (query.AssetId is { } assetId)
        {
            schedules = schedules.Where(s => s.AssetId == assetId);
        }

        if (query.Active is { } active)
        {
            schedules = schedules.Where(s => s.IsActive == active);
        }

        // A plain list, not a page: a factory has tens of schedules, not thousands.
        var rows = await schedules
            .OrderBy(s => s.NextDueOn)
            .ThenBy(s => s.AssetTag)
            .ThenBy(s => s.Title)
            .Select(s => new PmScheduleListItem(
                s.Id.Value,
                s.AssetId,
                s.AssetTag,
                s.AssetName,
                s.Title,
                s.IntervalDays,
                s.LeadDays,
                s.Priority,
                s.NextDueOn,
                s.IsActive))
            .ToListAsync(ct);

        return TypedResults.Ok<IReadOnlyList<PmScheduleListItem>>(rows);
    }

    private static async Task<Ok<PmScheduleDetail>> Get(Guid id, HttpContext http, WorkOrdersDbContext db, CancellationToken ct)
    {
        var schedule = await db.PmSchedules.AsNoTracking().FirstOrDefaultAsync(s => s.Id == new PmScheduleId(id), ct)
            ?? throw NotFound(id);

        http.Response.Headers.ETag = IfMatch.ETag(schedule.RowVersion);
        return TypedResults.Ok(Describe(schedule));
    }

    private static async Task<Created<PmScheduleDetail>> Create(
        CreatePmScheduleRequest request,
        HttpContext http,
        WorkOrdersDbContext db,
        IAssetDirectory assets,
        CancellationToken ct)
    {
        var asset = await assets.FindAsync(request.AssetId, ct)
            ?? throw new DomainException($"Asset '{request.AssetId}' does not exist.");
        if (!asset.IsInService)
        {
            throw new DomainException($"Asset {asset.Tag} is decommissioned; maintenance cannot be scheduled on it.");
        }

        var schedule = PmSchedule.Create(
            asset.Id,
            asset.Tag,
            asset.Name,
            request.Title,
            request.Instructions,
            request.IntervalDays,
            request.LeadDays,
            request.Priority,
            request.NextDueOn);

        db.PmSchedules.Add(schedule);
        await db.SaveChangesAsync(ct);

        http.Response.Headers.ETag = IfMatch.ETag(schedule.RowVersion);
        return TypedResults.Created($"/api/pm-schedules/{schedule.Id.Value}", Describe(schedule));
    }

    private static Task<CommandResult> Update(Guid id, UpdatePmScheduleRequest request, HttpContext http, WorkOrdersDbContext db, CancellationToken ct) =>
        Execute(id, http, db, ct, s => s.Update(
            request.Title,
            request.Instructions,
            request.IntervalDays,
            request.LeadDays,
            request.Priority,
            request.NextDueOn));

    private static Task<CommandResult> Deactivate(Guid id, HttpContext http, WorkOrdersDbContext db, CancellationToken ct) =>
        Execute(id, http, db, ct, s => s.Deactivate());

    private static Task<CommandResult> Activate(Guid id, HttpContext http, WorkOrdersDbContext db, CancellationToken ct) =>
        Execute(id, http, db, ct, s => s.Activate());

    // The work-order pipeline in miniature (no resource rule here): precondition header, load, stale check, domain
    // method, save with the client's version as the concurrency token, new ETag.
    private static async Task<CommandResult> Execute(
        Guid id,
        HttpContext http,
        WorkOrdersDbContext db,
        CancellationToken ct,
        Action<PmSchedule> apply)
    {
        var (clientVersion, problem) = IfMatch.Read(http.Request);
        if (clientVersion is null)
        {
            // Read returns exactly one of the two.
            return problem!;
        }

        var schedule = await db.PmSchedules.FindAsync([new PmScheduleId(id)], ct) ?? throw NotFound(id);

        // Before the domain method: a stale client should hear "reload" (412), not a business-rule 400.
        if (!schedule.RowVersion.AsSpan().SequenceEqual(clientVersion))
        {
            return PreconditionFailed();
        }

        apply(schedule);

        // The runner also writes this row (it advances NextDueOn), so this check is not theoretical.
        db.Entry(schedule).Property(s => s.RowVersion).OriginalValue = clientVersion;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return PreconditionFailed();
        }

        http.Response.Headers.ETag = IfMatch.ETag(schedule.RowVersion);
        return TypedResults.NoContent();
    }

    private static ProblemHttpResult PreconditionFailed() => TypedResults.Problem(
        statusCode: StatusCodes.Status412PreconditionFailed,
        title: "Precondition failed",
        detail: "The schedule was changed by someone else since you read it. Reload and try again.");

    private static NotFoundException NotFound(Guid id) => new($"PM schedule '{id}' was not found.");

    private static PmScheduleDetail Describe(PmSchedule s) => new(
        s.Id.Value,
        s.AssetId,
        s.AssetTag,
        s.AssetName,
        s.Title,
        s.IntervalDays,
        s.LeadDays,
        s.Priority,
        s.NextDueOn,
        s.IsActive,
        s.Instructions);
}
