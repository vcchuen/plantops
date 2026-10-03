using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PlantOps.BuildingBlocks.Infrastructure;
using PlantOps.Modules.Inventory.Contracts;
using PlantOps.Modules.Inventory.Endpoints;
using PlantOps.Modules.Inventory.Handlers;
using PlantOps.Modules.Inventory.Infrastructure;
using PlantOps.Modules.WorkOrders.Contracts;

namespace PlantOps.Modules.Inventory;

public static class InventoryModule
{
    public static IServiceCollection AddInventoryModule(this IServiceCollection services, IConfiguration configuration)
    {
        // The connection string is read inside the callback (see AssetsModule for why).
        services.AddDomainEventAuditing();
        services.AddDbContext<InventoryDbContext>((sp, options) => options
            .UseSqlServer(
                configuration.GetConnectionString("PlantOps"),
                sql =>
                {
                    sql.MigrationsHistoryTable("__EFMigrationsHistory", InventoryDbContext.Schema);
                    sql.EnableRetryOnFailure();
                })
            .AddInterceptors(sp.GetRequiredService<DomainEventInterceptor>()));

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IReservationQueries, ReservationQueries>();

        // Consumers of WorkOrders' integration events (ADR-0009). Scoped: the dispatcher resolves them in a new scope per message.
        services.AddScoped<IIntegrationEventHandler<WorkOrderCompletedIntegrationEvent>, WorkOrderCompletedHandler>();
        services.AddScoped<IIntegrationEventHandler<WorkOrderCancelledIntegrationEvent>, WorkOrderCancelledHandler>();

        services.AddMigrateOnStartup<InventoryDbContext>();
        services.AddHealthChecks().AddDbContextCheck<InventoryDbContext>("inventory-db", tags: ["ready"]);
        return services;
    }

    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/inventory").WithTags("Inventory");
        InventoryEndpoints.Map(group);
        return app;
    }
}
