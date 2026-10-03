using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PlantOps.BuildingBlocks.Infrastructure;
using PlantOps.Modules.WorkOrders.Authorization;
using PlantOps.Modules.WorkOrders.Contracts;
using PlantOps.Modules.WorkOrders.Endpoints;
using PlantOps.Modules.WorkOrders.Infrastructure;
using PlantOps.Modules.WorkOrders.Integration;
using PlantOps.Modules.WorkOrders.Jobs;

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

        // Scheduled jobs (ADR-0010). The runners are scoped (they hold the DbContext); a host calls them from timers
        // (Functions) or from InProcessJobs (development). FactoryClock gives PM its notion of "today".
        services.AddFactoryClock();
        services.AddScoped<ISlaEscalationRunner, SlaEscalationRunner>();
        services.AddScoped<IPreventiveMaintenanceRunner, PreventiveMaintenanceRunner>();
        services.AddSingleton<IHostedService, InProcessJobs>();

        // Outbox (ADR-0009): the mapper decides which domain events become integration events, the interceptor
        // writes them with the change, the dispatcher delivers them. The AddIntegrationEvent calls are the
        // allow-list of types the dispatcher may deserialize.
        services.AddIntegrationEventMapper<WorkOrderIntegrationEventMapper>();
        services.AddIntegrationEvent<WorkOrderCompletedIntegrationEvent>();
        services.AddIntegrationEvent<WorkOrderCancelledIntegrationEvent>();
        services.AddIntegrationEvent<WorkOrderSlaBreachedIntegrationEvent>();
        AddSlaBreachForwarding(services);
        services.AddOutbox<WorkOrdersDbContext>();
        services.AddMigrateOnStartup<WorkOrdersDbContext>();
        services.AddHealthChecks().AddDbContextCheck<WorkOrdersDbContext>("workorders-db", tags: ["ready"]);
        return services;
    }

    // Which publisher runs is decided when it is first resolved (host configuration is complete by then): a managed
    // identity namespace, else a connection string (emulator), else just the log (laptop, tests).
    private static void AddSlaBreachForwarding(IServiceCollection services)
    {
        services.AddScoped<IIntegrationEventHandler<WorkOrderSlaBreachedIntegrationEvent>, SlaBreachForwarder>();

        // One ServiceBusClient for the whole process: it owns the AMQP connection and is thread-safe, so creating one
        // per message (or per scope) would open a connection each time. Only built when Service Bus is configured.
        services.AddSingleton(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var ns = configuration["ServiceBus:FullyQualifiedNamespace"];
            return !string.IsNullOrWhiteSpace(ns)
                ? new ServiceBusClient(ns, new DefaultAzureCredential())
                : new ServiceBusClient(configuration["ServiceBus:ConnectionString"]);
        });

        services.AddSingleton<ISlaBreachPublisher>(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            if (string.IsNullOrWhiteSpace(configuration["ServiceBus:FullyQualifiedNamespace"])
                && string.IsNullOrWhiteSpace(configuration["ServiceBus:ConnectionString"]))
            {
                return ActivatorUtilities.CreateInstance<LoggingSlaBreachPublisher>(sp);
            }

            var queue = configuration["ServiceBus:SlaBreachQueue"] is { Length: > 0 } name ? name : "sla-breaches";
            return new ServiceBusSlaBreachPublisher(sp.GetRequiredService<ServiceBusClient>().CreateSender(queue));
        });
    }

    public static IEndpointRouteBuilder MapWorkOrdersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/work-orders").WithTags("WorkOrders");
        WorkOrderEndpoints.Map(group);
        PmScheduleEndpoints.Map(app.MapGroup("/api/pm-schedules").WithTags("PmSchedules"));
        return app;
    }
}
