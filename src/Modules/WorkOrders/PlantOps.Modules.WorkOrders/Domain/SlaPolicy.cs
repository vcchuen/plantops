namespace PlantOps.Modules.WorkOrders.Domain;

/// <summary>
/// The resolution target for a priority, and the pure rules that turn it into a deadline and a state.
/// Nothing here reads a clock: "now" is always passed in, so every boundary is unit-testable.
/// </summary>
internal readonly record struct SlaPolicy(TimeSpan Target)
{
    public static SlaPolicy For(WorkOrderPriority priority) => new(priority switch
    {
        WorkOrderPriority.P1 => TimeSpan.FromHours(4),
        WorkOrderPriority.P2 => TimeSpan.FromHours(8),
        WorkOrderPriority.P3 => TimeSpan.FromHours(24),
        WorkOrderPriority.P4 => TimeSpan.FromHours(72),
        _ => throw new ArgumentOutOfRangeException(nameof(priority), priority, "Unknown priority."),
    });

    /// <summary>The clock starts when the problem is reported, not when someone approves it (design 04).</summary>
    public DateTimeOffset DueAt(DateTimeOffset submittedAt) => submittedAt + Target;

    /// <param name="completedAt">When the technician finished; null while the work is still open.</param>
    public SlaState Evaluate(DateTimeOffset submittedAt, DateTimeOffset? completedAt, DateTimeOffset now)
    {
        var dueAt = DueAt(submittedAt);

        if (completedAt is { } done)
        {
            // Finishing exactly at the deadline still meets it.
            return done <= dueAt ? SlaState.Met : SlaState.Missed;
        }

        // Exactly at DueAt is not yet breached: "past DueAt" means strictly later.
        if (now > dueAt)
        {
            return SlaState.Breached;
        }

        // 25 % or less remaining is at risk, including exactly 25 %. Compared as remaining * 4 <= target in
        // integer ticks, so there is no floating-point rounding at the boundary.
        return (dueAt - now).Ticks * 4 <= Target.Ticks ? SlaState.AtRisk : SlaState.OnTrack;
    }
}
