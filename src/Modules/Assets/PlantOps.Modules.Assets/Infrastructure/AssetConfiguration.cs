using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlantOps.Modules.Assets.Domain;

namespace PlantOps.Modules.Assets.Infrastructure;

internal sealed class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> builder)
    {
        builder.ToTable("Assets");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property<string>(Asset.TagField).HasColumnName("Tag").HasMaxLength(AssetTag.MaxLength);
        builder.Ignore(a => a.Tag);

        // Pending events are in-memory only; the interceptor turns them into AuditEntries.
        builder.Ignore(a => a.DomainEvents);

        builder.Property(a => a.Name).HasMaxLength(Asset.NameMaxLength);
        builder.Property(a => a.Manufacturer).HasMaxLength(Asset.ManufacturerMaxLength);
        builder.Property(a => a.Model).HasMaxLength(Asset.ModelMaxLength);
        builder.Property(a => a.SerialNumber).HasMaxLength(Asset.SerialNumberMaxLength);

        builder.Property(a => a.Station).HasMaxLength(Location.StationMaxLength);
        builder.Ignore(a => a.Location);

        // Strings, not ints: readable in ad-hoc SQL and reports, and reordering the enum cannot corrupt data.
        builder.Property(a => a.Criticality).HasConversion<string>().HasMaxLength(10);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);

        builder.Property(a => a.DecommissionReason).HasMaxLength(Asset.DecommissionReasonMaxLength);

        builder.HasIndex(Asset.TagField).IsUnique().HasDatabaseName("UX_Assets_Tag");
        builder.HasIndex(a => a.LineId).HasDatabaseName("IX_Assets_LineId");

        // Reference by id only (no navigation): the aggregates stay separate, the FK backs up the check at command time.
        builder.HasOne<ProductionLine>()
            .WithMany()
            .HasForeignKey(a => a.LineId)
            .HasConstraintName("FK_Assets_ProductionLines_LineId")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
