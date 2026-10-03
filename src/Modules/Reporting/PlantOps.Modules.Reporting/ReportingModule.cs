using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PlantOps.BuildingBlocks.Infrastructure;
using PlantOps.Modules.Reporting.Endpoints;
using PlantOps.Modules.Reporting.Handlers;
using PlantOps.Modules.Reporting.Infrastructure;
using PlantOps.Modules.Reporting.Projection;
using PlantOps.Modules.Reporting.Queries;
using PlantOps.Modules.WorkOrders.Contracts;

namespace PlantOps.Modules.Reporting;

public static class ReportingModule
{
    public static IServiceCollection AddReportingModule(this IServiceCollection services, IConfiguration configuration)
    {
        // The connection string is read inside the callback (see AssetsModule for why). No audit interceptor: the
        // facts are derived data, and the rebuild is the audit of last resort.
        services.AddDbContext<ReportingDbContext>(options => options.UseSqlServer(
            configuration.GetConnectionString("PlantOps"),
            sql =>
            {
                sql.MigrationsHistoryTable("__EFMigrationsHistory", ReportingDbContext.Schema);
                sql.EnableRetryOnFailure();
            }));

        services.TryAddSingleton(TimeProvider.System);
        services.AddFactoryClock();

        // Consumer of WorkOrders' completion event (ADR-0009, ADR-0011). Scoped: the dispatcher resolves it per message.
        services.AddScoped<IIntegrationEventHandler<WorkOrderCompletedIntegrationEvent>, WorkOrderCompletedHandler>();

        services.AddScoped<ReportQueries>();
        services.AddScoped<ReportRebuilder>();

        services.AddMigrateOnStartup<ReportingDbContext>();
        services.AddHealthChecks().AddDbContextCheck<ReportingDbContext>("reporting-db", tags: ["ready"]);
        return services;
    }

    public static IEndpointRouteBuilder MapReportingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reports").WithTags("Reports");
        ReportEndpoints.Map(group);
        return app;
    }
}
