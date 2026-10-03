using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using PlantOps.BuildingBlocks.Infrastructure;

namespace PlantOps.Api.Security;

// Bound from "RateLimiting". Everything is configurable so tests and the E2E compose stack can raise (or shrink) the
// limits; the defaults are the production values from design 08.
internal sealed class RateLimitSettings
{
    public const string Section = "RateLimiting";

    public LoginSettings Login { get; set; } = new();

    public ApiSettings Api { get; set; } = new();

    internal sealed class LoginSettings
    {
        public int PermitLimit { get; set; } = 10;

        public int WindowSeconds { get; set; } = 60;
    }

    internal sealed class ApiSettings
    {
        public int TokenLimit { get; set; } = 100;

        public int TokensPerPeriod { get; set; } = 100;

        public int ReplenishmentSeconds { get; set; } = 10;
    }
}

internal static class RateLimiting
{
    public const string LoginPolicy = "login";
    public const string ApiPolicy = "api";

    private const string LoginPath = "/api/identity/login";

    // Carries the policy name from the partition selector to OnRejected: the rate limiter reports a rejection without
    // saying which limiter said no, and the log event needs it.
    private const string PolicyItem = "plantops.ratelimit.policy";
    private const string PartitionItem = "plantops.ratelimit.partition";

    public static IServiceCollection AddPlantOpsRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<RateLimitSettings>().BindConfiguration(RateLimitSettings.Section);

        // Configured through IOptions (resolved at first request) so settings added after service registration, such as
        // WebApplicationFactory's UseSetting, are honoured.
        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<RateLimitSettings>, ILoggerFactory>(Configure);
        return services;
    }

    private static void Configure(RateLimiterOptions options, IOptions<RateLimitSettings> settings, ILoggerFactory loggers)
    {
        var logger = loggers.CreateLogger(SecurityEvents.Category);
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        // ONE global limiter that picks the policy by path, instead of a named policy per endpoint plus a global one:
        // a login request is then counted against "login" only (the stricter rule), never twice, and the middleware
        // never throws for a policy name that a host forgot to register.
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            var path = context.Request.Path;
            if (!path.StartsWithSegments("/api"))
            {
                // Static files, the SPA and /health/* are not rate limited (health probes must never be refused).
                return RateLimitPartition.GetNoLimiter("none");
            }

            var value = settings.Value;
            if (path.Equals(LoginPath, StringComparison.OrdinalIgnoreCase))
            {
                // Per client IP: nobody is signed in yet. The IP is the real client's only because ForwardedHeaders
                // runs first (see Program.cs).
                var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                context.Items[PolicyItem] = LoginPolicy;
                context.Items[PartitionItem] = "ip";
                return RateLimitPartition.GetFixedWindowLimiter($"{LoginPolicy}:{ip}", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = Math.Max(1, value.Login.PermitLimit),
                    Window = TimeSpan.FromSeconds(Math.Max(1, value.Login.WindowSeconds)),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
            }

            // Per user when signed in (so one noisy user cannot starve the others and a shared office IP is not
            // punished), per IP when anonymous.
            var sub = context.User.FindFirst("sub")?.Value;
            var key = !string.IsNullOrEmpty(sub)
                ? $"user:{sub}"
                : $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
            context.Items[PolicyItem] = ApiPolicy;
            // The log shows the subject (allowed), but never an IP address.
            context.Items[PartitionItem] = !string.IsNullOrEmpty(sub) ? $"user:{sub}" : "ip";
            return RateLimitPartition.GetTokenBucketLimiter($"{ApiPolicy}:{key}", _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = Math.Max(1, value.Api.TokenLimit),
                TokensPerPeriod = Math.Max(1, value.Api.TokensPerPeriod),
                ReplenishmentPeriod = TimeSpan.FromSeconds(Math.Max(1, value.Api.ReplenishmentSeconds)),
                QueueLimit = 0,
                AutoReplenishment = true,
            });
        });

        options.OnRejected = async (rejected, cancellationToken) =>
        {
            var http = rejected.HttpContext;
            var policy = http.Items[PolicyItem] as string ?? ApiPolicy;
            var partition = http.Items[PartitionItem] as string ?? "unknown";
            SecurityEvents.RateLimited(logger, policy, partition);

            // Retry-After tells a well-behaved client when to come back; fall back to the policy's window if the
            // limiter did not provide a hint.
            var retryAfter = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var hint)
                ? hint
                : TimeSpan.FromSeconds(policy == LoginPolicy ? settings.Value.Login.WindowSeconds : settings.Value.Api.ReplenishmentSeconds);
            http.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

            await Results.Problem(
                    statusCode: StatusCodes.Status429TooManyRequests,
                    title: "Too many requests",
                    detail: "Rate limit exceeded. Retry after the number of seconds in the Retry-After header.")
                .ExecuteAsync(http);
        };
    }
}
