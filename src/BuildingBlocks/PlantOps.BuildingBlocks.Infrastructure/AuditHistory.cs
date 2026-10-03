using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace PlantOps.BuildingBlocks.Infrastructure;

/// <summary>One line of an aggregate's history as the API returns it; the payload is the stored event as a JSON object.</summary>
public sealed record AuditHistoryItem(string EventType, string ActorName, DateTimeOffset OccurredAt, JsonElement Payload);

public static class AuditHistory
{
    /// <summary>Everything recorded about one aggregate, newest first.</summary>
    public static async Task<IReadOnlyList<AuditHistoryItem>> ForAggregateAsync(
        this DbContext context,
        string aggregateId,
        CancellationToken cancellationToken)
    {
        var rows = await context.Set<AuditEntry>()
            .AsNoTracking()
            .Where(e => e.AggregateId == aggregateId)
            .OrderByDescending(e => e.OccurredAt)
            .Select(e => new { e.EventType, e.ActorName, e.OccurredAt, e.Payload })
            .ToListAsync(cancellationToken);

        // Parsed after the query: JSON parsing is not SQL, and Deserialize<JsonElement> yields a standalone
        // value (no JsonDocument to dispose) that serializes back as a nested object, not an escaped string.
        return rows
            .Select(r => new AuditHistoryItem(
                r.EventType,
                r.ActorName,
                r.OccurredAt,
                JsonSerializer.Deserialize<JsonElement>(r.Payload)))
            .ToList();
    }
}
