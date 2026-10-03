using PlantOps.SharedKernel;

namespace PlantOps.Modules.WorkOrders.Domain;

// A note on a work order. Not an aggregate: it has no lifecycle of its own and raises no events, so it does not
// go through the audit interceptor.
internal sealed class WorkOrderComment
{
    public const int BodyMaxLength = 2000;
    public const int PersonIdMaxLength = 200;
    public const int PersonNameMaxLength = 200;

    // For EF Core only.
    private WorkOrderComment()
    {
    }

    public WorkOrderComment(WorkOrderId workOrderId, int sequence, Actor author, string? body, DateTimeOffset now)
    {
        if (sequence < 1)
        {
            throw new DomainException("Comment sequence starts at 1.");
        }

        Id = Guid.CreateVersion7();
        WorkOrderId = workOrderId;
        Sequence = sequence;
        AuthorId = author.Id;
        AuthorName = author.Name;
        Body = TextRules.Required(body, "Comment", BodyMaxLength);
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public WorkOrderId WorkOrderId { get; private set; }

    /// <summary>1-based position within the work order; the order the comments are shown in.</summary>
    public int Sequence { get; private set; }

    public string AuthorId { get; private set; } = null!;

    // Snapshot, like the people on the work order itself.
    public string AuthorName { get; private set; } = null!;

    public string Body { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? EditedAt { get; private set; }

    public void Edit(string? body, DateTimeOffset now)
    {
        Body = TextRules.Required(body, "Comment", BodyMaxLength);
        EditedAt = now;
    }
}
