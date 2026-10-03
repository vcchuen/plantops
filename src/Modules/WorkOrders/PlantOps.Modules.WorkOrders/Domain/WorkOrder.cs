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

    public byte[] RowVersion { get; private set; } = [];

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
    public SlaState? SlaStateAt(DateTimeOffset now) => ComputeSlaState(Status, Priority, SubmittedAt, CompletedAt, now);

    /// <summary>Static so list queries can evaluate it on projected columns without loading aggregates.</summary>
    public static SlaState? ComputeSlaState(
        WorkOrderStatus status,
        WorkOrderPriority priority,
        DateTimeOffset submittedAt,
        DateTimeOffset? completedAt,
        DateTimeOffset now) =>
        status is WorkOrderStatus.Rejected or WorkOrderStatus.Cancelled
            ? null
            : SlaPolicy.For(priority).Evaluate(submittedAt, completedAt, now);

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
