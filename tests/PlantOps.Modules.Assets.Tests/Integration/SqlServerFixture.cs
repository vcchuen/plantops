using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlantOps.Api.Tests;
using PlantOps.Modules.Assets.Infrastructure;
using Testcontainers.MsSql;

namespace PlantOps.Modules.Assets.Tests.Integration;

/// <summary>One SQL Server container and one host per test collection; started only when integration tests are enabled.</summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;

    public CommandCounter Commands { get; } = new();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        if (!IntegrationFactAttribute.Enabled)
        {
            return;
        }

        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _container.StartAsync();

        // The container's string targets master; a dedicated database proves the migrations create real state.
        var connectionString = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = "PlantOpsTests",
        }.ConnectionString;

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:PlantOps", connectionString);
            // The module's hosted service runs MigrateAsync() while the host starts, so the tests exercise the real migrations.
            builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");
            builder.ConfigureTestServices(services =>
            {
                services.ConfigureDbContext<AssetsDbContext>(options => options.AddInterceptors(Commands));
                services.AddTestAuthentication();
            });
        });

        // The host (and therefore the migration) starts lazily on first use.
        using var warmUp = Factory.CreateClient();
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
public sealed class IntegrationCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "Assets integration (SQL Server)";
}
