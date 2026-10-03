using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlantOps.Modules.Assets.Tests.Integration;
using PlantOps.Modules.WorkOrders.Infrastructure;

namespace PlantOps.Modules.WorkOrders.Tests.Integration;

// The technician's work queue: a partial of the main API test class so it shares its clients and helpers.
public partial class WorkOrdersApiTests
{
    // Raised, approved and assigned to Tom; optionally started by him.
    private async Task<Guid> AssignedToTom(Guid assetId, bool started = false)
    {
        using var oscar = Oscar();
        using var sam = Sam();
        using var tom = Tom();
        var (id, etag) = await Raise(oscar, assetId);
        etag = await Succeed(sam, id, "approve", etag);
        etag = await Succeed(sam, id, "assign", etag, new { technicianId = "tom" });
        if (started)
        {
            await Succeed(tom, id, "start", etag);
        }

        return id;
    }

    private async Task<Guid> NewPartInStock()
    {
        using var sam = Sam();
        var created = await sam.PostAsJsonAsync("/api/inventory/parts", new
        {
            partNumber = NewPrefix() + "-PART",
            name = "Feeder spring",
            unit = "pcs",
            binLocation = "A-03-2",
            reorderLevel = 1,
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var partId = (await ReadJson(created)).GetProperty("id").GetGuid();
        var received = await sam.PostAsJsonAsync($"/api/inventory/parts/{partId}/receive", new { quantity = 10 }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, received.StatusCode);
        return partId;
    }

    private static async Task ReserveAsTom(HttpClient tom, Guid partId, Guid workOrderId)
    {
        var response = await tom.PostAsJsonAsync("/api/inventory/reservations", new { partId, workOrderId, quantity = 1 }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task SetDueAtAsync(Guid workOrderId, DateTimeOffset dueAt)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkOrdersDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [workorders].[WorkOrders] SET [DueAt] = {dueAt} WHERE [Id] = {workOrderId}", Ct);
    }

    private static async Task<List<JsonElement>> Queue(HttpClient client, string query = "")
    {
        var response = await client.GetAsync($"/api/work-orders/queue{query}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return [.. (await ReadJson(response)).EnumerateArray()];
    }

    [IntegrationFact]
    public async Task The_queue_holds_only_my_open_assigned_work_orders_with_their_reserved_part_counts()
    {
        var assetId = await RegisterAsset();
        using var tom = Tom();
        using var lee = Lee();
        using var sam = Sam();

        var assigned = await AssignedToTom(assetId);
        var inProgress = await AssignedToTom(assetId, started: true);

        var firstPart = await NewPartInStock();
        var secondPart = await NewPartInStock();
        await ReserveAsTom(tom, firstPart, assigned);
        await ReserveAsTom(tom, secondPart, assigned);

        // Not Tom's queue: assigned to Lee, still waiting for a technician, and already completed.
        using var oscar = Oscar();
        var (leesId, leesEtag) = await Raise(oscar, assetId);
        leesEtag = await Succeed(sam, leesId, "approve", leesEtag);
        await Succeed(sam, leesId, "assign", leesEtag, new { technicianId = "lee" });
        var (unassigned, _) = await Raise(oscar, assetId);
        var completed = await AssignedToTom(assetId, started: true);
        var completedEtag = ETagOf(await tom.GetAsync($"/api/work-orders/{completed}", Ct));
        await Succeed(tom, completed, "complete", completedEtag, new { resolution = "Replaced the feeder spring." });

        var queue = await Queue(tom);

        var ids = queue.Select(i => i.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(assigned, ids);
        Assert.Contains(inProgress, ids);
        Assert.DoesNotContain(leesId, ids);
        Assert.DoesNotContain(unassigned, ids);
        Assert.DoesNotContain(completed, ids);

        var first = queue.Single(i => i.GetProperty("id").GetGuid() == assigned);
        Assert.Equal(2, first.GetProperty("partsReserved").GetInt32());
        Assert.Equal("Assigned", first.GetProperty("status").GetString());
        Assert.Equal("P2", first.GetProperty("priority").GetString());
        Assert.Matches(NumberPattern(), first.GetProperty("number").GetString()!);
        Assert.StartsWith("W", first.GetProperty("assetTag").GetString());
        var second = queue.Single(i => i.GetProperty("id").GetGuid() == inProgress);
        Assert.Equal(0, second.GetProperty("partsReserved").GetInt32());
        Assert.Equal("InProgress", second.GetProperty("status").GetString());

        var leesQueue = (await Queue(lee)).Select(i => i.GetProperty("id").GetGuid());
        Assert.Contains(leesId, leesQueue);
        Assert.DoesNotContain(assigned, leesQueue);
    }

    [IntegrationFact]
    public async Task The_due_today_filter_keeps_only_work_orders_due_today()
    {
        var assetId = await RegisterAsset();
        using var tom = Tom();
        var dueToday = await AssignedToTom(assetId);
        var dueTomorrow = await AssignedToTom(assetId);
        var utcMidnight = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        await SetDueAtAsync(dueToday, utcMidnight.AddHours(12));
        await SetDueAtAsync(dueTomorrow, utcMidnight.AddDays(1).AddHours(12));

        var filtered = (await Queue(tom, "?dueToday=true")).Select(i => i.GetProperty("id").GetGuid()).ToList();
        var everything = (await Queue(tom)).Select(i => i.GetProperty("id").GetGuid()).ToList();

        Assert.Contains(dueToday, filtered);
        Assert.DoesNotContain(dueTomorrow, filtered);
        Assert.Contains(dueToday, everything);
        Assert.Contains(dueTomorrow, everything);
    }
}
