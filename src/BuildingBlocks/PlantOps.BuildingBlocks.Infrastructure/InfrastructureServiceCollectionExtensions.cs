using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PlantOps.SharedKernel;

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

    /// <summary>
    /// Registers the <see cref="FactoryClock"/> with the zone from "Factory:TimeZone" (default Asia/Kuala_Lumpur).
    /// Several modules call this; TryAdd keeps one instance. The configuration is read when the clock is first
    /// resolved, not now, so host settings added after registration (and test overrides) still apply.
    /// </summary>
    public static IServiceCollection AddFactoryClock(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(sp => new FactoryClock(
            sp.GetRequiredService<TimeProvider>(),
            FactoryClock.FindZone(sp.GetRequiredService<IConfiguration>()[FactoryClock.ConfigurationKey])));
        return services;
    }

    /// <summary>
    /// Registers the outbox dispatcher and processor for a producing module's context (ADR-0009). The poll interval
    /// is "Outbox:PollInterval" (a TimeSpan, default 2 s).
    /// </summary>
    public static IServiceCollection AddOutbox<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IntegrationEventRegistry>();
        services.AddSingleton<IOutboxProcessor<TContext>, OutboxProcessor<TContext>>();
        services.AddSingleton<IHostedService, OutboxDispatcher<TContext>>();
        return services;
    }

    /// <summary>Adds an event type to the dispatcher's allow-list. Only registered types are ever deserialized.</summary>
    public static IServiceCollection AddIntegrationEvent<TEvent>(this IServiceCollection services)
        where TEvent : class, IIntegrationEvent
    {
        services.AddSingleton(IntegrationEventRegistration.For<TEvent>());
        return services;
    }

    /// <summary>Registers a producing module's domain-to-integration event mapper for <see cref="DomainEventInterceptor"/>.</summary>
    public static IServiceCollection AddIntegrationEventMapper<TMapper>(this IServiceCollection services)
        where TMapper : class, IIntegrationEventMapper
    {
        services.AddSingleton<IIntegrationEventMapper, TMapper>();
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
