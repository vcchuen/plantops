using Microsoft.EntityFrameworkCore;

namespace PlantOps.BuildingBlocks.Infrastructure;

/// <summary>Makes an integration-event handler idempotent (ADR-0009, "idempotent inbox").</summary>
public static class InboxGuard
{
    /// <summary>
    /// Runs <paramref name="effect"/> at most once per (message, handler). The effect only changes tracked entities;
    /// this method adds the inbox row and does the single SaveChanges, so effect and "done" marker commit together.
    /// </summary>
    /// <returns>True when the effect was applied; false when the message had already been handled.</returns>
    public static async Task<bool> RunOnceAsync(
        this DbContext db,
        Guid messageId,
        string handler,
        TimeProvider time,
        Func<CancellationToken, Task> effect,
        CancellationToken cancellationToken)
    {
        // Fast path for the common redelivery: skip the work. It is only an optimisation, not the guarantee:
        // two deliveries can both pass this check, which is why the primary key below is the real arbiter.
        if (await db.Set<InboxMessage>().AnyAsync(m => m.MessageId == messageId && m.Handler == handler, cancellationToken))
        {
            return false;
        }

        await effect(cancellationToken);
        db.Set<InboxMessage>().Add(new InboxMessage(messageId, handler, time.GetUtcNow()));

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (UniqueViolation.Is(ex))
        {
            // A concurrent delivery won the race (inbox key, or the effect's own unique index). Nothing of ours was
            // committed, so "already handled" is the truth. DbUpdateConcurrencyException has no SqlException
            // inside, so it is not swallowed here: callers retry it.
            db.ChangeTracker.Clear();
            return false;
        }
    }
}
