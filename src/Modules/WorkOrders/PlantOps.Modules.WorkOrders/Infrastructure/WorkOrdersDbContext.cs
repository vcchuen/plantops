using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PlantOps.BuildingBlocks.Infrastructure;
using PlantOps.Modules.WorkOrders.Domain;

namespace PlantOps.Modules.WorkOrders.Infrastructure;

internal sealed class WorkOrdersDbContext(DbContextOptions<WorkOrdersDbContext> options) : DbContext(options)
{
    public const string Schema = "workorders";
    public const string NumberSequence = "WorkOrderNumbers";

    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<WorkOrderId>().HaveConversion<WorkOrderIdConverter>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.HasSequence<int>(NumberSequence, Schema);
        modelBuilder.ApplyConfiguration(new WorkOrderConfiguration());
        modelBuilder.ApplyAuditEntries();
        modelBuilder.ApplyOutbox();
    }
}

internal sealed class WorkOrderIdConverter()
    : ValueConverter<WorkOrderId, Guid>(id => id.Value, value => new WorkOrderId(value));
