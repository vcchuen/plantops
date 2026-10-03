using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PlantOps.BuildingBlocks.Infrastructure;
using PlantOps.Modules.WorkOrders.Contracts;
using PlantOps.Modules.WorkOrders.Domain;
using PlantOps.Modules.WorkOrders.Infrastructure;

namespace PlantOps.Modules.WorkOrders.Jobs;

/// <summary>
/// Raises the work orders PM schedules owe (design 06, decision 3). Safe to run any number of times a day, from any
/// number of hosts: a schedule that is not due is skipped, and the unique index on (PmScheduleId, PmDueOn) is the
/// final arbiter against duplicates.
/// </summary>
internal sealed class PreventiveMaintenanceRunner(
    WorkOrdersDbContext db,
    TimeProvider time,
    FactoryClock factory,
    ILogger<PreventiveMaintenanceRunner> logger) : IPreventiveMaintenanceRunner
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var today = factory.Today;
        var now = time.GetUtcNow();

        // Nothing can be due beyond the largest lead window, so that bounds the scan (IX_PmSchedules_IsActive_NextDueOn).
        // The exact per-schedule rule (own LeadDays) is applied by the aggregate below.
        var horizon = today.AddDays(PmSchedule.MaxLeadDays);
        var candidates = await db.PmSchedules
            .AsNoTracking()
            .Where(s => s.IsActive && s.NextDueOn <= horizon)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        var generated = 0;
        foreach (var id in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await GenerateForAsync(id, today, now, cancellationToken))
            {
                generated++;
            }
        }

        db.ChangeTracker.Clear();
        if (generated > 0)
        {
            logger.LogInformation("Generated {Count} preventive work order(s)", generated);
        }

        return generated;
    }

    private async Task<bool> GenerateForAsync(PmScheduleId id, DateOnly today, DateTimeOffset now, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var schedule = await db.PmSchedules.FindAsync([id], ct);
        if (schedule is null || !schedule.IsDue(today))
        {
            return false;
        }

        // Cheap pre-check; the unique index below is the real guarantee.
        if (await OccurrenceExistsAsync(schedule, ct))
        {
            await AdvanceExistingAsync(id, today, ct);
            return false;
        }

        var workOrder = schedule.GenerateIfDue(today, now, factory.EndOfDay);
        if (workOrder is null)
        {
            return false;
        }

        // The work order and the schedule's new NextDueOn commit in ONE SaveChanges (one transaction). Two commits
        // could crash in between: either a duplicate order next run (advance lost) or a lost occurrence (order lost).
        db.WorkOrders.Add(workOrder);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // The schedule's rowversion moved: a supervisor edited it, or another runner already advanced it. Nothing of
            // ours was committed (one transaction), so skip; the next run re-reads the schedule and decides afresh.
            logger.LogInformation("PM schedule {ScheduleId} changed during generation; skipped, the next run re-evaluates it", id.Value);
            return false;
        }
        catch (DbUpdateException ex) when (UniqueViolation.Is(ex))
        {
            // Another runner inserted the work order for this (schedule, due date) first (2601 on the filtered
            // unique index). Our transaction rolled back whole, including the schedule advance. Two cases remain:
            // the winner also advanced the schedule (it is no longer due: nothing to do), or the order exists with the
            // schedule NOT advanced (e.g. a supervisor moved NextDueOn back). Reload and advance in the second case so
            // the schedule does not hit the same duplicate on every run. Never retry the insert: the order exists.
            logger.LogInformation("PM work order for schedule {ScheduleId} was already generated; reconciling", id.Value);
            await AdvanceExistingAsync(id, today, ct);
            return false;
        }
    }

    private Task<bool> OccurrenceExistsAsync(PmSchedule schedule, CancellationToken ct)
    {
        var scheduleId = schedule.Id.Value;
        var dueOn = schedule.NextDueOn;
        return db.WorkOrders.AnyAsync(w => w.PmScheduleId == scheduleId && w.PmDueOn == dueOn, ct);
    }

    // Reloads the schedule (the failed save left stale tracked state) and moves it past an occurrence whose order exists.
    private async Task AdvanceExistingAsync(PmScheduleId id, DateOnly today, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var schedule = await db.PmSchedules.FindAsync([id], ct);
        if (schedule is null || !schedule.IsDue(today) || !await OccurrenceExistsAsync(schedule, ct))
        {
            return;
        }

        schedule.SkipAlreadyGenerated(today);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone else advanced it meanwhile, which is the outcome we wanted.
            logger.LogInformation("PM schedule {ScheduleId} was advanced by someone else; nothing to reconcile", id.Value);
        }
    }
}
