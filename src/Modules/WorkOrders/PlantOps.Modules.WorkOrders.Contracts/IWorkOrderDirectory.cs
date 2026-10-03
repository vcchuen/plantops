namespace PlantOps.Modules.WorkOrders.Contracts;

/// <summary>What other modules may know about a work order. Read-only: WorkOrders owns the data.</summary>
public interface IWorkOrderDirectory
{
    Task<WorkOrderSummary?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Completed (and closed) work orders whose completion is at or after <paramref name="since"/>, oldest first,
    /// streamed. Lets the Reporting read model be rebuilt (design 07) without a cross-schema join.
    /// </summary>
    IAsyncEnumerable<CompletedWorkOrder> CompletedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken);
}

/// <summary>The same facts as <c>WorkOrderCompletedIntegrationEvent</c>, for rebuilds. Priority and Source are names.</summary>
public sealed record CompletedWorkOrder(
    Guid WorkOrderId,
    string Number,
    Guid AssetId,
    string Title,
    DateTimeOffset SubmittedAt,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    bool AssetDown,
    string Priority,
    string Source,
    DateTimeOffset DueAt);

/// <param name="Number">Display number, e.g. "WO-000042".</param>
/// <param name="Status">The status name, e.g. "Assigned" or "InProgress" (a string so the contract does not leak the internal enum).</param>
/// <param name="AssignedToId">The assigned technician's subject id; null until assigned.</param>
public sealed record WorkOrderSummary(Guid Id, string Number, string Status, string? AssignedToId);
