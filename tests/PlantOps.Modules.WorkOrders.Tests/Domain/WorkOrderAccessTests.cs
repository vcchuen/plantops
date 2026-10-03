using PlantOps.Modules.WorkOrders.Authorization;
using PlantOps.Modules.WorkOrders.Domain;

namespace PlantOps.Modules.WorkOrders.Tests.Domain;

public class WorkOrderAccessTests
{
    private static string[] Actions(WorkOrderStatus status, bool supervise, bool work) =>
        [.. WorkOrderAccess.AllowedActions(status, supervise, work)];

    // status, canSupervise, canWork -> expected actions (space separated, in display order)
    [Theory]
    [InlineData("Submitted", true, false, "approve reject cancel")]
    [InlineData("Submitted", false, false, "")]
    [InlineData("Submitted", false, true, "")]
    [InlineData("Approved", true, false, "assign cancel")]
    [InlineData("Approved", false, false, "")]
    [InlineData("Assigned", true, false, "assign cancel")]
    [InlineData("Assigned", false, true, "start")]
    [InlineData("Assigned", true, true, "start assign cancel")]
    [InlineData("Assigned", false, false, "")]
    [InlineData("InProgress", false, true, "complete")]
    [InlineData("InProgress", true, true, "complete")]
    [InlineData("InProgress", true, false, "")]
    [InlineData("InProgress", false, false, "")]
    [InlineData("Completed", true, false, "close")]
    [InlineData("Completed", false, true, "")]
    [InlineData("Closed", true, true, "")]
    [InlineData("Rejected", true, true, "")]
    [InlineData("Cancelled", true, true, "")]
    public void Allowed_actions_follow_status_role_and_resource_rule(string statusName, bool supervise, bool work, string expected)
    {
        var status = Enum.Parse<WorkOrderStatus>(statusName);
        var expectedActions = expected.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(expectedActions, Actions(status, supervise, work));
    }

    [Fact]
    public void Matrix_covers_every_status()
    {
        // A new status must be added to the matrix above; with no supervisor and no worker nothing is ever allowed.
        foreach (var status in Enum.GetValues<WorkOrderStatus>())
        {
            Assert.Empty(Actions(status, supervise: false, work: false));
        }
    }

    [Theory]
    [InlineData("tom", false, "tom", true)]
    [InlineData("lee", false, "tom", false)]
    [InlineData("lee", true, "tom", true)]
    [InlineData("lee", true, null, true)]
    [InlineData("tom", false, null, false)]
    [InlineData(null, false, null, false)]
    [InlineData("", false, "", false)]
    public void Can_work_is_the_assignee_or_an_admin(string? userId, bool isAdmin, string? assignedToId, bool expected)
    {
        Assert.Equal(expected, WorkOrderAccess.CanWork(userId, isAdmin, assignedToId));
    }
}
