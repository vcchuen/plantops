using Microsoft.EntityFrameworkCore;

namespace PlantOps.BuildingBlocks.Infrastructure;

public static class OutboxModelBuilderExtensions
{
    /// <summary>
    /// Maps <see cref="OutboxMessage"/> to "OutboxMessages" in the context's default schema (each producing module
    /// keeps its own outbox, ADR-0002). Call after <c>HasDefaultSchema</c>.
    /// </summary>
    public static ModelBuilder ApplyOutbox(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(builder =>
        {
            builder.ToTable("OutboxMessages");
            builder.HasKey(m => m.Id);
            builder.Property(m => m.Id).ValueGeneratedNever();
            builder.Property(m => m.Type).HasMaxLength(OutboxMessage.TypeMaxLength);
            builder.Property(m => m.LastError).HasMaxLength(OutboxMessage.LastErrorMaxLength);

            // The dispatcher's only query is "oldest unprocessed first". Filtered, so the index holds just the
            // backlog and stays tiny however many processed rows pile up.
            builder.HasIndex(m => new { m.ProcessedAt, m.OccurredAt })
                .HasFilter("[ProcessedAt] IS NULL")
                .HasDatabaseName("IX_OutboxMessages_Pending");

            builder.Ignore(m => m.IsParked);
        });

        return modelBuilder;
    }

    /// <summary>Maps <see cref="InboxMessage"/> to "InboxMessages" with a (MessageId, Handler) key. Call after <c>HasDefaultSchema</c>.</summary>
    public static ModelBuilder ApplyInbox(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InboxMessage>(builder =>
        {
            builder.ToTable("InboxMessages");
            // The key IS the idempotency check: one row per (message, handler).
            builder.HasKey(m => new { m.MessageId, m.Handler });
            builder.Property(m => m.MessageId).ValueGeneratedNever();
            builder.Property(m => m.Handler).HasMaxLength(InboxMessage.HandlerMaxLength);
        });

        return modelBuilder;
    }
}
