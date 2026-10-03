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

        // Every report filters on [from, to) over CompletedAt. Chosen from measured plans (perf run 37091237887,
        // docs/study/07): the design's first candidate left CompletedMonth/AssetId/AssetTag uncovered, so "by month" and
        // "by asset" still scanned the clustered index (894 reads, 0 % drop). Covering every column the report plans read
        // turned all of them into an Index Seek: 894 → 48 logical reads.
        builder.HasIndex(f => f.CompletedAt)
            .IncludeProperties(f => new
            {
                f.LineId,
                f.LineName,
                f.Priority,
                f.MetSla,
                f.RepairMinutes,
                f.DowntimeMinutes,
                f.AssetId,
                f.AssetTag,
                f.CompletedMonth,
            })
            .HasDatabaseName("IX_WorkOrderFacts_CompletedAt");
    }
}
