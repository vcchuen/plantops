using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlantOps.Modules.WorkOrders.Domain;

namespace PlantOps.Modules.WorkOrders.Infrastructure;

internal sealed class PmScheduleConfiguration : IEntityTypeConfiguration<PmSchedule>
{
    public void Configure(EntityTypeBuilder<PmSchedule> builder)
    {
        builder.ToTable("PmSchedules");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.AssetTag).HasMaxLength(WorkOrder.AssetTagMaxLength);
        builder.Property(s => s.AssetName).HasMaxLength(WorkOrder.AssetNameMaxLength);
        builder.Property(s => s.Title).HasMaxLength(PmSchedule.TitleMaxLength);
        builder.Property(s => s.Instructions).HasMaxLength(PmSchedule.InstructionsMaxLength);
        builder.Property(s => s.Priority).HasConversion<string>().HasMaxLength(2);

        // DateOnly maps to a SQL "date": a calendar day with no time zone, which is what a due date is.
        builder.Property(s => s.NextDueOn).HasColumnType("date");

        builder.Property(s => s.RowVersion).IsRowVersion();

        // The runner's scan (active schedules by next due date) and the per-asset list filter.
        builder.HasIndex(s => new { s.IsActive, s.NextDueOn }).HasDatabaseName("IX_PmSchedules_IsActive_NextDueOn");
        builder.HasIndex(s => s.AssetId).HasDatabaseName("IX_PmSchedules_AssetId");

        builder.Ignore(s => s.DomainEvents);
    }
}
