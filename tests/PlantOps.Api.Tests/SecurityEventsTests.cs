using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlantOps.BuildingBlocks.Infrastructure;
using PlantOps.Modules.Identity.Contracts;

namespace PlantOps.Api.Tests;

public class SecurityEventsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_post_without_the_csrf_header_emits_event_1004_with_the_path_only()
    {
        var logs = new CapturingLoggerProvider();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:PlantOps", string.Empty);
            builder.ConfigureTestServices(s => s.AddTestAuthentication());
            builder.ConfigureLogging(l => l.AddProvider(logs));
        });
        using var client = factory.CreateClient().AsUser("Sam", Roles.Supervisor);

        var response = await client.PostAsJsonAsync("/api/assets?secret=hunter2", new { }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var entry = Assert.Single(logs.Entries, e => e.EventId.Id == SecurityEvents.CsrfRejectedId);
        Assert.Equal(SecurityEvents.Category, entry.Category);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("/api/assets", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_event_has_its_fixed_id_level_and_a_message_that_carries_only_what_it_should()
    {
        var logs = new CapturingLoggerProvider();
        var logger = logs.CreateLogger(SecurityEvents.Category);
        var messageId = Guid.Parse("0197a5c0-0000-7000-8000-0000000000aa");

        SecurityEvents.SignInSucceeded(logger, "11111111-1111-4111-8111-111111111111");
        SecurityEvents.SignInFailed(logger, "SecurityTokenException");
        SecurityEvents.AccessDenied(logger, "/api/reports/mttr", "11111111-1111-4111-8111-111111111111");
        SecurityEvents.CsrfRejected(logger, "/api/assets");
        SecurityEvents.RateLimited(logger, "login", "ip");
        SecurityEvents.OutboxMessageParked(logger, messageId, "WorkOrderCompleted");

        var entries = logs.Entries;
        Assert.Equal([1001, 1002, 1003, 1004, 1005, 1006], entries.Select(e => e.EventId.Id));
        Assert.Equal(
            [
                "SignInSucceeded", "SignInFailed", "AccessDenied", "CsrfRejected", "RateLimited", "OutboxMessageParked",
            ],
            entries.Select(e => e.EventId.Name));
        Assert.Contains("11111111-1111-4111-8111-111111111111", entries[0].Message, StringComparison.Ordinal);
        Assert.Contains("/api/reports/mttr", entries[2].Message, StringComparison.Ordinal);
        Assert.Contains("login", entries[4].Message, StringComparison.Ordinal);
        Assert.Contains(messageId.ToString(), entries[5].Message, StringComparison.Ordinal);
        Assert.Equal(LogLevel.Error, entries[5].Level);
    }
}
