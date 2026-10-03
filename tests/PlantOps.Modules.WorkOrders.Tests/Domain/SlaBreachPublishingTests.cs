using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PlantOps.BuildingBlocks.Infrastructure;
using PlantOps.Modules.WorkOrders.Contracts;
using PlantOps.Modules.WorkOrders.Integration;

namespace PlantOps.Modules.WorkOrders.Tests.Domain;

public class SlaBreachPublishingTests
{
    private static readonly WorkOrderSlaBreachedIntegrationEvent Breach = new(
        Guid.Parse("0f6c3b3e-5c1d-4b0e-9a55-3c2d2b1a0001"),
        "WO-000042",
        "Feeder jam",
        "SMT1-PNP-01",
        "P1",
        new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 10, 3, 12, 5, 0, TimeSpan.Zero),
        "Tom");

    [Fact]
    public void The_service_bus_message_carries_the_outbox_id_the_event_type_and_a_json_body()
    {
        var messageId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        var message = ServiceBusSlaBreachPublisher.ToMessage(Breach, messageId);

        // MessageId is what Service Bus duplicate detection keys on.
        Assert.Equal("11111111-2222-3333-4444-555555555555", message.MessageId);
        Assert.Equal("application/json", message.ContentType);
        Assert.Equal(typeof(WorkOrderSlaBreachedIntegrationEvent).FullName, message.Subject);

        var body = JsonDocument.Parse(message.Body.ToString()).RootElement;
        Assert.Equal(Breach.WorkOrderId, body.GetProperty("workOrderId").GetGuid());
        Assert.Equal("WO-000042", body.GetProperty("number").GetString());
        Assert.Equal("P1", body.GetProperty("priority").GetString());
        Assert.Equal("Tom", body.GetProperty("assignedToName").GetString());
        Assert.Equal(Breach.DueAt, body.GetProperty("dueAt").GetDateTimeOffset());
    }

    [Fact]
    public void The_message_body_round_trips_to_the_same_event()
    {
        var message = ServiceBusSlaBreachPublisher.ToMessage(Breach, Guid.NewGuid());

        var roundTripped = message.Body.ToObjectFromJson<WorkOrderSlaBreachedIntegrationEvent>(new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(Breach, roundTripped);
    }

    [Fact]
    public void An_unassigned_breach_has_a_null_assignee_in_the_body()
    {
        var message = ServiceBusSlaBreachPublisher.ToMessage(Breach with { AssignedToName = null }, Guid.NewGuid());

        Assert.Equal(JsonValueKind.Null, JsonDocument.Parse(message.Body.ToString()).RootElement.GetProperty("assignedToName").ValueKind);
    }

    [Fact]
    public async Task The_forwarder_hands_the_event_and_the_outbox_id_to_the_publisher()
    {
        var publisher = new RecordingPublisher();
        var messageId = Guid.NewGuid();

        await new SlaBreachForwarder(publisher).HandleAsync(Breach, messageId, CancellationToken.None);

        Assert.Equal((Breach, messageId), Assert.Single(publisher.Sent));
    }

    [Fact]
    public async Task The_logging_publisher_completes_without_throwing()
    {
        var publisher = new LoggingSlaBreachPublisher(NullLogger<LoggingSlaBreachPublisher>.Instance);

        await publisher.PublishAsync(Breach, Guid.NewGuid(), CancellationToken.None);
    }

    [Fact]
    public void Without_service_bus_settings_the_module_uses_the_logging_publisher()
    {
        using var provider = Build(new ConfigurationBuilder().Build());

        Assert.IsType<LoggingSlaBreachPublisher>(provider.GetRequiredService<ISlaBreachPublisher>());
    }

    [Fact]
    public async Task A_service_bus_connection_string_selects_the_service_bus_publisher_with_one_shared_client()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            // Syntactically valid, never contacted: creating the client and a sender opens no connection.
            ["ServiceBus:ConnectionString"] = "Endpoint=sb://plantops-test.servicebus.windows.net/;SharedAccessKeyName=a;SharedAccessKey=Zm9v",
        }).Build();
        await using var provider = Build(config);

        Assert.IsType<ServiceBusSlaBreachPublisher>(provider.GetRequiredService<ISlaBreachPublisher>());
        Assert.Same(provider.GetRequiredService<ServiceBusClient>(), provider.GetRequiredService<ServiceBusClient>());
    }

    [Fact]
    public void The_module_registers_the_breach_event_for_the_outbox_and_a_forwarder_for_it()
    {
        using var provider = Build(new ConfigurationBuilder().Build());

        Assert.True(provider.GetRequiredService<IntegrationEventRegistry>()
            .TryGet(typeof(WorkOrderSlaBreachedIntegrationEvent).FullName, out var registration));
        Assert.Equal(typeof(WorkOrderSlaBreachedIntegrationEvent), registration.EventType);
        using var scope = provider.CreateScope();
        Assert.IsType<SlaBreachForwarder>(Assert.Single(scope.ServiceProvider.GetServices<IIntegrationEventHandler<WorkOrderSlaBreachedIntegrationEvent>>()));
    }

    private static ServiceProvider Build(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddLogging();
        services.AddWorkOrdersModule(configuration);
        return services.BuildServiceProvider();
    }

    private sealed class RecordingPublisher : ISlaBreachPublisher
    {
        public List<(WorkOrderSlaBreachedIntegrationEvent Breach, Guid MessageId)> Sent { get; } = [];

        public Task PublishAsync(WorkOrderSlaBreachedIntegrationEvent breach, Guid messageId, CancellationToken cancellationToken)
        {
            Sent.Add((breach, messageId));
            return Task.CompletedTask;
        }
    }
}
