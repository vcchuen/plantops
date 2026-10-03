using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlantOps.BuildingBlocks.Infrastructure;
using PlantOps.Modules.Identity.Contracts;

namespace PlantOps.Api.Tests;

// Offline: the login endpoint answers 503 (no IdP configured) and /api/identity/me needs no database, and a rate-limited
// attempt counts whatever the endpoint would have answered, which is all these tests need.
public class RateLimitingTests
{
    private const string Login = "/api/identity/login";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static WebApplicationFactory<Program> CreateFactory(
        IDictionary<string, string>? settings = null,
        CapturingLoggerProvider? logs = null,
        IPAddress? remoteAddress = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:PlantOps", string.Empty);
            foreach (var (key, value) in settings ?? new Dictionary<string, string>())
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureTestServices(s =>
            {
                s.AddTestAuthentication();
                if (remoteAddress is not null)
                {
                    s.AddSingleton<IStartupFilter>(new RemoteAddressStartupFilter(remoteAddress));
                }
            });
            if (logs is not null)
            {
                builder.ConfigureLogging(l => l.AddProvider(logs));
            }
        });

    [Fact]
    public async Task The_eleventh_login_request_in_a_minute_is_429_with_retry_after_and_a_problem_body()
    {
        // The shipped defaults: 10 per minute per client IP.
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        for (var i = 1; i <= 10; i++)
        {
            var allowed = await client.GetAsync(Login, Ct);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, allowed.StatusCode);
        }

        var rejected = await client.GetAsync(Login, Ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);
        var retryAfter = Assert.Single(rejected.Headers.GetValues("Retry-After"));
        Assert.InRange(int.Parse(retryAfter, System.Globalization.CultureInfo.InvariantCulture), 1, 60);
    }

    [Fact]
    public async Task Login_limits_are_configurable()
    {
        await using var factory = CreateFactory(new Dictionary<string, string> { ["RateLimiting:Login:PermitLimit"] = "2" });
        using var client = factory.CreateClient();

        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await client.GetAsync(Login, Ct)).StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await client.GetAsync(Login, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync(Login, Ct)).StatusCode);
    }

    [Fact]
    public async Task Api_calls_are_limited_per_user_and_health_and_static_paths_never_are()
    {
        await using var factory = CreateFactory(new Dictionary<string, string>
        {
            ["RateLimiting:Api:TokenLimit"] = "2",
            ["RateLimiting:Api:TokensPerPeriod"] = "2",
            ["RateLimiting:Api:ReplenishmentSeconds"] = "3600",
        });
        using var sam = factory.CreateClient().AsUser("Sam", Roles.Supervisor);
        using var tom = factory.CreateClient().AsUser("Tom", Roles.Technician);

        Assert.Equal(HttpStatusCode.OK, (await sam.GetAsync("/api/identity/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await sam.GetAsync("/api/identity/me", Ct)).StatusCode);
        var rejected = await sam.GetAsync("/api/identity/me", Ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.NotEmpty(rejected.Headers.GetValues("Retry-After"));
        // A different user has their own bucket.
        Assert.Equal(HttpStatusCode.OK, (await tom.GetAsync("/api/identity/me", Ct)).StatusCode);
        // Health probes are outside /api and must never be refused, however busy the user.
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await sam.GetAsync("/health/live", Ct)).StatusCode);
        }
    }

    [Fact]
    public async Task Anonymous_api_calls_are_limited_per_client_ip_taken_from_x_forwarded_for_only_when_trusted()
    {
        var settings = new Dictionary<string, string>
        {
            ["RateLimiting:Login:PermitLimit"] = "1",
            ["ForwardedHeaders:TrustAll"] = "true",
        };
        // TestServer connections come from loopback, which the default configuration already trusts; a real App Service
        // front end is some other address, so pretend to be one.
        var frontEnd = IPAddress.Parse("198.51.100.9");
        await using var trusting = CreateFactory(settings, remoteAddress: frontEnd);
        using var client = trusting.CreateClient();

        HttpRequestMessage From(string ip)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, Login);
            request.Headers.Add("X-Forwarded-For", ip);
            return request;
        }

        // Two clients behind the same front end get separate buckets because the forwarded address is honoured.
        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await client.SendAsync(From("203.0.113.7"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(From("203.0.113.7"), Ct)).StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await client.SendAsync(From("203.0.113.8"), Ct)).StatusCode);

        // Without the opt-in the header is ignored, so a client cannot dodge the limit by forging it.
        await using var strict = CreateFactory(new Dictionary<string, string> { ["RateLimiting:Login:PermitLimit"] = "1" }, remoteAddress: frontEnd);
        using var strictClient = strict.CreateClient();
        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await strictClient.SendAsync(From("203.0.113.7"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await strictClient.SendAsync(From("203.0.113.8"), Ct)).StatusCode);
    }

    [Fact]
    public async Task A_rejection_emits_security_event_1005_without_an_ip_address()
    {
        var logs = new CapturingLoggerProvider();
        await using var factory = CreateFactory(
            new Dictionary<string, string>
            {
                ["RateLimiting:Api:TokenLimit"] = "1",
                ["RateLimiting:Api:TokensPerPeriod"] = "1",
                ["RateLimiting:Api:ReplenishmentSeconds"] = "3600",
            },
            logs);
        using var sam = factory.CreateClient().AsUser("Sam", Roles.Supervisor);
        using var anonymous = factory.CreateClient();

        await sam.GetAsync("/api/identity/me", Ct);
        await sam.GetAsync("/api/identity/me", Ct);
        await anonymous.GetAsync("/api/assets", Ct);
        await anonymous.GetAsync("/api/assets", Ct);

        var events = logs.Entries.Where(e => e.EventId.Id == SecurityEvents.RateLimitedId).ToList();
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal(SecurityEvents.Category, e.Category));
        Assert.Contains(events, e => e.Message.Contains("'api'", StringComparison.Ordinal) && e.Message.Contains("user:sam", StringComparison.Ordinal));
        // The anonymous partition is labelled, never the address.
        Assert.Contains(events, e => e.Message.EndsWith("(partition ip)", StringComparison.Ordinal));
    }

    // Runs before the application's own middleware, so the forwarded-headers middleware sees this as the peer address.
    private sealed class RemoteAddressStartupFilter(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = address;
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
