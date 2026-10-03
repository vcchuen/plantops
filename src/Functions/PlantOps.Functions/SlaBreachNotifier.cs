using System.Net.Http.Json;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PlantOps.Modules.WorkOrders.Contracts;

namespace PlantOps.Functions;

/// <summary>
/// Turns an SLA breach message into a notification: a structured log line, plus a Teams post when
/// Notifications:TeamsWebhookUrl is set. Throwing is the retry signal: Service Bus redelivers until the queue's
/// max delivery count, then dead-letters the message, so a Teams outage delays the notification instead of losing it.
/// </summary>
public sealed class SlaBreachNotifier(
    IHttpClientFactory httpClients,
    IConfiguration configuration,
    ILogger<SlaBreachNotifier> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // "ServiceBus" is the connection NAME, not a connection string: the extension looks for "ServiceBus" (a connection
    // string) or "ServiceBus__fullyQualifiedNamespace" (identity-based, DefaultAzureCredential / managed identity).
    // The queue name comes from the app setting SlaBreachQueue (a %binding expression%, resolved by the host).
    [Function("SlaBreachNotifier")]
    public async Task Run(
        [ServiceBusTrigger("%SlaBreachQueue%", Connection = "ServiceBus")] ServiceBusReceivedMessage message,
        CancellationToken cancellationToken)
    {
        var breach = message.Body.ToObjectFromJson<WorkOrderSlaBreachedIntegrationEvent>(JsonOptions)
            ?? throw new InvalidOperationException($"Message {message.MessageId} has no readable SLA breach body.");

        logger.LogWarning(
            "SLA breached: {Number} {Title} on {AssetTag} ({Priority}), due {DueAt:O}, escalated {EscalatedAt:O}, assigned to {AssignedTo}. Message {MessageId}, delivery {DeliveryCount}",
            breach.Number,
            breach.Title,
            breach.AssetTag,
            breach.Priority,
            breach.DueAt,
            breach.EscalatedAt,
            breach.AssignedToName ?? "nobody",
            message.MessageId,
            message.DeliveryCount);

        var webhook = configuration["Notifications:TeamsWebhookUrl"];
        if (string.IsNullOrWhiteSpace(webhook))
        {
            return;
        }

        using var client = httpClients.CreateClient();
        using var response = await client.PostAsJsonAsync(webhook, TeamsCard.For(breach), cancellationToken);

        // Not a success: let the exception escape so the message is retried, then dead-lettered.
        response.EnsureSuccessStatusCode();
    }
}

/// <summary>The Teams "message with an Adaptive Card attachment" payload that Teams Workflows webhooks accept.</summary>
internal static class TeamsCard
{
    public static object For(WorkOrderSlaBreachedIntegrationEvent breach) => new
    {
        type = "message",
        attachments = new[]
        {
            new
            {
                contentType = "application/vnd.microsoft.card.adaptive",
                contentUrl = (string?)null,
                // A dictionary, not an anonymous type: "$schema" is not a legal C# member name.
                content = new Dictionary<string, object>
                {
                    ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
                    ["type"] = "AdaptiveCard",
                    ["version"] = "1.4",
                    ["body"] = new object[]
                    {
                        new { type = "TextBlock", size = "Medium", weight = "Bolder", text = $"SLA breached: {breach.Number}" },
                        new { type = "TextBlock", wrap = true, text = breach.Title },
                        new
                        {
                            type = "FactSet",
                            facts = new[]
                            {
                                new { title = "Asset", value = breach.AssetTag },
                                new { title = "Priority", value = breach.Priority },
                                new { title = "Due", value = breach.DueAt.ToString("u") },
                                new { title = "Assigned to", value = breach.AssignedToName ?? "Unassigned" },
                            },
                        },
                    },
                },
            },
        },
    };
}
