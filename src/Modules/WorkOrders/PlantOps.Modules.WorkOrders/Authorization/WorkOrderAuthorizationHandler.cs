using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using PlantOps.Modules.Identity.Contracts;
using PlantOps.Modules.WorkOrders.Domain;

namespace PlantOps.Modules.WorkOrders.Authorization;

internal static class WorkOrderOperations
{
    /// <summary>Start or complete the work: assigned technician or admin.</summary>
    public static readonly OperationAuthorizationRequirement Work = new() { Name = nameof(Work) };
}

// Resource-based authorization: the decision needs the loaded work order, which a role policy cannot see.
internal sealed class WorkOrderAuthorizationHandler : AuthorizationHandler<OperationAuthorizationRequirement, WorkOrder>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OperationAuthorizationRequirement requirement,
        WorkOrder resource)
    {
        if (requirement == WorkOrderOperations.Work
            && WorkOrderAccess.CanWork(context.User.FindFirst("sub")?.Value, context.User.IsInRole(Roles.Admin), resource.AssignedToId))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
