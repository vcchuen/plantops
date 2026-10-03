using PlantOps.SharedKernel;

namespace PlantOps.Modules.WorkOrders.Domain;

// Every transition takes the actor and the time, so the command shape is uniform. Those with a dedicated column
// store them there (ApprovedBy/At, StartedAt, ...); the rest (reject, assign, cancel) rely on the audit entry the
// interceptor writes for the raised event, which records the same actor and time.
internal sealed class WorkOrder : AggregateRoot
{
    public const int AssetTagMaxLength = 20;
    public const int AssetNameMaxLength = 100;
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 2000;
    public const int ResolutionMaxLength = 2000;
    public const int ReasonMaxLength = 500;
    public const int PersonIdMaxLength = 200;
    public const int PersonNameMaxLength = 200;

    // For EF Core only.
    private WorkOrder()
    {
    }

    public WorkOrderId Id { get; private set; }

    public override string AggregateId => Id.Value.ToString();

    /// <summary>From the database sequence at insert; 0 until the work order is first saved.</summary>
    public int Number { get; private set; }

    // Snapshot of the asset at the moment of reporting (ADR-0002: no cross-schema join, and a later rename must
    // not rewrite history).
    public Guid AssetId { get; private set; }

    public string AssetTag { get; private set; } = null!;

    public string AssetName { get; private set; } = null!;

    public string Title { get; private set; } = null!;

    public string Description { get; private set; } = null!;

    public WorkOrderPriority Priority { get; private set; }

    public bool AssetDown { get; private set; }

    public WorkOrderStatus Status { get; private set; }

    // People are an id plus a name snapshot, as scalar columns (EF Core 9 cannot index complex-type properties,
    // and AssignedToId is a list filter).
    public string ReportedById { get; private set; } = null!;

    public string ReportedByName { get; private set; } = null!;

    public string? ApprovedById { get; private set; }

    public string? ApprovedByName { get; private set; }

    public string? AssignedToId { get; private set; }

    public string? AssignedToName { get; private set; }

    public DateTimeOffset SubmittedAt { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public DateTimeOffset DueAt { get; private set; }

    public string? Resolution { get; private set; }

    public string? RejectionReason { get; private set; }

    public string? CancellationReason { get; private set; }

    public WorkOrderSource Source { get; private set; }

    /// <summary>
    /// When the SLA breach was noticed and people were told (a stored fact, unlike the computed SLA state). Null until
    /// then; once set it is never cleared, which is what makes escalation happen once.
    /// </summary>
    public DateTimeOffset? EscalatedAt { get; private set; }

    /// <summary>The schedule that planned this work; null for reactive work. With <see cref="PmDueOn"/> it is unique.</summary>
    public Guid? PmScheduleId { get; private set; }

    /// <summary>The occurrence (factory calendar date) this work order covers.</summary>
    public DateOnly? PmDueOn { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    /// <summary>Open = someone still owes the repair. Completed work is done (awaiting only sign-off), so it cannot breach.</summary>
    public static bool IsOpenStatus(WorkOrderStatus status) =>
        status is WorkOrderStatus.Submitted or WorkOrderStatus.Approved or WorkOrderStatus.Assigned or WorkOrderStatus.InProgress;

    /// <summary>Same set as <see cref="IsOpenStatus"/>, as an array so a list query can translate it to SQL IN.</summary>
    public static readonly WorkOrderStatus[] OpenStatuses =
        [WorkOrderStatus.Submitted, WorkOrderStatus.Approved, WorkOrderStatus.Assigned, WorkOrderStatus.InProgress];

    /// <summary>
    /// A planned job raised by a PM schedule. It starts Approved (a planned job needs no approval), is reported and
    /// approved by the system actor, and its deadline is the due date itself rather than a priority target.
    /// </summary>
    public static WorkOrder RaisePreventive(
        Guid assetId,
        string assetTag,
        string assetName,
        string title,
        string? instructions,
        WorkOrderPriority priority,
        Guid pmScheduleId,
        DateOnly dueOn,
        Actor system,
        DateTimeOffset now,
        DateTimeOffset dueAt)
    {
        ArgumentNullException.ThrowIfNull(system);
        EnsureDefined(priority);

        var workOrder = new WorkOrder
        {
            Id = WorkOrderId.New(),
            AssetId = assetId,
            AssetTag = TextRules.Required(assetTag, "Asset tag", AssetTagMaxLength),
            AssetName = TextRules.Required(assetName, "Asset name", AssetNameMaxLength),
            Title = TextRules.Required(title, "Title", TitleMaxLength),
            Description = TextRules.Optional(instructions, "Description", DescriptionMaxLength),
            Priority = priority,
            AssetDown = false,
            Status = WorkOrderStatus.Approved,
            ReportedById = system.Id,
            ReportedByName = system.Name,
            ApprovedById = system.Id,
            ApprovedByName = system.Name,
            SubmittedAt = now,
            ApprovedAt = now,
            DueAt = dueAt,
            Source = WorkOrderSource.Preventive,
            PmScheduleId = pmScheduleId,
            PmDueOn = dueOn,
        };

        // The same two facts a reactive order records, so the history reads the same for both.
        workOrder.Raise(new WorkOrderSubmitted(
            workOrder.Id.Value,
            assetId,
            workOrder.AssetTag,
            workOrder.Title,
            priority,
            AssetDown: false,
            dueAt));
        workOrder.Raise(new WorkOrderApproved(workOrder.Id.Value, priority, dueAt));
        return workOrder;
    }

    /// <summary>True when escalation would act now: open, past its deadline (strictly), and not yet escalated.</summary>
    public bool IsEscalatable(DateTimeOffset now) => EscalatedAt is null && IsOpenStatus(Status) && now > DueAt;

    /// <summary>
    /// Records that the SLA was breached and raises <see cref="WorkOrderSlaBreached"/>. Idempotent: an already
    /// escalated order is left alone and false is returned, so a second run of the job changes nothing.
    /// </summary>
    /// <returns>True when this call escalated the order; false when it already was.</returns>
    public bool Escalate(DateTimeOffset now)
    {
        if (EscalatedAt is not null)
        {
            return false;
        }

        if (!IsOpenStatus(Status))
        {
            throw new DomainException($"Cannot escalate a work order that is {Status}; it must be open.");
        }

        if (now <= DueAt)
        {
            throw new DomainException("Cannot escalate a work order that is not past its deadline.");
        }

        EscalatedAt = now;
        Raise(new WorkOrderSlaBreached(
            Id.Value,
            FormatNumber(Number),
            Title,
            AssetTag,
            Priority,
            DueAt,
            now,
            AssignedToName));
        return true;
    }

    /// <param name="now">Passed in so the aggregate never reads a clock.</param>
    public static WorkOrder Submit(
        Guid assetId,
        string assetTag,
        string assetName,
        string title,
        string? description,
        WorkOrderPriority priority,
        bool assetDown,
        Actor reporter,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(reporter);
        EnsureDefined(priority);

        var workOrder = new WorkOrder
        {
            Id = WorkOrderId.New(),
            AssetId = assetId,
            AssetTag = TextRules.Required(assetTag, "Asset tag", AssetTagMaxLength),
            AssetName = TextRules.Required(assetName, "Asset name", AssetNameMaxLength),
            Title = TextRules.Required(title, "Title", TitleMaxLength),
            Description = TextRules.Optional(description, "Description", DescriptionMaxLength),
            Priority = priority,
            AssetDown = assetDown,
            Status = WorkOrderStatus.Submitted,
            ReportedById = reporter.Id,
            ReportedByName = reporter.Name,
            SubmittedAt = now,
        };
        workOrder.DueAt = SlaPolicy.For(priority).DueAt(now);

        workOrder.Raise(new WorkOrderSubmitted(
            workOrder.Id.Value,
            assetId,
            workOrder.AssetTag,
            workOrder.Title,
            priority,
            assetDown,
            workOrder.DueAt));
        return workOrder;
    }

    /// <param name="priority">A supervisor may re-triage; the deadline is recomputed from the original report time.</param>
    public void Approve(Actor actor, DateTimeOffset now, WorkOrderPriority? priority = null)
    {
        ArgumentNullException.ThrowIfNull(actor);
        EnsureStatus("approve", WorkOrderStatus.Submitted);

        if (priority is { } newPriority)
        {
            EnsureDefined(newPriority);
            Priority = newPriority;
            DueAt = SlaPolicy.For(Priority).DueAt(SubmittedAt);
        }

        Status = WorkOrderStatus.Approved;
        ApprovedById = actor.Id;
        ApprovedByName = actor.Name;
        ApprovedAt = now;
        Raise(new WorkOrderApproved(Id.Value, Priority, DueAt));
    }

    public void Reject(Actor actor, DateTimeOffset now, string reason)
    {
        ArgumentNullException.ThrowIfNull(actor);
        EnsureStatus("reject", WorkOrderStatus.Submitted);
        var trimmed = TextRules.Required(reason, "Rejection reason", ReasonMaxLength);

        Status = WorkOrderStatus.Rejected;
        RejectionReason = trimmed;
        Raise(new WorkOrderRejected(Id.Value, trimmed));
    }

    /// <summary>Assigns, or reassigns while still <see cref="WorkOrderStatus.Assigned"/>.</summary>
    public void Assign(Actor actor, Actor technician, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(technician);
        EnsureStatus("assign", WorkOrderStatus.Approved, WorkOrderStatus.Assigned);

        var previous = AssignedToId;
        Status = WorkOrderStatus.Assigned;
        AssignedToId = technician.Id;
        AssignedToName = technician.Name;
        Raise(new WorkOrderAssigned(Id.Value, technician.Id, technician.Name, previous));
    }

    public void Start(Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        EnsureStatus("start", WorkOrderStatus.Assigned);

        Status = WorkOrderStatus.InProgress;
        StartedAt = now;
        Raise(new WorkOrderStarted(Id.Value));
    }

    public void Complete(Actor actor, DateTimeOffset now, string resolution)
    {
        ArgumentNullException.ThrowIfNull(actor);
        EnsureStatus("complete", WorkOrderStatus.InProgress);
        var trimmed = TextRules.Required(resolution, "Resolution", ResolutionMaxLength);

        Status = WorkOrderStatus.Completed;
        CompletedAt = now;
        Resolution = trimmed;
        Raise(new WorkOrderCompleted(
            Id.Value,
            trimmed,
            FormatNumber(Number),
            AssetId,
            Title,
            SubmittedAt,
            StartedAt!.Value, // InProgress implies Start() ran
            now,
            // Whoever completes it, the job belongs to the assigned technician (an admin may complete on their behalf).
            AssignedToName ?? actor.Name,
            AssetDown));
    }

    public void Close(Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        EnsureStatus("close", WorkOrderStatus.Completed);

        Status = WorkOrderStatus.Closed;
        ClosedAt = now;
        Raise(new WorkOrderClosed(Id.Value));
    }

    public void Cancel(Actor actor, DateTimeOffset now, string reason)
    {
        ArgumentNullException.ThrowIfNull(actor);
        EnsureStatus("cancel", WorkOrderStatus.Submitted, WorkOrderStatus.Approved, WorkOrderStatus.Assigned);
        var trimmed = TextRules.Required(reason, "Cancellation reason", ReasonMaxLength);

        Status = WorkOrderStatus.Cancelled;
        CancellationReason = trimmed;
        Raise(new WorkOrderCancelled(Id.Value, FormatNumber(Number), trimmed, now));
    }

    public static string FormatNumber(int number) => $"WO-{number:D6}";

    /// <summary>
    /// Null for rejected and cancelled orders: no repair was ever owed, so there is no deadline to meet.
    /// Computed, never stored, because the answer changes with the clock.
    /// </summary>
    public SlaState? SlaStateAt(DateTimeOffset now) => ComputeSlaState(Status, Priority, DueAt, CompletedAt, now);

    /// <summary>
    /// Static so list queries can evaluate it on projected columns without loading aggregates. Judged against the
    /// stored <paramref name="dueAt"/> (not submittedAt + target) because a preventive order's deadline is its due
    /// date; for reactive orders the two are identical (Submit and a re-triaging Approve both store target-based dueAt).
    /// </summary>
    public static SlaState? ComputeSlaState(
        WorkOrderStatus status,
        WorkOrderPriority priority,
        DateTimeOffset dueAt,
        DateTimeOffset? completedAt,
        DateTimeOffset now) =>
        status is WorkOrderStatus.Rejected or WorkOrderStatus.Cancelled
            ? null
            : SlaPolicy.For(priority).EvaluateAgainst(dueAt, completedAt, now);

    private void EnsureStatus(string action, params WorkOrderStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new DomainException(
                $"Cannot {action} a work order that is {Status}; it must be {string.Join(" or ", allowed)}.");
        }
    }

    private static void EnsureDefined(WorkOrderPriority priority)
    {
        if (!Enum.IsDefined(priority))
        {
            throw new DomainException($"'{priority}' is not a valid priority.");
        }
    }
}
