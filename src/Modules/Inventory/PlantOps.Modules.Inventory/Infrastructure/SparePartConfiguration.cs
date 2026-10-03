using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlantOps.Modules.Inventory.Domain;

namespace PlantOps.Modules.Inventory.Infrastructure;

internal sealed class SparePartConfiguration : IEntityTypeConfiguration<SparePart>
{
    public void Configure(EntityTypeBuilder<SparePart> builder)
    {
        // The CHECK is the last line of defence behind the aggregate's own invariant: even a bug or a hand-written
        // UPDATE cannot leave stock negative or over-reserved.
        builder.ToTable("SpareParts", t => t.HasCheckConstraint(
            "CK_SpareParts_Quantities",
            "[QuantityReserved] >= 0 AND [QuantityReserved] <= [QuantityOnHand] AND [ReorderLevel] >= 0"));
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.PartNumber).HasMaxLength(SparePart.PartNumberMaxLength);
        builder.Property(p => p.Name).HasMaxLength(SparePart.NameMaxLength);
        builder.Property(p => p.Unit).HasMaxLength(SparePart.UnitMaxLength);
        builder.Property(p => p.BinLocation).HasMaxLength(SparePart.BinLocationMaxLength);

        builder.HasIndex(p => p.PartNumber).IsUnique().HasDatabaseName("UX_SpareParts_PartNumber");

        // The database bumps it on every UPDATE; EF adds "AND RowVersion = @original" to the WHERE. This is what makes
        // two reservations of the last unit collide: the second UPDATE matches zero rows (see ConcurrencyRetry).
        builder.Property(p => p.RowVersion).IsRowVersion();

        // Derived, not stored.
        builder.Ignore(p => p.QuantityAvailable);
        builder.Ignore(p => p.IsLowStock);
        builder.Ignore(p => p.DomainEvents);

        builder.HasMany(p => p.Reservations)
            .WithOne()
            .HasForeignKey(r => r.SparePartId)
            .HasConstraintName("FK_Reservations_SpareParts_SparePartId")
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Reservations).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("Reservations", t => t.HasCheckConstraint("CK_Reservations_Quantity", "[Quantity] > 0"));
        builder.HasKey(r => r.Id);
        // Never store-generated: with a client-assigned Guid, EF would otherwise take a "new" reservation found
        // through the part's collection for an existing row (key set + generated = Modified) and emit an UPDATE.
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.WorkOrderNumber).HasMaxLength(Reservation.WorkOrderNumberMaxLength);
        builder.Property(r => r.ReservedById).HasMaxLength(Reservation.PersonIdMaxLength);
        builder.Property(r => r.ReservedByName).HasMaxLength(Reservation.PersonNameMaxLength);

        // Strings, not ints: readable in ad-hoc SQL and reordering the enum cannot corrupt data.
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);

        // "One active reservation per (part, work order)" enforced by the database, not just by Reserve(): a
        // filtered unique index only constrains Active rows, so released/consumed history can repeat.
        builder.HasIndex(r => new { r.SparePartId, r.WorkOrderId })
            .IsUnique()
            .HasFilter("[Status] = 'Active'")
            .HasDatabaseName("UX_Reservations_Active_PartId_WorkOrderId");

        // "What is reserved for this work order?" (the work order page).
        builder.HasIndex(r => r.WorkOrderId).HasDatabaseName("IX_Reservations_WorkOrderId");
    }
}
