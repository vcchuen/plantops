using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlantOps.Api.Seeding;
using PlantOps.BuildingBlocks.Infrastructure;
using PlantOps.Modules.Assets.Domain;
using PlantOps.Modules.Assets.Infrastructure;
using PlantOps.Modules.Assets.Tests.Integration;
using PlantOps.Modules.Identity;
using PlantOps.Modules.Inventory.Infrastructure;
using PlantOps.Modules.Reporting.Infrastructure;
using PlantOps.Modules.WorkOrders.Domain;
using PlantOps.Modules.WorkOrders.Infrastructure;
using Testcontainers.MsSql;

namespace PlantOps.Api.Tests;

// CI only (needs Docker): boots the whole application against a real SQL Server with Seed:Demo=true.
public class DemoSeedIntegrationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [IntegrationFact]
    public async Task Booting_with_the_demo_seed_fills_every_module_and_a_second_boot_adds_nothing()
    {
        await using var container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await container.StartAsync(Ct);
        var connectionString = new SqlConnectionStringBuilder(container.GetConnectionString()) { InitialCatalog = "PlantOpsSeedTests" }.ConnectionString;

        Counts first;
        await using (var factory = CreateFactory(connectionString))
        {
            using var warmUp = factory.CreateClient();
            await DrainWorkOrdersOutboxAsync(factory);
            first = await CountAsync(factory);

            await AssertSeededAsync(factory, first);
        }

        await using (var secondBoot = CreateFactory(connectionString))
        {
            using var warmUp = secondBoot.CreateClient();
            await DrainWorkOrdersOutboxAsync(secondBoot);

            Assert.Equal(first, await CountAsync(secondBoot));
        }
    }

    private static WebApplicationFactory<Program> CreateFactory(string connectionString) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:PlantOps", connectionString);
            builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");
            builder.UseSetting(DemoSeeder.ConfigurationKey, "true");
            // Delivery happens only when the test drains the outbox, so before and after are deterministic.
            builder.UseSetting("Outbox:PollInterval", "01:00:00");
        });

    private static async Task DrainWorkOrdersOutboxAsync(WebApplicationFactory<Program> factory)
    {
        var processor = factory.Services.GetRequiredService<IOutboxProcessor<WorkOrdersDbContext>>();
        // Bounded: a message that always fails is retried at most Attempts times, so this ends.
        for (var pass = 0; pass < 30; pass++)
        {
            if (await processor.ProcessOnceAsync(Ct) == 0)
            {
                break;
            }
        }
    }

    private static async Task AssertSeededAsync(WebApplicationFactory<Program> factory, Counts counts)
    {
        Assert.Equal(4, counts.Users);
        Assert.InRange(counts.Assets, 28, 34);
        Assert.Equal(25, counts.Parts);
        Assert.Equal(8, counts.Schedules);
        Assert.Equal(80, counts.WorkOrders);
        Assert.True(counts.Done >= 40, $"done {counts.Done}");

        // The completion events reached Assets (maintenance history) and Reporting (facts) through the outbox.
        Assert.Equal(counts.Done, counts.MaintenanceRecords);
        Assert.Equal(counts.Done, counts.Facts);
        Assert.Equal(0, counts.PendingOutbox);

        await using var scope = factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var pnp = await sp.GetRequiredService<AssetsDbContext>().Assets.SingleAsync(a => EF.Property<string>(a, Asset.TagField) == "SMT1-PNP-01", Ct);
        Assert.Equal(AssetStatus.InService, pnp.Status);

        var nozzle = await sp.GetRequiredService<InventoryDbContext>().SpareParts.SingleAsync(p => p.PartNumber == "NZL-CN040", Ct);
        Assert.Equal("pcs", nozzle.Unit);
        Assert.True(nozzle.QuantityAvailable >= 20, $"available {nozzle.QuantityAvailable}");

        var users = await sp.GetRequiredService<IdentityDbContext>().Users.Select(u => u.Id).ToListAsync(Ct);
        Assert.Contains(DemoSeedIds.Tom, users);

        var workOrders = sp.GetRequiredService<WorkOrdersDbContext>().WorkOrders;
        Assert.True(await workOrders.AnyAsync(w => w.Status == WorkOrderStatus.Approved && w.AssignedToId == null, Ct));
        Assert.True(await workOrders.AnyAsync(w => w.AssignedToId == DemoSeedIds.Tom, Ct));
    }

    private static async Task<Counts> CountAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var workOrders = sp.GetRequiredService<WorkOrdersDbContext>();
        return new Counts(
            await sp.GetRequiredService<IdentityDbContext>().Users.CountAsync(Ct),
            await sp.GetRequiredService<AssetsDbContext>().Assets.CountAsync(Ct),
            await sp.GetRequiredService<InventoryDbContext>().SpareParts.CountAsync(Ct),
            await workOrders.PmSchedules.CountAsync(Ct),
            await workOrders.WorkOrders.CountAsync(Ct),
            await workOrders.WorkOrders.CountAsync(w => w.Status == WorkOrderStatus.Completed || w.Status == WorkOrderStatus.Closed, Ct),
            await sp.GetRequiredService<AssetsDbContext>().MaintenanceRecords.CountAsync(Ct),
            await sp.GetRequiredService<ReportingDbContext>().WorkOrderFacts.CountAsync(Ct),
            await workOrders.Set<OutboxMessage>().CountAsync(m => m.ProcessedAt == null, Ct));
    }

    private sealed record Counts(
        int Users,
        int Assets,
        int Parts,
        int Schedules,
        int WorkOrders,
        int Done,
        int MaintenanceRecords,
        int Facts,
        int PendingOutbox);
}
