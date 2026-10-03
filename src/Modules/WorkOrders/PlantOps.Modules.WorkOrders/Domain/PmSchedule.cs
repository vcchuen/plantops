using PlantOps.SharedKernel;

namespace PlantOps.Modules.WorkOrders.Domain;

/// <summary>
/// "Clean reflow oven RF-02 every 30 days": a recurring obligation that raises preventive work orders ahead of time
/// (design 06). The aggregate owns the date arithmetic; the runner owns the clock, the database and the retry rules.
/// </summary>
internal sealed class PmSchedule : AggregateRoot
{
    public const int TitleMaxLength = 200;
    public const int InstructionsMaxLength = 2000;
    public const int MinIntervalDays = 1;
    public const int MaxIntervalDays = 365;
    public const int MinLeadDays = 0;
    public const int MaxLeadDays = 30;

    // For EF Core only.
    private PmSchedule()
    {
    }

    public PmScheduleId Id { get; private set; }

    public override string AggregateId => Id.Value.ToString();

    // Snapshot of the asset (ADR-0002: no cross-schema join), same as on a work order.
    public Guid AssetId { get; private set; }

    public string AssetTag { get; private set; } = null!;

    public string AssetName { get; private set; } = null!;

    public string Title { get; private set; } = null!;

    public string Instructions { get; private set; } = null!;

    public int IntervalDays { get; private set; }

    public int LeadDays { get; private set; }

    public WorkOrderPriority Priority { get; private set; }

    /// <summary>The due date of the next occurrence to generate (a factory calendar date, not a UTC instant).</summary>
    public DateOnly NextDueOn { get; private set; }

    public bool IsActive { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public static PmSchedule Create(
        Guid assetId,
        string assetTag,
        string assetName,
        string title,
        string? instructions,
        int intervalDays,
        int leadDays,
        WorkOrderPriority priority,
        DateOnly nextDueOn)
    {
        var schedule = new PmSchedule
        {
            Id = PmScheduleId.New(),
            AssetId = assetId,
            AssetTag = TextRules.Required(assetTag, "Asset tag", WorkOrder.AssetTagMaxLength),
            AssetName = TextRules.Required(assetName, "Asset name", WorkOrder.AssetNameMaxLength),
            IsActive = true,
        };
        schedule.Apply(title, instructions, intervalDays, leadDays, priority, nextDueOn);

        schedule.Raise(new PmScheduleCreated(
            schedule.Id.Value,
            assetId,
            schedule.AssetTag,
            schedule.Title,
            schedule.IntervalDays,
            schedule.LeadDays,
            priority,
            nextDueOn));
        return schedule;
    }

    public void Update(
        string title,
        string? instructions,
        int intervalDays,
        int leadDays,
        WorkOrderPriority priority,
        DateOnly nextDueOn)
    {
        Apply(title, instructions, intervalDays, leadDays, priority, nextDueOn);
        Raise(new PmScheduleUpdated(Id.Value, Title, IntervalDays, LeadDays, Priority, NextDueOn));
    }

    public void Deactivate()
    {
        if (!IsActive)
        {
            throw new DomainException("The schedule is already inactive.");
        }

        IsActive = false;
        Raise(new PmScheduleDeactivated(Id.Value));
    }

    public void Activate()
    {
        if (IsActive)
        {
            throw new DomainException("The schedule is already active.");
        }

        IsActive = true;
        Raise(new PmScheduleActivated(Id.Value, NextDueOn));
    }

    /// <summary>True when the next occurrence is inside its lead window: <c>today &gt;= NextDueOn - LeadDays</c>.</summary>
    public bool IsDue(DateOnly today) => IsActive && today >= NextDueOn.AddDays(-LeadDays);

    /// <summary>
    /// Raises the preventive work order for <see cref="NextDueOn"/> if the schedule is due, and moves the schedule on.
    /// </summary>
    /// <remarks>
    /// Catch-up rule: if the job was down for three intervals, the schedule is three occurrences behind. It generates
    /// ONE work order, for the oldest due date (the work that was actually missed), and then advances until the next
    /// occurrence's lead window starts after <paramref name="today"/>. Three identical jobs for one machine help
    /// nobody, and "after the lead window" (not merely "after today") also means a second run on the same day finds
    /// nothing due, even without the unique index.
    /// </remarks>
    /// <param name="dueAtOf">Maps a due date to its deadline instant (end of that day, factory time); injected so the domain reads no clock or zone.</param>
    /// <returns>The new work order (not yet saved), or null when nothing is due.</returns>
    public WorkOrder? GenerateIfDue(DateOnly today, DateTimeOffset now, Func<DateOnly, DateTimeOffset> dueAtOf)
    {
        ArgumentNullException.ThrowIfNull(dueAtOf);
        if (!IsDue(today))
        {
            return null;
        }

        var dueOn = NextDueOn;
        var workOrder = WorkOrder.RaisePreventive(
            AssetId,
            AssetTag,
            AssetName,
            Title,
            Instructions,
            Priority,
            Id.Value,
            dueOn,
            SystemActor.Instance,
            now,
            dueAtOf(dueOn));

        AdvancePast(today);
        Raise(new PmWorkOrderGenerated(Id.Value, workOrder.Id.Value, dueOn, NextDueOn));
        return workOrder;
    }

    /// <summary>
    /// Moves on without generating, for an occurrence whose work order already exists (the unique index or a
    /// pre-check found it). Without this the schedule would stay due and the runner would find the same duplicate
    /// on every run.
    /// </summary>
    public void SkipAlreadyGenerated(DateOnly today)
    {
        if (!IsDue(today))
        {
            return;
        }

        var dueOn = NextDueOn;
        AdvancePast(today);
        Raise(new PmOccurrenceAlreadyGenerated(Id.Value, dueOn, NextDueOn));
    }

    private void AdvancePast(DateOnly today)
    {
        // At least one step (the occurrence just handled), then until the next lead window starts after today.
        // IntervalDays >= 1 guarantees termination.
        do
        {
            NextDueOn = NextDueOn.AddDays(IntervalDays);
        }
        while (today >= NextDueOn.AddDays(-LeadDays));
    }

    private void Apply(
        string title,
        string? instructions,
        int intervalDays,
        int leadDays,
        WorkOrderPriority priority,
        DateOnly nextDueOn)
    {
        var validTitle = TextRules.Required(title, "Title", TitleMaxLength);
        var validInstructions = TextRules.Optional(instructions, "Instructions", InstructionsMaxLength);
        if (intervalDays is < MinIntervalDays or > MaxIntervalDays)
        {
            throw new DomainException($"Interval must be between {MinIntervalDays} and {MaxIntervalDays} days.");
        }

        if (leadDays is < MinLeadDays or > MaxLeadDays)
        {
            throw new DomainException($"Lead time must be between {MinLeadDays} and {MaxLeadDays} days.");
        }

        if (!Enum.IsDefined(priority))
        {
            throw new DomainException($"'{priority}' is not a valid priority.");
        }

        // A missing JSON field deserializes to DateOnly.MinValue; that is never a real due date.
        if (nextDueOn == default)
        {
            throw new DomainException("Next due date is required.");
        }

        Title = validTitle;
        Instructions = validInstructions;
        IntervalDays = intervalDays;
        LeadDays = leadDays;
        Priority = priority;
        NextDueOn = nextDueOn;
    }
}

internal readonly record struct PmScheduleId(Guid Value)
{
    public static PmScheduleId New() => new(Guid.CreateVersion7());
}

/// <summary>The actor of background work. Matches the name the audit interceptor records when nobody is signed in.</summary>
internal static class SystemActor
{
    public static readonly Actor Instance = new("system", "System");
}
