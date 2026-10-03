namespace PlantOps.Modules.WorkOrders.Contracts;

/// <summary>What other modules may know about a work order. Read-only: WorkOrders owns the data.</summary>
public interface IWorkOrderDirectory
{
    Task<WorkOrderSummary?> FindAsync(Guid id, CancellationToken cancellationToken);
}

/// <param name="Number">Display number, e.g. "WO-000042".</param>
/// <param name="Status">The status name, e.g. "Assigned" or "InProgress" (a string so the contract does not leak the internal enum).</param>
/// <param name="AssignedToId">The assigned technician's subject id; null until assigned.</param>
public sealed record WorkOrderSummary(Guid Id, string Number, string Status, string? AssignedToId);
