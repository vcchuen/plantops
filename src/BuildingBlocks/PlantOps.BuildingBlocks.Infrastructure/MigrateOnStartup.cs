using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PlantOps.BuildingBlocks.Infrastructure;

// Runs in StartAsync, which completes before Kestrel starts accepting requests, so no request can hit a
// half-migrated schema. Local/compose only: production applies migration bundles in the pipeline (ADR-0004).
internal sealed class MigrateOnStartup<TContext>(
    IServiceProvider services,
    IConfiguration configuration,
    ILogger<MigrateOnStartup<TContext>> logger) : IHostedService
    where TContext : DbContext
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        logger.LogInformation("Applying {Context} migrations", typeof(TContext).Name);
        await db.Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
