using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlantOps.Modules.WorkOrders.Domain;

namespace PlantOps.Modules.WorkOrders.Infrastructure;

internal sealed class WorkOrderConfiguration : IEntityTypeConfiguration<WorkOrder>
{
    public void Configure(EntityTypeBuilder<WorkOrder> builder)
    {
        builder.ToTable("WorkOrders");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).ValueGeneratedNever();

        // The database hands out the number at insert, so there is no MAX()+1 race. A sequence (not IDENTITY)
        // because Number is not the key (the sequence itself is declared in the DbContext). EF reads the generated
        // value back with the INSERT, so the entity has its Number right after SaveChanges.
        builder.Property(w => w.Number)
            .HasDefaultValueSql($"NEXT VALUE FOR [{WorkOrdersDbContext.Schema}].[{WorkOrdersDbContext.NumberSequence}]")
            .ValueGeneratedOnAdd();
        builder.HasIndex(w => w.Number).IsUnique().HasDatabaseName("UX_WorkOrders_Number");

        builder.Property(w => w.AssetTag).HasMaxLength(WorkOrder.AssetTagMaxLength);
        builder.Property(w => w.AssetName).HasMaxLength(WorkOrder.AssetNameMaxLength);
        builder.Property(w => w.Title).HasMaxLength(WorkOrder.TitleMaxLength);
        builder.Property(w => w.Description).HasMaxLength(WorkOrder.DescriptionMaxLength);

        // Strings, not ints: readable in ad-hoc SQL and reports, and reordering the enum cannot corrupt data.
        builder.Property(w => w.Priority).HasConversion<string>().HasMaxLength(2);
        builder.Property(w => w.Status).HasConversion<string>().HasMaxLength(20);

        builder.Property(w => w.ReportedById).HasMaxLength(WorkOrder.PersonIdMaxLength);
        builder.Property(w => w.ReportedByName).HasMaxLength(WorkOrder.PersonNameMaxLength);
        builder.Property(w => w.ApprovedById).HasMaxLength(WorkOrder.PersonIdMaxLength);
        builder.Property(w => w.ApprovedByName).HasMaxLength(WorkOrder.PersonNameMaxLength);
        builder.Property(w => w.AssignedToId).HasMaxLength(WorkOrder.PersonIdMaxLength);
        builder.Property(w => w.AssignedToName).HasMaxLength(WorkOrder.PersonNameMaxLength);

        builder.Property(w => w.Resolution).HasMaxLength(WorkOrder.ResolutionMaxLength);
        builder.Property(w => w.RejectionReason).HasMaxLength(WorkOrder.ReasonMaxLength);
        builder.Property(w => w.CancellationReason).HasMaxLength(WorkOrder.ReasonMaxLength);

        // The database bumps it on every UPDATE; EF adds "AND RowVersion = @original" to the WHERE (ADR-0008).
        builder.Property(w => w.RowVersion).IsRowVersion();

        // The list filters (status, "assigned to me", per-asset).
        builder.HasIndex(w => w.Status).HasDatabaseName("IX_WorkOrders_Status");
        builder.HasIndex(w => w.AssignedToId).HasDatabaseName("IX_WorkOrders_AssignedToId");
        builder.HasIndex(w => w.AssetId).HasDatabaseName("IX_WorkOrders_AssetId");

        // Existing rows (and any insert that leaves it at the CLR default) are Reactive, so adding the column to a
        // populated table needs no hand-written backfill: the default lives in the model, hence in the snapshot.
        builder.Property(w => w.Source).HasConversion<string>().HasMaxLength(20).HasDefaultValue(WorkOrderSource.Reactive);
        builder.Property(w => w.PmDueOn).HasColumnType("date");

        // The arbiter of "no duplicate PM work order" (design 06): one work order per schedule occurrence. Filtered so
        // the many reactive rows (PmScheduleId NULL) are not constrained. The runner pre-checks too, but two timers
        // racing both pass the check, and only this index stops the second insert.
        builder.HasIndex(w => new { w.PmScheduleId, w.PmDueOn })
            .IsUnique()
            .HasFilter("[PmScheduleId] IS NOT NULL")
            .HasDatabaseName("UX_WorkOrders_PmSchedule_DueOn");

        // The escalation scan: "not yet escalated, ordered/filtered by deadline". Filtered, so it only holds the
        // rows the job still has to look at and shrinks as work orders are escalated or never breach.
        builder.HasIndex(w => w.DueAt)
            .HasFilter("[EscalatedAt] IS NULL")
            .HasDatabaseName("IX_WorkOrders_DueAt_NotEscalated");

        // Pending events are in-memory only; the interceptor turns them into AuditEntries.
        builder.Ignore(w => w.DomainEvents);
    }
}
