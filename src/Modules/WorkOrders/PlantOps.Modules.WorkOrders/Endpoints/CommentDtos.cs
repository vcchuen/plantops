namespace PlantOps.Modules.WorkOrders.Endpoints;

internal sealed record CommentRequest(string Body);

internal sealed record ListCommentsQuery(int Skip = 0, int Take = 50);

internal sealed record WorkOrderCommentItem(
    Guid Id,
    int Sequence,
    PersonRef Author,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt);
