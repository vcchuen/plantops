using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PlantOps.Modules.Identity.Contracts;

namespace PlantOps.Api.Tests;

public class HostTests
{
    private static WebApplicationFactory<Program> CreateFactory(string? connectionString = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // Overrides any user-secrets value so tests never touch a developer's real database.
            builder.UseSetting("ConnectionStrings:PlantOps", connectionString ?? string.Empty);
        });

    [Fact]
    public async Task Live_returns_200_without_running_checks()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_without_connection_string_returns_503_and_no_exception_text()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Unhealthy", doc.RootElement.GetProperty("status").GetString());
        Assert.DoesNotContain("Exception", json);
        Assert.DoesNotContain(" at ", json);
    }

    [Fact]
    public async Task Ready_with_unreachable_server_returns_503_quickly_and_does_not_leak_details()
    {
        await using var factory = CreateFactory("Server=tcp:127.0.0.1,1;Database=PlantOps;User Id=sa;Password=x;Connect Timeout=1;Encrypt=False");
        using var client = factory.CreateClient();

        var stopwatch = Stopwatch.StartNew();
        var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        stopwatch.Stop();
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15), $"took {stopwatch.Elapsed}");
        Assert.DoesNotContain("127.0.0.1", json);
        Assert.DoesNotContain("Exception", json);
    }

    [Fact]
    public async Task Unknown_api_route_returns_404_problem_json_not_the_spa()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/does-not-exist", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Unbindable_query_value_returns_400_problem_not_500()
    {
        await using var factory = CreateFactory().WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddTestAuthentication()));
        using var client = factory.CreateClient().AsUser("Sam", Roles.Supervisor);

        var response = await client.GetAsync("/api/assets?criticality=Z", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Unknown_non_api_route_falls_back_to_index_html()
    {
        var webRoot = Directory.CreateTempSubdirectory("plantops-wwwroot-").FullName;
        await File.WriteAllTextAsync(Path.Combine(webRoot, "index.html"), "<html><body>spa</body></html>", TestContext.Current.CancellationToken);
        try
        {
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseSetting(WebHostDefaults.WebRootKey, webRoot));
            using var client = factory.CreateClient();

            var response = await client.GetAsync("/some/spa/route", TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        }
        finally
        {
            Directory.Delete(webRoot, recursive: true);
        }
    }
}
