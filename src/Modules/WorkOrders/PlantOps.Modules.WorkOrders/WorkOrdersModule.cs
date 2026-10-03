using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PlantOps.BuildingBlocks.Infrastructure;
using PlantOps.Modules.WorkOrders.Authorization;
using PlantOps.Modules.WorkOrders.Contracts;
using PlantOps.Modules.WorkOrders.Endpoints;
using PlantOps.Modules.WorkOrders.Infrastructure;
using PlantOps.Modules.WorkOrders.Integration;

namespace PlantOps.Modules.WorkOrders;

public static class WorkOrdersModule
{
    public static IServiceCollection AddWorkOrdersModule(this IServiceCollection services, IConfiguration configuration)
    {
        // The connection string is read inside the callback (see AssetsModule for why).
        services.AddDomainEventAuditing();
        services.AddDbContext<WorkOrdersDbContext>((sp, options) => options
            .UseSqlServer(
                configuration.GetConnectionString("PlantOps"),
                sql =>
                {
                    sql.MigrationsHistoryTable("__EFMigrationsHistory", WorkOrdersDbContext.Schema);
                    sql.EnableRetryOnFailure();
                })
            .AddInterceptors(sp.GetRequiredService<DomainEventInterceptor>()));

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IAuthorizationHandler, WorkOrderAuthorizationHandler>();
        services.AddScoped<IWorkOrderDirectory, WorkOrderDirectory>();

        // Outbox (ADR-0009): the mapper decides which domain events become integration events, the interceptor
        // writes them with the change, the dispatcher delivers them. The two AddIntegrationEvent calls are the
        // allow-list of types the dispatcher may deserialize.
        services.AddIntegrationEventMapper<WorkOrderIntegrationEventMapper>();
        services.AddIntegrationEvent<WorkOrderCompletedIntegrationEvent>();
        services.AddIntegrationEvent<WorkOrderCancelledIntegrationEvent>();
        services.AddOutbox<WorkOrdersDbContext>();
        services.AddMigrateOnStartup<WorkOrdersDbContext>();
        services.AddHealthChecks().AddDbContextCheck<WorkOrdersDbContext>("workorders-db", tags: ["ready"]);
        return services;
    }

    public static IEndpointRouteBuilder MapWorkOrdersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/work-orders").WithTags("WorkOrders");
        WorkOrderEndpoints.Map(group);
        return app;
    }
}
