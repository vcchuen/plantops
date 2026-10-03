using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PlantOps.BuildingBlocks.Infrastructure;

namespace PlantOps.Modules.Identity;

// Defence in depth on top of SameSite=Lax (design 03, Decision 3). A cross-site page cannot add a custom header
// without a CORS preflight, and this API has no CORS policy, so the preflight fails and the forged request is
// never sent. Runs before authentication: a forged request is rejected without even decrypting the cookie.
internal sealed class CsrfGuardMiddleware(RequestDelegate next, ILoggerFactory loggers)
{
    public const string HeaderName = "X-CSRF";

    private static readonly HashSet<string> SafeMethods =
        [HttpMethods.Get, HttpMethods.Head, HttpMethods.Options, HttpMethods.Trace];

    private readonly ILogger _logger = loggers.CreateLogger(SecurityEvents.Category);

    public Task InvokeAsync(HttpContext context)
    {
        var isApi = context.Request.Path.StartsWithSegments("/api");
        if (isApi && !SafeMethods.Contains(context.Request.Method) && !context.Request.Headers.ContainsKey(HeaderName))
        {
            SecurityEvents.CsrfRejected(_logger, context.Request.Path);
            return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Bad request",
                    detail: $"Missing {HeaderName} header.")
                .ExecuteAsync(context);
        }

        return next(context);
    }
}
