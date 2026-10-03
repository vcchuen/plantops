using Microsoft.EntityFrameworkCore;
using PlantOps.BuildingBlocks.Infrastructure;
using PlantOps.Modules.Reporting.Domain;

namespace PlantOps.Modules.Reporting.Infrastructure;

internal sealed class ReportingDbContext(DbContextOptions<ReportingDbContext> options) : DbContext(options)
{
    public const string Schema = "reporting";

    public DbSet<WorkOrderFact> WorkOrderFacts => Set<WorkOrderFact>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new WorkOrderFactConfiguration());
        // No audit entries: the facts are derived data, rebuildable from the source modules. The inbox makes the
        // event handler idempotent (ADR-0009).
        modelBuilder.ApplyInbox();
    }
}
