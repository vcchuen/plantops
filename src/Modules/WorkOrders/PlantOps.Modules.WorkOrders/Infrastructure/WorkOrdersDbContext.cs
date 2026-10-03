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

    public DbSet<PmSchedule> PmSchedules => Set<PmSchedule>();

    public DbSet<WorkOrderComment> WorkOrderComments => Set<WorkOrderComment>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<WorkOrderId>().HaveConversion<WorkOrderIdConverter>();
        configurationBuilder.Properties<PmScheduleId>().HaveConversion<PmScheduleIdConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.HasSequence<int>(NumberSequence, Schema);
        modelBuilder.ApplyConfiguration(new WorkOrderConfiguration());
        modelBuilder.ApplyConfiguration(new PmScheduleConfiguration());
        modelBuilder.ApplyConfiguration(new WorkOrderCommentConfiguration());
        modelBuilder.ApplyAuditEntries();
        modelBuilder.ApplyOutbox();
    }
}

internal sealed class WorkOrderIdConverter()
    : ValueConverter<WorkOrderId, Guid>(id => id.Value, value => new WorkOrderId(value));

internal sealed class PmScheduleIdConverter()
    : ValueConverter<PmScheduleId, Guid>(id => id.Value, value => new PmScheduleId(value));
