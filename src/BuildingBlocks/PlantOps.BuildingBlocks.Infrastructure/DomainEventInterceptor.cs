using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PlantOps.SharedKernel;

namespace PlantOps.BuildingBlocks.Infrastructure;

/// <summary>
/// Turns the domain events pending on tracked aggregates into <see cref="AuditEntry"/> rows just before
/// SaveChanges. The rows join the same unit of work, so the change and its audit trail commit or roll back together.
/// When the module registered an <see cref="IIntegrationEventMapper"/> for the context, the same pass also writes
/// <see cref="OutboxMessage"/> rows (ADR-0009): audit, change and outbox are one atomic commit.
/// </summary>
public sealed class DomainEventInterceptor(
    ICurrentUser currentUser,
    TimeProvider time,
    IEnumerable<IIntegrationEventMapper> mappers) : SaveChangesInterceptor
{
    private const string SystemId = "system";
    private const string SystemName = "System";

    // Enums as names: an auditor reading "Criticality":"A" needs no enum table.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        WriteAuditEntries(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        WriteAuditEntries(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void WriteAuditEntries(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        // ToList: adding AuditEntry rows below changes the tracker's collection while we iterate it.
        var aggregates = context.ChangeTracker.Entries<AggregateRoot>()
            .Select(e => e.Entity)
            .Where(a => a.DomainEvents.Count > 0)
            .ToList();
        if (aggregates.Count == 0)
        {
            return;
        }

        var actorId = string.IsNullOrWhiteSpace(currentUser.Id) ? SystemId : currentUser.Id;
        var actorName = string.IsNullOrWhiteSpace(currentUser.Id)
            ? SystemName
            : string.IsNullOrWhiteSpace(currentUser.Name) ? currentUser.Id : currentUser.Name;

        // The interceptor instance is shared by every module's context; the mapper is chosen per context.
        var mapper = mappers.FirstOrDefault(m => m.ContextType == context.GetType());

        var now = time.GetUtcNow();
        var sequence = 0;
        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.DomainEvents)
            {
                // One tick per entry keeps OccurredAt strictly increasing within a save, so "newest first" is
                // deterministic even when several events are raised by one command.
                context.Set<AuditEntry>().Add(new AuditEntry(
                    aggregate.GetType().Name,
                    aggregate.AggregateId,
                    domainEvent.GetType().Name,
                    // The runtime type, not IDomainEvent: the interface has no members to serialize.
                    JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), JsonOptions),
                    actorId,
                    actorName,
                    now.AddTicks(sequence++)));

                if (mapper?.Map(domainEvent) is { } integrationEvent)
                {
                    // Runtime type again, for the same reason; the dispatcher deserializes by the stored type name.
                    context.Set<OutboxMessage>().Add(new OutboxMessage(
                        IntegrationEventRegistration.NameOf(integrationEvent.GetType()),
                        JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), IntegrationEventJson.Options),
                        now.AddTicks(sequence++)));
                }
            }

            aggregate.ClearDomainEvents();
        }
    }
}
