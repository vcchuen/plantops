using PlantOps.Modules.WorkOrders.Domain;
using PlantOps.SharedKernel;
using static PlantOps.Modules.WorkOrders.Tests.Domain.WorkOrderBuilder;

namespace PlantOps.Modules.WorkOrders.Tests.Domain;

public class WorkOrderCommentTests
{
    private static WorkOrderComment NewComment(string? body = "Belt replaced, waiting for a test run.", int sequence = 1) =>
        new(WorkOrderId.New(), sequence, Tom, body, T0);

    [Fact]
    public void A_new_comment_snapshots_the_author_and_has_not_been_edited()
    {
        var workOrderId = WorkOrderId.New();

        var comment = new WorkOrderComment(workOrderId, 3, Tom, "Ordered a spare feeder.", T0);

        Assert.NotEqual(Guid.Empty, comment.Id);
        Assert.Equal(workOrderId, comment.WorkOrderId);
        Assert.Equal(3, comment.Sequence);
        Assert.Equal("tom", comment.AuthorId);
        Assert.Equal("Tom", comment.AuthorName);
        Assert.Equal("Ordered a spare feeder.", comment.Body);
        Assert.Equal(T0, comment.CreatedAt);
        Assert.Null(comment.EditedAt);
    }

    [Fact]
    public void The_body_is_trimmed()
    {
        Assert.Equal("Checked the belt.", NewComment("  Checked the belt.  ").Body);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_body_is_rejected(string? body)
    {
        var error = Assert.Throws<DomainException>(() => NewComment(body));

        Assert.Contains("required", error.Message);
    }

    [Fact]
    public void The_body_may_be_exactly_2000_characters_but_not_more()
    {
        Assert.Equal(2000, NewComment(new string('x', 2000)).Body.Length);

        Assert.Throws<DomainException>(() => NewComment(new string('x', 2001)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void The_sequence_starts_at_one(int sequence)
    {
        Assert.Throws<DomainException>(() => NewComment(sequence: sequence));
    }

    [Fact]
    public void Editing_replaces_the_body_and_stamps_the_edit_time()
    {
        var comment = NewComment();
        var later = T0.AddMinutes(15);

        comment.Edit("Belt replaced and tested.", later);

        Assert.Equal("Belt replaced and tested.", comment.Body);
        Assert.Equal(later, comment.EditedAt);
        Assert.Equal(T0, comment.CreatedAt);
    }

    [Fact]
    public void An_invalid_edit_changes_nothing()
    {
        var comment = NewComment();

        Assert.Throws<DomainException>(() => comment.Edit(" ", T0.AddMinutes(15)));

        Assert.Equal("Belt replaced, waiting for a test run.", comment.Body);
        Assert.Null(comment.EditedAt);
    }
}
