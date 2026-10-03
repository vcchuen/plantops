using PlantOps.Modules.WorkOrders.Domain;
using PlantOps.SharedKernel;
using static PlantOps.Modules.WorkOrders.Tests.Domain.WorkOrderBuilder;

namespace PlantOps.Modules.WorkOrders.Tests.Domain;

public class WorkOrderTests
{
    // Which statuses each action accepts: the transition table from the design doc, written down once.
    private static readonly Dictionary<string, WorkOrderStatus[]> Legal = new()
    {
        ["approve"] = [WorkOrderStatus.Submitted],
        ["reject"] = [WorkOrderStatus.Submitted],
        ["assign"] = [WorkOrderStatus.Approved, WorkOrderStatus.Assigned],
        ["start"] = [WorkOrderStatus.Assigned],
        ["complete"] = [WorkOrderStatus.InProgress],
        ["close"] = [WorkOrderStatus.Completed],
        ["cancel"] = [WorkOrderStatus.Submitted, WorkOrderStatus.Approved, WorkOrderStatus.Assigned],
    };

    private static readonly DateTimeOffset Later = T0.AddHours(1);

    private static void Run(WorkOrder w, string action)
    {
        switch (action)
        {
            case "approve": w.Approve(Sam, Later); break;
            case "reject": w.Reject(Sam, Later, "No fault found"); break;
            case "assign": w.Assign(Sam, Lee, Later); break;
            case "start": w.Start(Tom, Later); break;
            case "complete": w.Complete(Tom, Later, "Fixed"); break;
            case "close": w.Close(Sam, Later); break;
            case "cancel": w.Cancel(Sam, Later, "Not needed"); break;
            default: throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    public static TheoryData<string, string> IllegalTransitions()
    {
        var data = new TheoryData<string, string>();
        foreach (var status in Enum.GetValues<WorkOrderStatus>())
        {
            foreach (var (action, allowed) in Legal)
            {
                if (!allowed.Contains(status))
                {
                    data.Add(status.ToString(), action);
                }
            }
        }

        return data;
    }

    public static TheoryData<string, string> LegalTransitions()
    {
        var data = new TheoryData<string, string>();
        foreach (var (action, allowed) in Legal)
        {
            foreach (var status in allowed)
            {
                data.Add(status.ToString(), action);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(IllegalTransitions))]
    public void Illegal_transition_throws_naming_the_action_and_status_and_changes_nothing(string statusName, string action)
    {
        var status = Enum.Parse<WorkOrderStatus>(statusName);
        var w = InStatus(status);

        var ex = Assert.Throws<DomainException>(() => Run(w, action));

        Assert.Contains($"Cannot {action}", ex.Message);
        Assert.Contains(status.ToString(), ex.Message);
        Assert.Equal(status, w.Status);
        Assert.Empty(w.DomainEvents);
    }

    [Theory]
    [MemberData(nameof(LegalTransitions))]
    public void Legal_transition_raises_exactly_one_event(string statusName, string action)
    {
        var status = Enum.Parse<WorkOrderStatus>(statusName);
        var w = InStatus(status);

        Run(w, action);

        Assert.Single(w.DomainEvents);
    }

    [Fact]
    public void Every_status_is_covered_by_the_transition_table()
    {
        // Guards the theories above: a new status that nobody added to Legal would silently pass as "all illegal".
        var terminal = new[] { WorkOrderStatus.Closed, WorkOrderStatus.Rejected, WorkOrderStatus.Cancelled };
        var accountedFor = Legal.Values.SelectMany(s => s).Concat(terminal).ToHashSet();

        Assert.Equal(Enum.GetValues<WorkOrderStatus>().Order(), accountedFor.Order());
    }

    [Fact]
    public void Submit_snapshots_the_asset_and_reporter_and_starts_the_sla_clock()
    {
        var assetId = Guid.NewGuid();

        var w = WorkOrder.Submit(assetId, "SMT1-PNP-01", "Pick and place", "  Feeder jam ", null, WorkOrderPriority.P1, true, Oscar, T0);

        Assert.NotEqual(default, w.Id);
        Assert.Equal(WorkOrderStatus.Submitted, w.Status);
        Assert.Equal(assetId, w.AssetId);
        Assert.Equal("SMT1-PNP-01", w.AssetTag);
        Assert.Equal("Pick and place", w.AssetName);
        Assert.Equal("Feeder jam", w.Title);
        Assert.Equal(string.Empty, w.Description);
        Assert.Equal("oscar", w.ReportedById);
        Assert.Equal("Oscar", w.ReportedByName);
        Assert.Equal(T0, w.SubmittedAt);
        Assert.Equal(T0.AddHours(4), w.DueAt);
        var raised = Assert.IsType<WorkOrderSubmitted>(Assert.Single(w.DomainEvents));
        Assert.Equal(w.Id.Value, raised.WorkOrderId);
        Assert.Equal(WorkOrderPriority.P1, raised.Priority);
        Assert.Equal(w.DueAt, raised.DueAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Submit_requires_a_title(string? title)
    {
        Assert.Throws<DomainException>(() =>
            WorkOrder.Submit(Guid.NewGuid(), "T-1", "Name", title!, null, WorkOrderPriority.P3, false, Oscar, T0));
    }

    [Fact]
    public void Submit_rejects_an_undefined_priority()
    {
        Assert.Throws<DomainException>(() =>
            WorkOrder.Submit(Guid.NewGuid(), "T-1", "Name", "Title", null, (WorkOrderPriority)99, false, Oscar, T0));
    }

    [Fact]
    public void Approve_records_who_and_when()
    {
        var w = InStatus(WorkOrderStatus.Submitted);

        w.Approve(Sam, Later);

        Assert.Equal(WorkOrderStatus.Approved, w.Status);
        Assert.Equal("sam", w.ApprovedById);
        Assert.Equal("Sam", w.ApprovedByName);
        Assert.Equal(Later, w.ApprovedAt);
        Assert.IsType<WorkOrderApproved>(Assert.Single(w.DomainEvents));
    }

    [Fact]
    public void Approve_with_a_new_priority_recomputes_the_deadline_from_the_report_time()
    {
        var w = InStatus(WorkOrderStatus.Submitted, WorkOrderPriority.P4);

        w.Approve(Sam, Later, WorkOrderPriority.P1);

        Assert.Equal(WorkOrderPriority.P1, w.Priority);
        Assert.Equal(T0.AddHours(4), w.DueAt);
        var raised = Assert.IsType<WorkOrderApproved>(Assert.Single(w.DomainEvents));
        Assert.Equal(WorkOrderPriority.P1, raised.Priority);
        Assert.Equal(T0.AddHours(4), raised.DueAt);
    }

    [Fact]
    public void Approve_without_a_priority_keeps_the_deadline()
    {
        var w = InStatus(WorkOrderStatus.Submitted, WorkOrderPriority.P3);

        w.Approve(Sam, Later);

        Assert.Equal(WorkOrderPriority.P3, w.Priority);
        Assert.Equal(T0.AddHours(24), w.DueAt);
    }

    [Fact]
    public void Reject_stores_the_reason_and_is_terminal()
    {
        var w = InStatus(WorkOrderStatus.Submitted);

        w.Reject(Sam, Later, "  Duplicate ");

        Assert.Equal(WorkOrderStatus.Rejected, w.Status);
        Assert.Equal("Duplicate", w.RejectionReason);
        var raised = Assert.IsType<WorkOrderRejected>(Assert.Single(w.DomainEvents));
        Assert.Equal("Duplicate", raised.Reason);
        Assert.Throws<DomainException>(() => w.Approve(Sam, Later));
    }

    [Fact]
    public void Reject_requires_a_reason()
    {
        var w = InStatus(WorkOrderStatus.Submitted);

        Assert.Throws<DomainException>(() => w.Reject(Sam, Later, " "));
        Assert.Equal(WorkOrderStatus.Submitted, w.Status);
        Assert.Empty(w.DomainEvents);
    }

    [Fact]
    public void Cancel_requires_a_reason()
    {
        var w = InStatus(WorkOrderStatus.Submitted);

        Assert.Throws<DomainException>(() => w.Cancel(Sam, Later, " "));
        Assert.Equal(WorkOrderStatus.Submitted, w.Status);
        Assert.Empty(w.DomainEvents);
    }

    [Fact]
    public void Assign_sets_the_technician_snapshot()
    {
        var w = InStatus(WorkOrderStatus.Approved);

        w.Assign(Sam, Tom, Later);

        Assert.Equal(WorkOrderStatus.Assigned, w.Status);
        Assert.Equal("tom", w.AssignedToId);
        Assert.Equal("Tom", w.AssignedToName);
        var raised = Assert.IsType<WorkOrderAssigned>(Assert.Single(w.DomainEvents));
        Assert.Equal("tom", raised.TechnicianId);
        Assert.Null(raised.PreviousTechnicianId);
    }

    [Fact]
    public void Reassign_replaces_the_technician_and_the_event_names_the_previous_one()
    {
        var w = InStatus(WorkOrderStatus.Assigned);

        w.Assign(Sam, Lee, Later);

        Assert.Equal(WorkOrderStatus.Assigned, w.Status);
        Assert.Equal("lee", w.AssignedToId);
        var raised = Assert.IsType<WorkOrderAssigned>(Assert.Single(w.DomainEvents));
        Assert.Equal("tom", raised.PreviousTechnicianId);
    }

    [Fact]
    public void Start_records_the_start_time()
    {
        var w = InStatus(WorkOrderStatus.Assigned);

        w.Start(Tom, Later);

        Assert.Equal(WorkOrderStatus.InProgress, w.Status);
        Assert.Equal(Later, w.StartedAt);
        Assert.IsType<WorkOrderStarted>(Assert.Single(w.DomainEvents));
    }

    [Fact]
    public void Complete_records_resolution_and_time_and_requires_a_resolution()
    {
        var w = InStatus(WorkOrderStatus.InProgress);

        Assert.Throws<DomainException>(() => w.Complete(Tom, Later, " "));
        w.Complete(Tom, Later, " Replaced spring ");

        Assert.Equal(WorkOrderStatus.Completed, w.Status);
        Assert.Equal("Replaced spring", w.Resolution);
        Assert.Equal(Later, w.CompletedAt);
        Assert.IsType<WorkOrderCompleted>(Assert.Single(w.DomainEvents));
    }

    [Fact]
    public void Close_records_the_close_time_and_is_terminal()
    {
        var w = InStatus(WorkOrderStatus.Completed);

        w.Close(Sam, Later);

        Assert.Equal(WorkOrderStatus.Closed, w.Status);
        Assert.Equal(Later, w.ClosedAt);
        Assert.IsType<WorkOrderClosed>(Assert.Single(w.DomainEvents));
        Assert.Throws<DomainException>(() => w.Cancel(Sam, Later, "too late"));
    }

    [Theory]
    [InlineData("Submitted")]
    [InlineData("Approved")]
    [InlineData("Assigned")]
    public void Cancel_stores_the_reason_from_any_pre_work_status(string statusName)
    {
        var status = Enum.Parse<WorkOrderStatus>(statusName);
        var w = InStatus(status);

        w.Cancel(Sam, Later, "Machine scrapped");

        Assert.Equal(WorkOrderStatus.Cancelled, w.Status);
        Assert.Equal("Machine scrapped", w.CancellationReason);
        var raised = Assert.IsType<WorkOrderCancelled>(Assert.Single(w.DomainEvents));
        Assert.Equal("Machine scrapped", raised.Reason);
    }

    [Fact]
    public void Full_lifecycle_raises_one_event_per_step_in_order()
    {
        var w = Submitted();

        w.Approve(Sam, Later);
        w.Assign(Sam, Tom, Later);
        w.Start(Tom, Later);
        w.Complete(Tom, Later, "Fixed");
        w.Close(Sam, Later);

        Assert.Equal(
            [typeof(WorkOrderSubmitted), typeof(WorkOrderApproved), typeof(WorkOrderAssigned), typeof(WorkOrderStarted), typeof(WorkOrderCompleted), typeof(WorkOrderClosed)],
            w.DomainEvents.Select(e => e.GetType()).ToArray());
    }

    [Fact]
    public void Clear_domain_events_empties_the_pending_list()
    {
        var w = Submitted();

        w.ClearDomainEvents();

        Assert.Empty(w.DomainEvents);
    }

    [Theory]
    [InlineData("Rejected")]
    [InlineData("Cancelled")]
    public void Sla_state_does_not_apply_to_rejected_or_cancelled_orders(string statusName)
    {
        Assert.Null(InStatus(Enum.Parse<WorkOrderStatus>(statusName)).SlaStateAt(T0.AddDays(30)));
    }

    [Fact]
    public void Sla_state_uses_the_completion_time_once_the_work_is_done()
    {
        var w = InStatus(WorkOrderStatus.Completed, WorkOrderPriority.P1);

        // Completed 60 minutes after the report against a 4 h target: met, however late we look.
        Assert.Equal(SlaState.Met, w.SlaStateAt(T0.AddDays(30)));
    }

    [Fact]
    public void Number_is_formatted_with_six_digits()
    {
        Assert.Equal("WO-000123", WorkOrder.FormatNumber(123));
    }

    [Fact]
    public void Aggregate_id_is_the_guid_text()
    {
        var w = Submitted();

        Assert.Equal(w.Id.Value.ToString(), w.AggregateId);
    }
}
