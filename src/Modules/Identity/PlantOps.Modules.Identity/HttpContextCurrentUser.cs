using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using PlantOps.BuildingBlocks.Infrastructure;

namespace PlantOps.Modules.Identity;

// Reads the principal that authentication already built for this request. No HttpContext (a background job)
// or no signed-in user means Id is null, which the audit interceptor records as "System".
internal sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string? Id => Principal?.FindFirstValue(Claims.Subject);

    public string? Name => Principal?.FindFirstValue(Claims.Name);

    private ClaimsPrincipal? Principal =>
        accessor.HttpContext?.User is { Identity.IsAuthenticated: true } user ? user : null;
}
