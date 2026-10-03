using Microsoft.EntityFrameworkCore;
using PlantOps.BuildingBlocks.Infrastructure;
using PlantOps.Modules.Assets.Domain;
using PlantOps.Modules.Assets.Infrastructure;
using PlantOps.Modules.Identity;
using PlantOps.Modules.Inventory.Domain;
using PlantOps.Modules.Inventory.Infrastructure;
using PlantOps.Modules.WorkOrders.Domain;
using PlantOps.Modules.WorkOrders.Infrastructure;
using PlantOps.SharedKernel;

namespace PlantOps.Api.Seeding;

internal static class DemoSeedExtensions
{
    // Registered after every module (Program.cs), and hosted services start in registration order, so each module's
    // MigrateOnStartup has already run when this one starts. The seeder is inert unless Seed:Demo is true.
    public static IServiceCollection AddDemoSeed(this IServiceCollection services) =>
        services.AddSingleton<IHostedService, DemoSeeder>();
}

// Fills an empty system with a believable Penang plant (design 08, Decision 2).
//
// Why in the host, using the modules' internals (InternalsVisibleTo "PlantOps.Api"), and not a SeedDemoAsync on each
// module: the dataset is one story that crosses modules (a work order carries an asset id, a part reservation carries
// a work order number), and no module may know the others' internals. The host is the composition root, the one place
// allowed to know them all, and the seed is a deployment concern (off by default), not module behaviour. A per-module
// seeder would have to be fed the other modules' ids through contracts that exist for no other reason.
//
// Everything goes through aggregates and SaveChanges, so audit rows and outbox messages come out exactly as in real
// use. Known limits: AuditEntries record the seeding moment and actor "System" (the audit interceptor stamps the
// clock and the current HTTP user); the real people and times are in the work orders' own columns.
internal sealed class DemoSeeder(
    IServiceProvider services,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<DemoSeeder> logger) : IHostedService
{
    public const string ConfigurationKey = "Seed:Demo";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>(ConfigurationKey))
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var plan = DemoSeedPlan.Generate(time.GetUtcNow());

        await EnsureUsersAsync(sp.GetRequiredService<IdentityDbContext>(), plan, time.GetUtcNow(), cancellationToken);

        var assetsDb = sp.GetRequiredService<AssetsDbContext>();
        if (await assetsDb.Assets.AnyAsync(cancellationToken))
        {
            // The gate is "Assets has rows", and the assets are saved first, so a boot that died half-way through the
            // seed is not repaired by the next one. The demo database is disposable: docker compose down -v.
            logger.LogInformation("Demo seed skipped: the system already has assets");
            return;
        }

        var factory = sp.GetRequiredService<FactoryClock>();
        var inventoryDb = sp.GetRequiredService<InventoryDbContext>();
        var workOrdersDb = sp.GetRequiredService<WorkOrdersDbContext>();
        var actors = plan.Users.ToDictionary(u => u.Id, u => new Actor(u.Id, u.Name));

        var parts = await SeedPartsAsync(inventoryDb, plan, cancellationToken);
        var assets = await SeedAssetsAsync(assetsDb, plan, cancellationToken);
        var scheduleIds = await SeedSchedulesAsync(workOrdersDb, plan, assets, cancellationToken);

        // Two phases, because completing a work order publishes an outbox event and the dispatcher (already polling in
        // the background) starts delivering it at once. Inventory's handler consumes the work order's reservation, and
        // it would race with reservations still being written here. Phase 1 raises everything up to "in progress" and
        // reserves the parts (no outbox events exist yet); phase 2 completes and cancels, touching WorkOrders only.
        var ids = new Dictionary<int, Guid>();
        foreach (var seed in plan.WorkOrders)
        {
            var workOrder = Raise(seed, assets, scheduleIds, actors, factory);
            workOrdersDb.WorkOrders.Add(workOrder);
            await workOrdersDb.SaveChangesAsync(cancellationToken);
            ids[seed.Key] = workOrder.Id.Value;

            if (seed.UsesParts)
            {
                foreach (var use in seed.Parts)
                {
                    parts[use.PartNumber].Reserve(
                        workOrder.Id.Value,
                        WorkOrder.FormatNumber(workOrder.Number),
                        use.Quantity,
                        actors[seed.TechnicianId],
                        use.At);
                }

                await inventoryDb.SaveChangesAsync(cancellationToken);
            }

            workOrdersDb.ChangeTracker.Clear();
        }

        foreach (var wo in plan.WorkOrders.Where(w => w.IsDone || w.Status == SeedWorkOrderStatus.Cancelled))
        {
            var workOrder = await workOrdersDb.WorkOrders.FindAsync([new WorkOrderId(ids[wo.Key])], cancellationToken)
                ?? throw new InvalidOperationException($"Seeded work order {wo.Key} disappeared.");
            if (wo.Status == SeedWorkOrderStatus.Cancelled)
            {
                workOrder.Cancel(actors[wo.SupervisorId], wo.EndedAt!.Value, wo.Reason!);
            }
            else
            {
                workOrder.Complete(actors[wo.TechnicianId], wo.CompletedAt!.Value, wo.Resolution!);
                if (wo.Status == SeedWorkOrderStatus.Closed)
                {
                    workOrder.Close(actors[wo.SupervisorId], wo.ClosedAt!.Value);
                }
            }

            await workOrdersDb.SaveChangesAsync(cancellationToken);
            workOrdersDb.ChangeTracker.Clear();
        }

        // The Assets maintenance history and the Reporting facts are built from the completion events. Those travel
        // through the outbox, so they appear a moment after startup, when the dispatcher delivers them: the seeder does
        // not write them itself, which is what keeps them consistent with real use.
        logger.LogInformation(
            "Demo seed created {Assets} assets, {Parts} spare parts, {Schedules} PM schedules and {WorkOrders} work orders",
            plan.Assets.Count,
            plan.Parts.Count,
            plan.Schedules.Count,
            plan.WorkOrders.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task EnsureUsersAsync(IdentityDbContext db, DemoSeedPlan plan, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await db.Users.Select(u => u.Id).ToListAsync(ct);
        foreach (var user in plan.Users.Where(u => !existing.Contains(u.Id)))
        {
            db.Users.Add(User.Provision(user.Id, user.Name, user.Email, [user.Role], now));
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task<Dictionary<string, SparePart>> SeedPartsAsync(InventoryDbContext db, DemoSeedPlan plan, CancellationToken ct)
    {
        var parts = new Dictionary<string, SparePart>();
        foreach (var seed in plan.Parts)
        {
            var part = SparePart.Register(seed.PartNumber, seed.Name, seed.Unit, seed.BinLocation, seed.ReorderLevel);
            part.Receive(seed.InitialOnHand);
            db.SpareParts.Add(part);
            parts[seed.PartNumber] = part;
        }

        await db.SaveChangesAsync(ct);
        return parts;
    }

    private static async Task<Dictionary<string, Asset>> SeedAssetsAsync(AssetsDbContext db, DemoSeedPlan plan, CancellationToken ct)
    {
        var assets = new Dictionary<string, Asset>();
        foreach (var seed in plan.Assets)
        {
            var asset = Asset.Register(
                AssetTag.Create(seed.Tag),
                seed.Name,
                seed.Manufacturer,
                seed.Model,
                seed.SerialNumber,
                new Location(LineFor(seed.LineCode), seed.Station),
                Enum.Parse<Criticality>(seed.Criticality),
                seed.CommissionedOn);
            db.Assets.Add(asset);
            assets[seed.Tag] = asset;
        }

        await db.SaveChangesAsync(ct);
        return assets;
    }

    private static async Task<List<Guid>> SeedSchedulesAsync(
        WorkOrdersDbContext db,
        DemoSeedPlan plan,
        Dictionary<string, Asset> assets,
        CancellationToken ct)
    {
        var ids = new List<Guid>();
        foreach (var seed in plan.Schedules)
        {
            var asset = assets[seed.AssetTag];
            var schedule = PmSchedule.Create(
                asset.Id.Value,
                seed.AssetTag,
                asset.Name,
                seed.Title,
                seed.Instructions,
                seed.IntervalDays,
                seed.LeadDays,
                Enum.Parse<WorkOrderPriority>(seed.Priority),
                seed.NextDueOn);
            db.PmSchedules.Add(schedule);
            ids.Add(schedule.Id.Value);
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return ids;
    }

    // Raises the order and walks it up to (not including) completion or cancellation.
    private static WorkOrder Raise(
        SeedWorkOrder seed,
        Dictionary<string, Asset> assets,
        List<Guid> scheduleIds,
        Dictionary<string, Actor> actors,
        FactoryClock factory)
    {
        var asset = assets[seed.AssetTag];
        var priority = Enum.Parse<WorkOrderPriority>(seed.Priority);
        var supervisor = actors[seed.SupervisorId];
        var technician = actors[seed.TechnicianId];

        var workOrder = seed.IsPreventive
            ? WorkOrder.RaisePreventive(
                asset.Id.Value,
                seed.AssetTag,
                asset.Name,
                seed.Title,
                seed.Description,
                priority,
                scheduleIds[seed.PmScheduleIndex!.Value],
                seed.PmDueOn!.Value,
                SystemActor.Instance,
                seed.SubmittedAt,
                factory.EndOfDay(seed.PmDueOn.Value))
            : WorkOrder.Submit(
                asset.Id.Value,
                seed.AssetTag,
                asset.Name,
                seed.Title,
                seed.Description,
                priority,
                seed.AssetDown,
                actors[seed.ReporterId],
                seed.SubmittedAt);

        if (seed.Status == SeedWorkOrderStatus.Rejected)
        {
            workOrder.Reject(supervisor, seed.EndedAt!.Value, seed.Reason!);
            return workOrder;
        }

        // A preventive order is born approved; a reactive one needs its approval.
        if (seed.ApprovedAt is { } approvedAt && !seed.IsPreventive)
        {
            workOrder.Approve(supervisor, approvedAt);
        }

        if (seed.AssignedAt is { } assignedAt)
        {
            workOrder.Assign(supervisor, technician, assignedAt);
        }

        if (seed.StartedAt is { } startedAt)
        {
            workOrder.Start(technician, startedAt);
        }

        return workOrder;
    }

    private static ProductionLineId LineFor(string code) => code switch
    {
        "SMT-1" => ProductionLineConfiguration.Smt1,
        "SMT-2" => ProductionLineConfiguration.Smt2,
        "FA-1" => ProductionLineConfiguration.Fa1,
        "TEST-1" => ProductionLineConfiguration.Test1,
        _ => throw new InvalidOperationException($"Unknown production line '{code}'."),
    };
}
