using PlantOps.Modules.Inventory.Domain;
using PlantOps.SharedKernel;

namespace PlantOps.Modules.Inventory.Tests.Domain;

public class SparePartTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 3, 8, 0, 0, TimeSpan.Zero);
    private static readonly Actor Tom = new("tom", "Tom");
    private static readonly Guid Wo1 = Guid.NewGuid();
    private static readonly Guid Wo2 = Guid.NewGuid();

    private static SparePart Part(int onHand = 5, int reorderLevel = 2)
    {
        var part = SparePart.Register("fdr-8mm-001", "Feeder 8mm spring", "pcs", "A-03-2", reorderLevel);
        if (onHand > 0)
        {
            part.Receive(onHand);
        }

        part.ClearDomainEvents();
        return part;
    }

    [Fact]
    public void Register_normalises_the_part_number_and_starts_empty()
    {
        var part = SparePart.Register("  fdr-8mm-001 ", "Feeder 8mm spring", "pcs", "A-03-2", 2);

        Assert.Equal("FDR-8MM-001", part.PartNumber);
        Assert.Equal(0, part.QuantityOnHand);
        Assert.Equal(0, part.QuantityReserved);
        Assert.IsType<PartRegistered>(Assert.Single(part.DomainEvents));
    }

    [Theory]
    [InlineData("", "Name", "pcs", "A-1")]
    [InlineData("P-1", " ", "pcs", "A-1")]
    [InlineData("P-1", "Name", "", "A-1")]
    [InlineData("P-1", "Name", "pcs", " ")]
    public void Register_requires_its_text_fields(string number, string name, string unit, string bin) =>
        Assert.Throws<DomainException>(() => SparePart.Register(number, name, unit, bin, 1));

    [Fact]
    public void Register_rejects_a_negative_reorder_level() =>
        Assert.Throws<DomainException>(() => SparePart.Register("P-1", "Name", "pcs", "A-1", -1));

    [Fact]
    public void Receive_increases_stock_and_raises_an_event()
    {
        var part = Part(onHand: 0);

        part.Receive(7);

        Assert.Equal(7, part.QuantityOnHand);
        var raised = Assert.IsType<StockReceived>(Assert.Single(part.DomainEvents));
        Assert.Equal(7, raised.QuantityOnHand);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Quantities_must_be_positive(int quantity)
    {
        var part = Part();

        Assert.Throws<DomainException>(() => part.Receive(quantity));
        Assert.Throws<DomainException>(() => part.Reserve(Wo1, "WO-000001", quantity, Tom, T0));
        Assert.Equal(5, part.QuantityOnHand);
        Assert.Equal(0, part.QuantityReserved);
    }

    [Fact]
    public void Reserve_holds_stock_without_removing_it()
    {
        var part = Part();

        var reservation = part.Reserve(Wo1, "WO-000001", 2, Tom, T0);

        Assert.Equal(5, part.QuantityOnHand);
        Assert.Equal(2, part.QuantityReserved);
        Assert.Equal(3, part.QuantityAvailable);
        Assert.Equal(ReservationStatus.Active, reservation.Status);
        Assert.Equal("WO-000001", reservation.WorkOrderNumber);
        Assert.Equal("Tom", reservation.ReservedByName);
        Assert.IsType<PartReserved>(Assert.Single(part.DomainEvents));
    }

    [Fact]
    public void Reserving_more_than_available_throws_a_conflict_with_the_designed_message()
    {
        var part = Part(onHand: 5);
        part.Reserve(Wo1, "WO-000001", 3, Tom, T0);

        var ex = Assert.Throws<InsufficientStockException>(() => part.Reserve(Wo2, "WO-000002", 3, Tom, T0));

        Assert.Equal("Only 2 pcs of FDR-8MM-001 available", ex.Message);
        Assert.IsAssignableFrom<ConflictException>(ex);
        Assert.Equal(3, part.QuantityReserved);
    }

    [Fact]
    public void Reserving_exactly_the_available_quantity_is_allowed()
    {
        var part = Part(onHand: 1);

        part.Reserve(Wo1, "WO-000001", 1, Tom, T0);

        Assert.Equal(0, part.QuantityAvailable);
    }

    [Fact]
    public void Reserving_again_for_the_same_work_order_increases_the_one_active_reservation()
    {
        var part = Part();

        var first = part.Reserve(Wo1, "WO-000001", 1, Tom, T0);
        var second = part.Reserve(Wo1, "WO-000001", 2, Tom, T0);

        Assert.Same(first, second);
        Assert.Single(part.Reservations);
        Assert.Equal(3, second.Quantity);
        Assert.Equal(3, part.QuantityReserved);
    }

    [Fact]
    public void The_increase_is_checked_against_available_stock_too()
    {
        var part = Part(onHand: 3);
        part.Reserve(Wo1, "WO-000001", 2, Tom, T0);

        Assert.Throws<InsufficientStockException>(() => part.Reserve(Wo1, "WO-000001", 2, Tom, T0));
        Assert.Equal(2, part.QuantityReserved);
    }

    [Fact]
    public void Release_returns_the_stock_to_available()
    {
        var part = Part();
        var reservation = part.Reserve(Wo1, "WO-000001", 4, Tom, T0);
        part.ClearDomainEvents();

        part.Release(reservation.Id, T0.AddMinutes(5));

        Assert.Equal(0, part.QuantityReserved);
        Assert.Equal(5, part.QuantityOnHand);
        Assert.Equal(ReservationStatus.Released, reservation.Status);
        Assert.Equal(T0.AddMinutes(5), reservation.ResolvedAt);
        Assert.IsType<ReservationReleased>(Assert.Single(part.DomainEvents));
    }

    [Fact]
    public void Releasing_twice_is_a_business_rule_error_and_does_not_double_release()
    {
        var part = Part();
        var reservation = part.Reserve(Wo1, "WO-000001", 2, Tom, T0);
        part.Reserve(Wo2, "WO-000002", 1, Tom, T0);
        part.Release(reservation.Id, T0);

        Assert.Throws<DomainException>(() => part.Release(reservation.Id, T0));
        Assert.Equal(1, part.QuantityReserved);
    }

    [Fact]
    public void Releasing_an_unknown_reservation_is_not_found() =>
        Assert.Throws<NotFoundException>(() => Part().Release(Guid.NewGuid(), T0));

    [Fact]
    public void A_released_work_order_can_reserve_again_as_a_new_reservation()
    {
        var part = Part();
        var first = part.Reserve(Wo1, "WO-000001", 1, Tom, T0);
        part.Release(first.Id, T0);

        var second = part.Reserve(Wo1, "WO-000001", 1, Tom, T0);

        Assert.NotSame(first, second);
        Assert.Equal(1, part.QuantityReserved);
    }

    [Fact]
    public void ConsumeFor_removes_the_stock_from_both_on_hand_and_reserved()
    {
        var part = Part(onHand: 5);
        var reservation = part.Reserve(Wo1, "WO-000001", 2, Tom, T0);
        part.Reserve(Wo2, "WO-000002", 1, Tom, T0);
        part.ClearDomainEvents();

        var consumed = part.ConsumeFor(Wo1, T0.AddHours(1));

        Assert.True(consumed);
        Assert.Equal(3, part.QuantityOnHand);
        Assert.Equal(1, part.QuantityReserved); // the other work order's hold is untouched
        Assert.Equal(ReservationStatus.Consumed, reservation.Status);
        Assert.IsType<ReservationsConsumed>(Assert.Single(part.DomainEvents));
    }

    [Fact]
    public void ConsumeFor_without_an_active_reservation_does_nothing()
    {
        var part = Part();

        Assert.False(part.ConsumeFor(Wo1, T0));

        Assert.Equal(5, part.QuantityOnHand);
        Assert.Empty(part.DomainEvents);
    }

    [Fact]
    public void ConsumeFor_ignores_reservations_that_are_no_longer_active()
    {
        var part = Part();
        var reservation = part.Reserve(Wo1, "WO-000001", 2, Tom, T0);
        part.Release(reservation.Id, T0);

        Assert.False(part.ConsumeFor(Wo1, T0));
        Assert.Equal(5, part.QuantityOnHand);
    }

    [Fact]
    public void ReleaseFor_gives_back_the_cancelled_work_orders_stock()
    {
        var part = Part();
        part.Reserve(Wo1, "WO-000001", 2, Tom, T0);

        Assert.True(part.ReleaseFor(Wo1, T0));
        Assert.Equal(0, part.QuantityReserved);
        Assert.Equal(5, part.QuantityOnHand);
        Assert.False(part.ReleaseFor(Wo1, T0)); // second call: nothing active, no change
    }

    [Theory]
    [InlineData(5, 2, 0, false)] // available 5 > reorder 2
    [InlineData(5, 2, 2, false)] // available 3 > reorder 2
    [InlineData(5, 2, 3, true)] // available 2 == reorder 2: the boundary counts as low
    [InlineData(5, 2, 5, true)]
    public void Low_stock_means_available_at_or_below_the_reorder_level(int onHand, int reorder, int reserved, bool expected)
    {
        var part = Part(onHand, reorder);
        if (reserved > 0)
        {
            part.Reserve(Wo1, "WO-000001", reserved, Tom, T0);
        }

        Assert.Equal(expected, part.IsLowStock);
    }
}
