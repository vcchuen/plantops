using Microsoft.EntityFrameworkCore;

namespace PlantOps.BuildingBlocks.Infrastructure;

public static class AuditModelBuilderExtensions
{
    /// <summary>
    /// Maps <see cref="AuditEntry"/> to "AuditEntries" in the context's default schema, so every module keeps its
    /// own audit table (ADR-0002). Call after <c>HasDefaultSchema</c>.
    /// </summary>
    public static ModelBuilder ApplyAuditEntries(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditEntry>(builder =>
        {
            builder.ToTable("AuditEntries");
            builder.HasKey(e => e.Id);
            builder.Property(e => e.Id).ValueGeneratedNever();
            builder.Property(e => e.AggregateType).HasMaxLength(AuditEntry.TypeMaxLength);
            builder.Property(e => e.AggregateId).HasMaxLength(AuditEntry.AggregateIdMaxLength);
            builder.Property(e => e.EventType).HasMaxLength(AuditEntry.TypeMaxLength);
            builder.Property(e => e.ActorId).HasMaxLength(AuditEntry.ActorMaxLength);
            builder.Property(e => e.ActorName).HasMaxLength(AuditEntry.ActorMaxLength);

            // History is always "everything about this aggregate, newest first".
            builder.HasIndex(e => new { e.AggregateId, e.OccurredAt }).HasDatabaseName("IX_AuditEntries_AggregateId_OccurredAt");
        });

        return modelBuilder;
    }
}
