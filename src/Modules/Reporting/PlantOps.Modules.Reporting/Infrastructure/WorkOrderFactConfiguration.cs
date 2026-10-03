using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlantOps.Modules.Reporting.Domain;

namespace PlantOps.Modules.Reporting.Infrastructure;

internal sealed class WorkOrderFactConfiguration : IEntityTypeConfiguration<WorkOrderFact>
{
    public void Configure(EntityTypeBuilder<WorkOrderFact> builder)
    {
        builder.ToTable("WorkOrderFacts");

        // The work order's own id is the key: one fact per completed work order, which is also what makes the
        // projection (and the rebuild) an upsert.
        builder.HasKey(f => f.WorkOrderId);
        builder.Property(f => f.WorkOrderId).ValueGeneratedNever();

        builder.Property(f => f.Number).HasMaxLength(WorkOrderFact.NumberMaxLength);
        builder.Property(f => f.AssetTag).HasMaxLength(WorkOrderFact.AssetTagMaxLength);
        builder.Property(f => f.LineName).HasMaxLength(WorkOrderFact.LineNameMaxLength);
        builder.Property(f => f.Priority).HasMaxLength(WorkOrderFact.PriorityMaxLength);
        builder.Property(f => f.Source).HasMaxLength(WorkOrderFact.SourceMaxLength);
        builder.Property(f => f.CompletedMonth).HasColumnType("date");

        // Deliberately NO index on CompletedAt yet. Every report filters on [from, to) over CompletedAt, so a covering
        // index (CompletedAt INCLUDE LineId, LineName, Priority, MetSla, RepairMinutes, DowntimeMinutes) is the obvious
        // candidate, but design 07 Decision 3 adds it only if the measured query plans justify it.
    }
}
