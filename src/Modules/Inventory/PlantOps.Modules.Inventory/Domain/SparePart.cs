using PlantOps.SharedKernel;

namespace PlantOps.Modules.Inventory.Domain;

/// <summary>
/// A stocked part. The aggregate guards 0 &lt;= Reserved &lt;= OnHand, so no caller can break it. Reservations are
/// children: they are only changed through this root, which is what makes the invariant enforceable.
/// Methods expect <see cref="Reservations"/> to contain at least the Active reservations (queries load only those).
/// </summary>
internal sealed class SparePart : AggregateRoot
{
    public const int PartNumberMaxLength = 40;
    public const int NameMaxLength = 200;
    public const int UnitMaxLength = 20;
    public const int BinLocationMaxLength = 50;

    private readonly List<Reservation> _reservations = [];

    // For EF Core only.
    private SparePart()
    {
    }

    public SparePartId Id { get; private set; }

    public override string AggregateId => Id.Value.ToString();

    public string PartNumber { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string Unit { get; private set; } = null!;

    public string BinLocation { get; private set; } = null!;

    public int QuantityOnHand { get; private set; }

    public int QuantityReserved { get; private set; }

    public int ReorderLevel { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<Reservation> Reservations => _reservations;

    /// <summary>Free stock: what a new reservation can draw on. Derived, never stored.</summary>
    public int QuantityAvailable => QuantityOnHand - QuantityReserved;

    public bool IsLowStock => QuantityAvailable <= ReorderLevel;

    public static SparePart Register(string partNumber, string name, string unit, string binLocation, int reorderLevel)
    {
        if (reorderLevel < 0)
        {
            throw new DomainException("Reorder level cannot be negative.");
        }

        var part = new SparePart
        {
            Id = SparePartId.New(),
            // Upper-case so "fdr-8mm-001" and "FDR-8MM-001" are one part, and the unique index needs no collation tricks.
            PartNumber = TextRules.Required(partNumber, "Part number", PartNumberMaxLength).ToUpperInvariant(),
            Name = TextRules.Required(name, "Name", NameMaxLength),
            Unit = TextRules.Required(unit, "Unit", UnitMaxLength),
            BinLocation = TextRules.Required(binLocation, "Bin location", BinLocationMaxLength),
            ReorderLevel = reorderLevel,
        };

        part.Raise(new PartRegistered(part.Id.Value, part.PartNumber, part.Name, part.Unit, part.BinLocation, reorderLevel));
        return part;
    }

    public void Receive(int quantity)
    {
        EnsurePositive(quantity);

        QuantityOnHand += quantity;
        Raise(new StockReceived(Id.Value, quantity, QuantityOnHand));
    }

    /// <summary>
    /// Holds stock for a work order. A second call for the same work order grows the existing active reservation
    /// (at most one active reservation per part and work order).
    /// </summary>
    public Reservation Reserve(Guid workOrderId, string workOrderNumber, int quantity, Actor by, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(by);
        EnsurePositive(quantity);

        if (quantity > QuantityAvailable)
        {
            throw new InsufficientStockException(QuantityAvailable, Unit, PartNumber);
        }

        var reservation = ActiveFor(workOrderId);
        if (reservation is null)
        {
            reservation = new Reservation(Id, workOrderId, workOrderNumber, quantity, by.Id, by.Name, now);
            _reservations.Add(reservation);
        }
        else
        {
            reservation.Increase(quantity);
        }

        QuantityReserved += quantity;
        Raise(new PartReserved(Id.Value, reservation.Id, workOrderId, reservation.WorkOrderNumber, quantity, reservation.Quantity));
        return reservation;
    }

    /// <summary>Gives a specific active reservation back to the shelf.</summary>
    public void Release(Guid reservationId, DateTimeOffset now)
    {
        var reservation = _reservations.FirstOrDefault(r => r.Id == reservationId)
            ?? throw new NotFoundException($"Reservation '{reservationId}' was not found.");
        if (reservation.Status != ReservationStatus.Active)
        {
            throw new DomainException($"Reservation is already {reservation.Status}; only an active reservation can be released.");
        }

        ReleaseReservation(reservation, now);
    }

    /// <summary>Work order cancelled: release its active reservation, if any. Idempotent by nature (no active one, no change).</summary>
    /// <returns>True when something was released.</returns>
    public bool ReleaseFor(Guid workOrderId, DateTimeOffset now)
    {
        var reservation = ActiveFor(workOrderId);
        if (reservation is null)
        {
            return false;
        }

        ReleaseReservation(reservation, now);
        return true;
    }

    /// <summary>Work order completed: the reserved stock leaves the shelf (OnHand and Reserved both drop).</summary>
    /// <returns>True when something was consumed.</returns>
    public bool ConsumeFor(Guid workOrderId, DateTimeOffset now)
    {
        var reservation = ActiveFor(workOrderId);
        if (reservation is null)
        {
            return false;
        }

        QuantityOnHand -= reservation.Quantity;
        QuantityReserved -= reservation.Quantity;
        reservation.Resolve(ReservationStatus.Consumed, now);
        Raise(new ReservationsConsumed(Id.Value, reservation.Id, workOrderId, reservation.Quantity));
        return true;
    }

    private void ReleaseReservation(Reservation reservation, DateTimeOffset now)
    {
        QuantityReserved -= reservation.Quantity;
        reservation.Resolve(ReservationStatus.Released, now);
        Raise(new ReservationReleased(Id.Value, reservation.Id, reservation.WorkOrderId, reservation.Quantity));
    }

    private Reservation? ActiveFor(Guid workOrderId) =>
        _reservations.FirstOrDefault(r => r.WorkOrderId == workOrderId && r.Status == ReservationStatus.Active);

    private static void EnsurePositive(int quantity)
    {
        if (quantity <= 0)
        {
            throw new DomainException("Quantity must be greater than zero.");
        }
    }
}
