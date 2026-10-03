using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PlantOps.BuildingBlocks.Infrastructure;

/// <summary>Delivers one batch of pending outbox messages. The dispatcher calls it on a timer; tests call it to drain synchronously.</summary>
public interface IOutboxProcessor<TContext>
    where TContext : DbContext
{
    /// <returns>How many messages were claimed (delivered or failed) in this pass.</returns>
    Task<int> ProcessOnceAsync(CancellationToken cancellationToken = default);
}

internal sealed class OutboxProcessor<TContext>(
    IServiceScopeFactory scopes,
    IntegrationEventRegistry registry,
    TimeProvider time,
    ILogger<OutboxProcessor<TContext>> logger,
    ILoggerFactory loggers) : IOutboxProcessor<TContext>
    where TContext : DbContext
{
    public const int BatchSize = 20;

    private readonly ILogger _securityLogger = loggers.CreateLogger(SecurityEvents.Category);

    public async Task<int> ProcessOnceAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();

        // The contexts use EnableRetryOnFailure, which forbids a user transaction outside the execution strategy.
        // The whole claim-deliver-commit unit is the retryable block; handlers are idempotent so a rerun is safe.
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            // UPDLOCK: we intend to update these rows, so take the update lock now (no lock-upgrade deadlocks).
            // READPAST: skip rows another instance has claimed instead of waiting, so two app instances split the
            // backlog and never deliver the same row concurrently. ROWLOCK: lock rows, not pages.
            // Table and schema come from the EF model (trusted metadata), never from user input.
            var entity = db.Model.FindEntityType(typeof(OutboxMessage))!;
            var table = $"[{entity.GetSchema()}].[{entity.GetTableName()}]";
#pragma warning disable EF1002 // Only trusted model metadata and constants are interpolated; there is no user input in this SQL.
            var messages = await db.Set<OutboxMessage>()
                .FromSqlRaw(
                    $"SELECT TOP ({BatchSize}) * FROM {table} WITH (UPDLOCK, READPAST, ROWLOCK) " +
                    $"WHERE [ProcessedAt] IS NULL AND [Attempts] < {OutboxMessage.MaxAttempts} ORDER BY [OccurredAt]")
                .ToListAsync(cancellationToken);
#pragma warning restore EF1002

            foreach (var message in messages)
            {
                await DeliverAsync(message, cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return messages.Count;
        });
    }

    private async Task DeliverAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        try
        {
            if (!registry.TryGet(message.Type, out var registration))
            {
                throw new InvalidOperationException($"'{message.Type}' is not a registered integration event type.");
            }

            // A fresh scope per message: handlers get their own DbContext, so one failure cannot poison the next.
            await using var scope = scopes.CreateAsyncScope();
            await registration.Dispatch(scope.ServiceProvider, message.Payload, message.Id, cancellationToken);
            message.MarkProcessed(time.GetUtcNow());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            message.MarkFailed(ex is AggregateException aggregate ? aggregate.Flatten().Message : ex.Message);
            if (message.IsParked)
            {
                // Parked, not deleted: the row stays unprocessed (with LastError) for an operator to inspect.
                logger.LogError(ex, "Outbox message {MessageId} ({Type}) parked after {Attempts} failed attempts", message.Id, message.Type, message.Attempts);
                // Same fact under the fixed security EventId (1006) so an alert rule can key on it; no exception text here.
                SecurityEvents.OutboxMessageParked(_securityLogger, message.Id, message.Type);
            }
            else
            {
                logger.LogWarning(ex, "Outbox message {MessageId} ({Type}) failed attempt {Attempts}", message.Id, message.Type, message.Attempts);
            }
        }
    }
}
