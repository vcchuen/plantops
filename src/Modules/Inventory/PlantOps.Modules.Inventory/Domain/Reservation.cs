namespace PlantOps.Modules.Inventory.Domain;

/// <summary>Stock held on a part for one work order. Child of <see cref="SparePart"/>; only the aggregate changes it.</summary>
internal sealed class Reservation
{
    public const int WorkOrderNumberMaxLength = 20;
    public const int PersonIdMaxLength = 200;
    public const int PersonNameMaxLength = 200;

    // For EF Core only.
    private Reservation()
    {
    }

    internal Reservation(
        SparePartId sparePartId,
        Guid workOrderId,
        string workOrderNumber,
        int quantity,
        string reservedById,
        string reservedByName,
        DateTimeOffset reservedAt)
    {
        Id = Guid.CreateVersion7();
        SparePartId = sparePartId;
        WorkOrderId = workOrderId;
        WorkOrderNumber = workOrderNumber;
        Quantity = quantity;
        Status = ReservationStatus.Active;
        ReservedById = reservedById;
        ReservedByName = reservedByName;
        ReservedAt = reservedAt;
    }

    public Guid Id { get; private set; }

    public SparePartId SparePartId { get; private set; }

    public Guid WorkOrderId { get; private set; }

    // Snapshot (ADR-0002: no cross-schema join, and the number never changes anyway).
    public string WorkOrderNumber { get; private set; } = null!;

    public int Quantity { get; private set; }

    public ReservationStatus Status { get; private set; }

    public string ReservedById { get; private set; } = null!;

    public string ReservedByName { get; private set; } = null!;

    public DateTimeOffset ReservedAt { get; private set; }

    /// <summary>When it stopped being Active (consumed or released).</summary>
    public DateTimeOffset? ResolvedAt { get; private set; }

    internal void Increase(int quantity) => Quantity += quantity;

    internal void Resolve(ReservationStatus status, DateTimeOffset now)
    {
        Status = status;
        ResolvedAt = now;
    }
}
