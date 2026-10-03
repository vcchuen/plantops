using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PlantOps.BuildingBlocks.Infrastructure;

/// <summary>Polls one module's outbox and delivers its messages to in-process handlers (ADR-0009). One per producing module.</summary>
public sealed class OutboxDispatcher<TContext>(
    IOutboxProcessor<TContext> processor,
    IConfiguration configuration,
    ILogger<OutboxDispatcher<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = configuration.GetValue("Outbox:PollInterval", DefaultPollInterval);
        if (interval <= TimeSpan.Zero)
        {
            interval = DefaultPollInterval;
        }

        // Wait first, then work: a host that lives for less than one interval (most test hosts) never touches the database.
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await DrainAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async Task DrainAsync(CancellationToken stoppingToken)
    {
        try
        {
            // A full batch means there is probably more; keep going instead of idling a whole interval.
            while (await processor.ProcessOnceAsync(stoppingToken) >= OutboxProcessor<TContext>.BatchSize)
            {
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Database down or not migrated yet: log and try again next tick. The loop must never die.
            logger.LogError(ex, "Outbox pass for {Context} failed", typeof(TContext).Name);
        }
    }
}
