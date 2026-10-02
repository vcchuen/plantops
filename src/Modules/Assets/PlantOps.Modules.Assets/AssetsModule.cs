using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PlantOps.Modules.Assets.Endpoints;
using PlantOps.Modules.Assets.Infrastructure;

namespace PlantOps.Modules.Assets;

public static class AssetsModule
{
    public static IServiceCollection AddAssetsModule(this IServiceCollection services, IConfiguration configuration)
    {
        // The connection string is read inside the callback, i.e. when a context is first resolved, not here:
        // configuration added after service registration (tests, user-secrets) must still be honoured.
        services.AddDbContext<AssetsDbContext>(options => options.UseSqlServer(
            configuration.GetConnectionString("PlantOps"),
            sql =>
            {
                sql.MigrationsHistoryTable("__EFMigrationsHistory", AssetsDbContext.Schema);
                // Azure SQL drops connections during failovers and throttling; these faults are transient and
                // safe to retry for the single-SaveChanges units of work used here.
                sql.EnableRetryOnFailure();
            }));

        services.TryAddSingleton(TimeProvider.System);
        services.AddHostedService<AssetsMigrationService>();
        services.AddHealthChecks().AddDbContextCheck<AssetsDbContext>("assets-db", tags: ["ready"]);
        return services;
    }

    public static IEndpointRouteBuilder MapAssetsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/assets").WithTags("Assets");
        AssetEndpoints.Map(group);
        return app;
    }
}
