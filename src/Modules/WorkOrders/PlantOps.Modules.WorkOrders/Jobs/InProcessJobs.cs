using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PlantOps.Modules.WorkOrders.Contracts;

namespace PlantOps.Modules.WorkOrders.Jobs;

/// <summary>
/// Runs the scheduled jobs inside the API process when "Jobs:RunInProcess" is true (default false).
/// DEVELOPMENT ONLY and single-instance (ADR-0010): with N API instances this runs N times, there is no leader
/// election, and it stops whenever App Service idles or recycles the process. Production uses the Functions timers,
/// which the platform guarantees to run once per tick. Both runners are idempotent, so overlap is harmless, just wasteful.
/// </summary>
internal sealed class InProcessJobs(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<InProcessJobs> logger) : BackgroundService
{
    public static readonly TimeSpan SlaInterval = TimeSpan.FromMinutes(5);

    // The PM runner only generates when a schedule is due, so checking hourly (not once at 06:00 factory time) is
    // enough and survives a restart at any hour.
    public static readonly TimeSpan PmInterval = TimeSpan.FromHours(1);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue<bool>("Jobs:RunInProcess"))
        {
            return Task.CompletedTask;
        }

        logger.LogWarning("Jobs:RunInProcess is on: running SLA escalation and PM generation inside this process (development only)");
        return Task.WhenAll(
            Loop("SLA escalation", SlaInterval, (sp, ct) => sp.GetRequiredService<ISlaEscalationRunner>().RunAsync(ct), stoppingToken),
            Loop("PM generation", PmInterval, (sp, ct) => sp.GetRequiredService<IPreventiveMaintenanceRunner>().RunAsync(ct), stoppingToken));
    }

    private async Task Loop(
        string name,
        TimeSpan interval,
        Func<IServiceProvider, CancellationToken, Task<int>> run,
        CancellationToken stoppingToken)
    {
        // Run once straight away (a dev who just started the app wants to see it work), then on the interval.
        using var timer = new PeriodicTimer(interval);
        try
        {
            do
            {
                await RunOnceAsync(name, run, stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async Task RunOnceAsync(string name, Func<IServiceProvider, CancellationToken, Task<int>> run, CancellationToken stoppingToken)
    {
        try
        {
            // Scoped runners (they hold a DbContext): a fresh scope per run.
            await using var scope = scopes.CreateAsyncScope();
            await run(scope.ServiceProvider, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Database down or not migrated yet: log and wait for the next tick. The loop must never die.
            logger.LogError(ex, "In-process job '{Job}' failed", name);
        }
    }
}
