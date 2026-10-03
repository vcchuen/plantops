using PlantOps.BuildingBlocks.Infrastructure;

namespace PlantOps.Functions;

// No signed-in user outside an HTTP request. Null ids make DomainEventInterceptor record the actor as "System".
internal sealed class NoCurrentUser : ICurrentUser
{
    public string? Id => null;

    public string? Name => null;
}
