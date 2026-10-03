using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PlantOps.BuildingBlocks.Infrastructure;
using PlantOps.Modules.WorkOrders.Contracts;

namespace PlantOps.Modules.WorkOrders.Tests.Domain;

// The outbox Type column is data from the database; these tests pin that it can only ever select a registered event type.
public class IntegrationEventRegistryTests
{
    private static readonly string CompletedName = typeof(WorkOrderCompletedIntegrationEvent).FullName!;

    private static IntegrationEventRegistry RegistryFromModule()
    {
        var services = new ServiceCollection();
        services.AddWorkOrdersModule(new ConfigurationBuilder().Build());
        return services.BuildServiceProvider().GetRequiredService<IntegrationEventRegistry>();
    }

    [Fact]
    public void The_work_orders_module_registers_exactly_its_three_public_events()
    {
        var registry = RegistryFromModule();

        Assert.True(registry.TryGet(typeof(WorkOrderSlaBreachedIntegrationEvent).FullName, out var breached));
        Assert.Equal(typeof(WorkOrderSlaBreachedIntegrationEvent), breached.EventType);

        Assert.True(registry.TryGet(CompletedName, out var completed));
        Assert.Equal(typeof(WorkOrderCompletedIntegrationEvent), completed.EventType);
        Assert.True(registry.TryGet(typeof(WorkOrderCancelledIntegrationEvent).FullName, out var cancelled));
        Assert.Equal(typeof(WorkOrderCancelledIntegrationEvent), cancelled.EventType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("System.Diagnostics.Process")]
    [InlineData("System.IO.FileInfo, System.Private.CoreLib")]
    [InlineData("plantops.modules.workorders.contracts.workordercompletedintegrationevent")] // names are exact, not case-folded
    public void A_name_that_was_never_registered_is_rejected_not_looked_up_as_a_clr_type(string? name)
    {
        var registry = RegistryFromModule();

        Assert.False(registry.TryGet(name, out var registration));
        Assert.Null(registration);
    }

    [Fact]
    public void An_empty_registry_accepts_nothing() =>
        Assert.False(new IntegrationEventRegistry([]).TryGet(CompletedName, out _));

    [Fact]
    public async Task Dispatch_deserializes_the_payload_and_gives_it_to_every_handler()
    {
        var first = new RecordingHandler();
        var second = new RecordingHandler();
        var provider = new ServiceCollection()
            .AddSingleton<IIntegrationEventHandler<WorkOrderCancelledIntegrationEvent>>(first)
            .AddSingleton<IIntegrationEventHandler<WorkOrderCancelledIntegrationEvent>>(second)
            .BuildServiceProvider();
        var sent = new WorkOrderCancelledIntegrationEvent(Guid.NewGuid(), "WO-000007", "Scrapped", DateTimeOffset.UnixEpoch);
        var messageId = Guid.NewGuid();

        await IntegrationEventRegistration.For<WorkOrderCancelledIntegrationEvent>()
            .Dispatch(provider, JsonSerializer.Serialize(sent, JsonSerializerOptions.Web), messageId, CancellationToken.None);

        Assert.Equal(sent, Assert.Single(first.Received).Event);
        Assert.Equal(messageId, first.Received[0].MessageId);
        Assert.Single(second.Received);
    }

    [Fact]
    public async Task One_failing_handler_does_not_stop_the_others_but_the_failure_still_surfaces()
    {
        var healthy = new RecordingHandler();
        var provider = new ServiceCollection()
            .AddSingleton<IIntegrationEventHandler<WorkOrderCancelledIntegrationEvent>>(new ThrowingHandler())
            .AddSingleton<IIntegrationEventHandler<WorkOrderCancelledIntegrationEvent>>(healthy)
            .BuildServiceProvider();
        var payload = JsonSerializer.Serialize(
            new WorkOrderCancelledIntegrationEvent(Guid.NewGuid(), "WO-000007", "Scrapped", DateTimeOffset.UnixEpoch),
            JsonSerializerOptions.Web);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            IntegrationEventRegistration.For<WorkOrderCancelledIntegrationEvent>().Dispatch(provider, payload, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal("boom", ex.Message);
        Assert.Single(healthy.Received); // it ran even though the handler before it threw
    }

    private sealed class RecordingHandler : IIntegrationEventHandler<WorkOrderCancelledIntegrationEvent>
    {
        public List<(WorkOrderCancelledIntegrationEvent Event, Guid MessageId)> Received { get; } = [];

        public Task HandleAsync(WorkOrderCancelledIntegrationEvent integrationEvent, Guid messageId, CancellationToken cancellationToken)
        {
            Received.Add((integrationEvent, messageId));
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingHandler : IIntegrationEventHandler<WorkOrderCancelledIntegrationEvent>
    {
        public Task HandleAsync(WorkOrderCancelledIntegrationEvent integrationEvent, Guid messageId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom");
    }
}
