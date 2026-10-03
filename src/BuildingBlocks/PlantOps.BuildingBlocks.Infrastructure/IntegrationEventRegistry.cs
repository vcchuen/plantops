using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using PlantOps.SharedKernel;

namespace PlantOps.BuildingBlocks.Infrastructure;

internal static class IntegrationEventJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}

/// <summary>One known integration event type and the typed code that deserializes it and calls its handlers.</summary>
public sealed class IntegrationEventRegistration
{
    private IntegrationEventRegistration(Type eventType, Func<IServiceProvider, string, Guid, CancellationToken, Task> dispatch)
    {
        EventType = eventType;
        Dispatch = dispatch;
    }

    public Type EventType { get; }

    /// <summary>Name stored in <see cref="OutboxMessage.Type"/>.</summary>
    public string Name => NameOf(EventType);

    /// <summary>Deserializes the payload as this event type and runs every registered handler for it (arguments: scope, payload, message id).</summary>
    public Func<IServiceProvider, string, Guid, CancellationToken, Task> Dispatch { get; }

    public static string NameOf(Type eventType) => eventType.FullName ?? eventType.Name;

    public static IntegrationEventRegistration For<TEvent>()
        where TEvent : class, IIntegrationEvent =>
        new(typeof(TEvent), DispatchAsync<TEvent>);

    private static async Task DispatchAsync<TEvent>(IServiceProvider services, string payload, Guid messageId, CancellationToken cancellationToken)
        where TEvent : class, IIntegrationEvent
    {
        var integrationEvent = JsonSerializer.Deserialize<TEvent>(payload, IntegrationEventJson.Options)
            ?? throw new InvalidOperationException($"Payload of {typeof(TEvent).Name} deserialized to null.");

        // Every handler gets its turn even if an earlier one failed: one broken consumer must not starve the others.
        // The failures are rethrown together; handlers that succeeded are skipped on redelivery by their inbox rows.
        List<ExceptionDispatchInfo>? failures = null;
        foreach (var handler in services.GetServices<IIntegrationEventHandler<TEvent>>())
        {
            try
            {
                await handler.HandleAsync(integrationEvent, messageId, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                (failures ??= []).Add(ExceptionDispatchInfo.Capture(ex));
            }
        }

        if (failures is { Count: 1 })
        {
            failures[0].Throw();
        }

        if (failures is not null)
        {
            throw new AggregateException(failures.Select(f => f.SourceException));
        }
    }
}

/// <summary>
/// The allow-list of event types the dispatcher may deserialize. Built at startup from explicit registrations
/// (<see cref="OutboxServiceCollectionExtensions.AddIntegrationEvent{TEvent}"/>).
/// SECURITY: the outbox Type column is data. Feeding it to Type.GetType would let anyone who can write that column
/// (SQL injection elsewhere, a compromised job) pick an arbitrary CLR type to instantiate from JSON, the classic
/// deserialization gadget attack. An unknown name is simply a failed message, never a type lookup.
/// </summary>
public sealed class IntegrationEventRegistry
{
    private readonly Dictionary<string, IntegrationEventRegistration> _byName;

    public IntegrationEventRegistry(IEnumerable<IntegrationEventRegistration> registrations) =>
        _byName = registrations
            .GroupBy(r => r.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    public bool TryGet(string? typeName, [NotNullWhen(true)] out IntegrationEventRegistration? registration)
    {
        registration = null;
        return typeName is not null && _byName.TryGetValue(typeName, out registration);
    }
}
