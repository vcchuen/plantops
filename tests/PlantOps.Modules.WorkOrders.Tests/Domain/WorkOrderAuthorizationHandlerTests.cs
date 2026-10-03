using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using PlantOps.Modules.Identity.Contracts;
using PlantOps.Modules.WorkOrders.Authorization;
using PlantOps.Modules.WorkOrders.Domain;
using static PlantOps.Modules.WorkOrders.Tests.Domain.WorkOrderBuilder;

namespace PlantOps.Modules.WorkOrders.Tests.Domain;

public class WorkOrderAuthorizationHandlerTests
{
    private static ClaimsPrincipal User(string id, params string[] roles)
    {
        var identity = new ClaimsIdentity("test", "name", "roles");
        identity.AddClaim(new Claim("sub", id));
        foreach (var role in roles)
        {
            identity.AddClaim(new Claim("roles", role));
        }

        return new ClaimsPrincipal(identity);
    }

    private static async Task<bool> Authorize(ClaimsPrincipal user, WorkOrder resource, IAuthorizationRequirement requirement)
    {
        var context = new AuthorizationHandlerContext([requirement], user, resource);
        await new WorkOrderAuthorizationHandler().HandleAsync(context);
        return context.HasSucceeded;
    }

    private static WorkOrder AssignedToTom() => InStatus(WorkOrderStatus.Assigned);

    [Fact]
    public async Task Assigned_technician_may_work()
    {
        Assert.True(await Authorize(User("tom", Roles.Technician), AssignedToTom(), WorkOrderOperations.Work));
    }

    [Fact]
    public async Task Another_technician_may_not_work()
    {
        Assert.False(await Authorize(User("lee", Roles.Technician), AssignedToTom(), WorkOrderOperations.Work));
    }

    [Fact]
    public async Task Admin_may_work_on_anyones_order()
    {
        Assert.True(await Authorize(User("root", Roles.Admin), AssignedToTom(), WorkOrderOperations.Work));
    }

    [Fact]
    public async Task Supervisor_who_is_not_the_assignee_may_not_work()
    {
        Assert.False(await Authorize(User("sam", Roles.Supervisor), AssignedToTom(), WorkOrderOperations.Work));
    }

    [Fact]
    public async Task Nobody_but_an_admin_may_work_on_an_unassigned_order()
    {
        var unassigned = InStatus(WorkOrderStatus.Approved);

        Assert.False(await Authorize(User("tom", Roles.Technician), unassigned, WorkOrderOperations.Work));
    }

    [Fact]
    public async Task A_user_without_a_subject_claim_may_not_work()
    {
        var anonymousSubject = new ClaimsPrincipal(new ClaimsIdentity("test", "name", "roles"));

        Assert.False(await Authorize(anonymousSubject, AssignedToTom(), WorkOrderOperations.Work));
    }

    [Fact]
    public async Task Other_operations_are_not_handled()
    {
        var other = new OperationAuthorizationRequirement { Name = "Delete" };

        Assert.False(await Authorize(User("root", Roles.Admin), AssignedToTom(), other));
    }
}
