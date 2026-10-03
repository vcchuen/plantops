using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PlantOps.Modules.Assets.Domain;
using PlantOps.Modules.Assets.Infrastructure;
using PlantOps.Modules.Identity.Contracts;
using PlantOps.SharedKernel;

namespace PlantOps.Modules.Assets.Endpoints;

internal static class AssetEndpoints
{
    private const int MaxPageSize = 100;

    // SQL Server error numbers for unique index (2601) and unique constraint (2627) violations.
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    public static void Map(RouteGroupBuilder group)
    {
        group.ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/lines", ListLines);
        group.MapGet("", ListAssets);
        group.MapGet("/{id:guid}", GetAsset).ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("", RegisterAsset).ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(Policies.ManageAssets);
        group.MapPut("/{id:guid}/details", UpdateDetails).ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(Policies.ManageAssets);
        group.MapPost("/{id:guid}/relocate", Relocate).ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(Policies.ManageAssets);
        group.MapPost("/{id:guid}/criticality", ChangeCriticality).ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(Policies.ManageAssets);
        group.MapPost("/{id:guid}/decommission", Decommission).ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(Policies.ManageAssets);
    }

    private static async Task<Ok<LineResponse[]>> ListLines(AssetsDbContext db, CancellationToken ct)
    {
        var lines = await db.ProductionLines
            .AsNoTracking()
            .OrderBy(l => l.Code)
            .Select(l => new LineResponse(l.Id.Value, l.Code, l.Name))
            .ToArrayAsync(ct);

        return TypedResults.Ok(lines);
    }

    private static async Task<Ok<PagedResponse<AssetListItem>>> ListAssets(
        [AsParameters] ListAssetsQuery query,
        AssetsDbContext db,
        CancellationToken ct)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        var assets = db.Assets.AsNoTracking();
        if (query.LineId is { } lineGuid)
        {
            var lineId = new ProductionLineId(lineGuid);
            assets = assets.Where(a => a.LineId == lineId);
        }

        if (query.Criticality is { } criticality)
        {
            assets = assets.Where(a => a.Criticality == criticality);
        }

        if (query.Status is { } status)
        {
            assets = assets.Where(a => a.Status == status);
        }

        if (query.Search?.Trim() is { Length: > 0 } search)
        {
            // Tags are stored upper-case, so the prefix is upper-cased to stay index-friendly (a LIKE 'X%' seek).
            // StartsWith/Contains make EF escape the LIKE wildcards (% _ [) in user input, so searching "50%"
            // matches the literal text instead of everything. Not an injection concern (it is parameterised),
            // purely about result correctness.
            var tagPrefix = search.ToUpperInvariant();
            assets = assets.Where(a => EF.Property<string>(a, Asset.TagField).StartsWith(tagPrefix) || a.Name.Contains(search));
        }

        // Count runs on the filtered assets alone (no join): the FK guarantees every asset has a line.
        var totalCount = await assets.CountAsync(ct);

        // Query syntax so OrderBy applies to the entity column before the DTO projection (EF cannot translate
        // an OrderBy over a member of a constructed DTO).
        var items = await (
            from a in assets
            join l in db.ProductionLines on a.LineId equals l.Id
            orderby EF.Property<string>(a, Asset.TagField)
            select new AssetListItem(
                a.Id.Value,
                EF.Property<string>(a, Asset.TagField),
                a.Name,
                l.Id.Value,
                l.Name,
                a.Station,
                a.Criticality,
                a.Status))
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return TypedResults.Ok(new PagedResponse<AssetListItem>(items, page, pageSize, totalCount));
    }

    private static async Task<Ok<AssetDetail>> GetAsset(Guid id, AssetsDbContext db, CancellationToken ct)
    {
        var detail = await QueryDetail(db, new AssetId(id), ct)
            ?? throw new NotFoundException($"Asset '{id}' was not found.");
        return TypedResults.Ok(detail);
    }

    private static async Task<Created<AssetDetail>> RegisterAsset(
        RegisterAssetRequest request,
        AssetsDbContext db,
        CancellationToken ct)
    {
        var tag = AssetTag.Create(request.Tag);
        var location = new Location(new ProductionLineId(request.LineId), request.Station);
        await EnsureLineExists(db, location.LineId, ct);

        var asset = Asset.Register(
            tag,
            request.Name,
            request.Manufacturer,
            request.Model,
            request.SerialNumber,
            location,
            request.Criticality,
            request.CommissionedOn);

        db.Assets.Add(asset);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: UniqueIndexViolation or UniqueConstraintViolation })
        {
            // No "does it exist?" pre-check: two concurrent requests would both pass it. The unique index is
            // the only atomic arbiter, so we let it decide and translate its verdict.
            throw new ConflictException($"Asset tag '{tag}' already exists");
        }

        var detail = (await QueryDetail(db, asset.Id, ct))!;
        return TypedResults.Created($"/api/assets/{asset.Id.Value}", detail);
    }

    private static async Task<NoContent> UpdateDetails(
        Guid id,
        UpdateDetailsRequest request,
        AssetsDbContext db,
        CancellationToken ct)
    {
        var asset = await LoadAsset(db, id, ct);
        asset.UpdateDetails(request.Name, request.Manufacturer, request.Model, request.SerialNumber);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> Relocate(
        Guid id,
        RelocateRequest request,
        AssetsDbContext db,
        CancellationToken ct)
    {
        var asset = await LoadAsset(db, id, ct);
        var location = new Location(new ProductionLineId(request.LineId), request.Station);
        await EnsureLineExists(db, location.LineId, ct);
        asset.Relocate(location);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> ChangeCriticality(
        Guid id,
        ChangeCriticalityRequest request,
        AssetsDbContext db,
        CancellationToken ct)
    {
        var asset = await LoadAsset(db, id, ct);
        asset.ChangeCriticality(request.Criticality);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> Decommission(
        Guid id,
        DecommissionRequest request,
        AssetsDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var asset = await LoadAsset(db, id, ct);
        // UTC date for now; which calendar day "today" means for a Penang factory is a later policy decision.
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        asset.Decommission(request.On, request.Reason, today);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Asset> LoadAsset(AssetsDbContext db, Guid id, CancellationToken ct) =>
        await db.Assets.FindAsync([new AssetId(id)], ct)
        ?? throw new NotFoundException($"Asset '{id}' was not found.");

    private static async Task EnsureLineExists(AssetsDbContext db, ProductionLineId lineId, CancellationToken ct)
    {
        if (!await db.ProductionLines.AnyAsync(l => l.Id == lineId, ct))
        {
            throw new DomainException($"Production line '{lineId.Value}' does not exist.");
        }
    }

    private static Task<AssetDetail?> QueryDetail(AssetsDbContext db, AssetId id, CancellationToken ct) =>
        db.Assets
            .AsNoTracking()
            .Where(a => a.Id == id)
            .Join(db.ProductionLines, a => a.LineId, l => l.Id, (a, l) => new AssetDetail(
                a.Id.Value,
                EF.Property<string>(a, Asset.TagField),
                a.Name,
                a.Manufacturer,
                a.Model,
                a.SerialNumber,
                l.Id.Value,
                l.Code,
                l.Name,
                a.Station,
                a.Criticality,
                a.Status,
                a.CommissionedOn,
                a.DecommissionedOn,
                a.DecommissionReason))
            .FirstOrDefaultAsync(ct);
}
