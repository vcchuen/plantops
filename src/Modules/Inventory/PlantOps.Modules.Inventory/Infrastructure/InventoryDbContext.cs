using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PlantOps.BuildingBlocks.Infrastructure;
using PlantOps.Modules.Inventory.Domain;

namespace PlantOps.Modules.Inventory.Infrastructure;

internal sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public const string Schema = "inventory";

    public DbSet<SparePart> SpareParts => Set<SparePart>();

    /// <summary>For read queries that span parts (the reservations list). Writes go through <see cref="SparePart"/>.</summary>
    public DbSet<Reservation> Reservations => Set<Reservation>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<SparePartId>().HaveConversion<SparePartIdConverter>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new SparePartConfiguration());
        modelBuilder.ApplyConfiguration(new ReservationConfiguration());
        modelBuilder.ApplyAuditEntries();
        modelBuilder.ApplyInbox();
    }
}

internal sealed class SparePartIdConverter()
    : ValueConverter<SparePartId, Guid>(id => id.Value, value => new SparePartId(value));
