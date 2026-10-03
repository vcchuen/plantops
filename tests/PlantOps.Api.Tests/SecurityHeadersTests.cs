using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PlantOps.Modules.Identity.Contracts;

namespace PlantOps.Api.Tests;

public class SecurityHeadersTests
{
    // Spelled out here on purpose (not read from the middleware): changing the policy must be a deliberate edit of both.
    private const string ExpectedCsp =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; img-src 'self' data:; connect-src 'self'; " +
        "frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static WebApplicationFactory<Program> CreateFactory(Action<IWebHostBuilder>? configure = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:PlantOps", string.Empty);
            configure?.Invoke(builder);
        });

    private static void AssertSecurityHeaders(HttpResponseMessage response)
    {
        Assert.Equal(ExpectedCsp, Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("strict-origin-when-cross-origin", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Equal("camera=(), microphone=(), geolocation=()", Assert.Single(response.Headers.GetValues("Permissions-Policy")));
        Assert.Equal("same-origin", Assert.Single(response.Headers.GetValues("Cross-Origin-Opener-Policy")));
    }

    [Fact]
    public async Task Health_live_carries_the_security_headers()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task Api_responses_carry_the_security_headers_including_the_401_and_the_400_problem()
    {
        await using var factory = CreateFactory(b => b.ConfigureTestServices(s => s.AddTestAuthentication()));
        using var anonymous = factory.CreateClient();
        using var signedIn = factory.CreateClient().AsUser("Sam", Roles.Supervisor);

        var unauthorized = await anonymous.GetAsync("/api/assets", Ct);
        var badRequest = await signedIn.GetAsync("/api/assets?criticality=Z", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badRequest.StatusCode);
        AssertSecurityHeaders(unauthorized);
        // The exception handler rewrites failures and clears headers; the middleware sets them at response start so they survive.
        AssertSecurityHeaders(badRequest);
    }

    [Fact]
    public async Task The_spa_fallback_and_static_files_carry_the_security_headers()
    {
        var webRoot = Directory.CreateTempSubdirectory("plantops-wwwroot-").FullName;
        await File.WriteAllTextAsync(Path.Combine(webRoot, "index.html"), "<html>spa</html>", Ct);
        try
        {
            await using var factory = CreateFactory(b => b.UseSetting(WebHostDefaults.WebRootKey, webRoot));
            using var client = factory.CreateClient();

            var route = await client.GetAsync("/some/spa/route", Ct);
            var file = await client.GetAsync("/index.html", Ct);

            Assert.Equal(HttpStatusCode.OK, route.StatusCode);
            Assert.Equal(HttpStatusCode.OK, file.StatusCode);
            AssertSecurityHeaders(route);
            AssertSecurityHeaders(file);
        }
        finally
        {
            Directory.Delete(webRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Hsts_is_sent_over_https_outside_development_and_never_in_development()
    {
        await using var production = CreateFactory(b => b.UseEnvironment("Production"));
        await using var development = CreateFactory(b => b.UseEnvironment("Development"));
        // HSTS skips localhost by default, so the request must look like it targets a real host name.
        var options = new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://plantops.example") };
        using var prodClient = production.CreateClient(options);
        using var devClient = development.CreateClient(options);

        var prod = await prodClient.GetAsync("/health/live", Ct);
        var dev = await devClient.GetAsync("/health/live", Ct);

        Assert.Contains("max-age=", Assert.Single(prod.Headers.GetValues("Strict-Transport-Security")));
        Assert.False(dev.Headers.Contains("Strict-Transport-Security"));
    }
}
