using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlantOps.Modules.Assets.Domain;

namespace PlantOps.Modules.Assets.Infrastructure;

internal sealed class MaintenanceRecordConfiguration : IEntityTypeConfiguration<MaintenanceRecord>
{
    public void Configure(EntityTypeBuilder<MaintenanceRecord> builder)
    {
        builder.ToTable("MaintenanceRecords");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.Number).HasMaxLength(MaintenanceRecord.NumberMaxLength);
        builder.Property(m => m.Title).HasMaxLength(MaintenanceRecord.TitleMaxLength);
        builder.Property(m => m.Resolution).HasMaxLength(MaintenanceRecord.ResolutionMaxLength);
        builder.Property(m => m.TechnicianName).HasMaxLength(MaintenanceRecord.TechnicianNameMaxLength);

        builder.HasIndex(m => m.WorkOrderId).IsUnique().HasDatabaseName("UX_MaintenanceRecords_WorkOrderId");

        // "History of this machine, newest first."
        builder.HasIndex(m => new { m.AssetId, m.CompletedAt }).HasDatabaseName("IX_MaintenanceRecords_AssetId_CompletedAt");
    }
}
