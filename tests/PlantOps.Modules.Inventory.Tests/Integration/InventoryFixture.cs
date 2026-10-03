using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using PlantOps.Api.Tests;
using PlantOps.BuildingBlocks.Infrastructure;
using PlantOps.Modules.Assets.Tests.Integration;
using PlantOps.Modules.Identity;
using PlantOps.Modules.Identity.Contracts;
using PlantOps.Modules.WorkOrders.Infrastructure;
using Testcontainers.MsSql;

namespace PlantOps.Modules.Inventory.Tests.Integration;

/// <summary>
/// One SQL Server container and one host for the cross-module tests. The event flow touches WorkOrders, Inventory,
/// Assets and Identity, so the fixture boots the whole application (the Api host) rather than a module.
/// The outbox poll interval is set far beyond any test's lifetime: events are delivered only when a test calls
/// <see cref="DrainWorkOrdersOutboxAsync"/>, so "before delivery" and "after delivery" are both deterministic.
/// </summary>
public sealed class InventoryFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        if (!IntegrationFactAttribute.Enabled)
        {
            return;
        }

        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _container.StartAsync();

        var connectionString = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = "PlantOpsInventoryTests",
        }.ConnectionString;

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:PlantOps", connectionString);
            // The hosted services migrate every module's schema while the host starts.
            builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");
            // One test user fires hundreds of requests a second; the production limit would answer 429.
            builder.UseSetting("RateLimiting:Api:TokenLimit", "1000000");
            builder.UseSetting("RateLimiting:Api:TokensPerPeriod", "1000000");
            builder.UseSetting("Outbox:PollInterval", "01:00:00");
            builder.ConfigureTestServices(services => services.AddTestAuthentication());
        });

        // The host (and therefore the migrations) starts lazily on first use.
        using var warmUp = Factory.CreateClient();

        await SeedUsersAsync();
    }

    // Normally the OIDC login upserts these (just-in-time provisioning); tests have no IdP. The ids match what
    // TestAuth derives from the name ("Tom" -> "tom").
    private async Task SeedUsersAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = DateTimeOffset.UtcNow;
        db.Users.AddRange(
            User.Provision("tom", "Tom", "tom@plantops.test", [Roles.Technician], now),
            User.Provision("lee", "Lee", "lee@plantops.test", [Roles.Technician], now),
            User.Provision("sam", "Sam", "sam@plantops.test", [Roles.Supervisor], now),
            User.Provision("ada", "Ada", "ada@plantops.test", [Roles.Admin, Roles.Technician], now),
            User.Provision("olivia", "Olivia", "olivia@plantops.test", [Roles.Operator], now));
        await db.SaveChangesAsync();
    }

    /// <summary>Delivers every pending WorkOrders outbox message, the way the dispatcher's timer would.</summary>
    public async Task<int> DrainWorkOrdersOutboxAsync(CancellationToken cancellationToken)
    {
        var processor = Factory.Services.GetRequiredService<IOutboxProcessor<WorkOrdersDbContext>>();
        var total = 0;
        // Bounded: a message that always fails is retried at most Attempts times, so this ends.
        for (var pass = 0; pass < 10; pass++)
        {
            var claimed = await processor.ProcessOnceAsync(cancellationToken);
            if (claimed == 0)
            {
                break;
            }

            total += claimed;
        }

        return total;
    }

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}

[CollectionDefinition(Name)]
public sealed class InventoryCollection : ICollectionFixture<InventoryFixture>
{
    public const string Name = "Inventory integration (SQL Server)";
}
