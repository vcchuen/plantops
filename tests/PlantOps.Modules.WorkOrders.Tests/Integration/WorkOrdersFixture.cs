using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using PlantOps.Api.Tests;
using PlantOps.Modules.Assets.Tests.Integration;
using PlantOps.Modules.Identity;
using PlantOps.Modules.Identity.Contracts;
using Testcontainers.MsSql;

namespace PlantOps.Modules.WorkOrders.Tests.Integration;

/// <summary>
/// One SQL Server container and one host per test collection, started only when integration tests are enabled.
/// The same pattern as the Assets fixture, plus the identity directory rows that assignment needs.
/// </summary>
public sealed class WorkOrdersFixture : IAsyncLifetime
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
            InitialCatalog = "PlantOpsWorkOrderTests",
        }.ConnectionString;

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:PlantOps", connectionString);
            // The hosted services migrate Assets, WorkOrders and Identity while the host starts.
            builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");
            // One test user fires hundreds of requests a second; the production limit would answer 429.
            builder.UseSetting("RateLimiting:Api:TokenLimit", "1000000");
            builder.UseSetting("RateLimiting:Api:TokensPerPeriod", "1000000");
            builder.ConfigureTestServices(services => services.AddTestAuthentication());
        });

        // The host (and therefore the migrations) starts lazily on first use.
        using var warmUp = Factory.CreateClient();

        await SeedUsersAsync();
    }

    // Normally the OIDC login upserts these (just-in-time provisioning); tests have no IdP, so seed the directory
    // directly. The ids match what TestAuth derives from the name ("Tom" -> "tom").
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
public sealed class WorkOrdersCollection : ICollectionFixture<WorkOrdersFixture>
{
    public const string Name = "Work orders integration (SQL Server)";
}
