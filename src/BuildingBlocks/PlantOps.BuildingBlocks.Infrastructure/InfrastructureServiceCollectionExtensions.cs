using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace PlantOps.BuildingBlocks.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers the audit interceptor. The host must also register an <see cref="ICurrentUser"/> (Identity does).
    /// Scoped because it depends on the per-request user.
    /// </summary>
    public static IServiceCollection AddDomainEventAuditing(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<DomainEventInterceptor>();
        return services;
    }

    /// <summary>Applies the context's pending migrations while the host starts, when Database:ApplyMigrationsOnStartup is true.</summary>
    public static IServiceCollection AddMigrateOnStartup<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.AddSingleton<IHostedService, MigrateOnStartup<TContext>>();
        return services;
    }
}
