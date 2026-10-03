using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlantOps.Api.Tests;
using PlantOps.BuildingBlocks.Infrastructure;
using PlantOps.Modules.Assets.Tests.Integration;
using PlantOps.Modules.WorkOrders.Contracts;
using PlantOps.Modules.WorkOrders.Infrastructure;

namespace PlantOps.Modules.WorkOrders.Tests.Integration;

// M6: SLA escalation, preventive maintenance and the /api/pm-schedules endpoints. A partial of the main API test class
// so it shares its helpers (clients, asset registration, ETag and problem assertions) and its collection fixture.
// The runners are resolved from the host's container, exactly as the Functions timers and InProcessJobs call them.
public partial class WorkOrdersApiTests
{
    private static readonly TimeZoneInfo Penang = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kuala_Lumpur");

    private static DateOnly FactoryToday() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Penang).DateTime);

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd");

    private async Task<int> RunEscalationAsync()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISlaEscalationRunner>().RunAsync(Ct);
    }

    private async Task<int> RunPreventiveMaintenanceAsync()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPreventiveMaintenanceRunner>().RunAsync(Ct);
    }

    // Moves the deadline into the past directly in the database: the API always sets DueAt from the clock, and the
    // fixture runs the real clock, so this is how a test makes an order "breached" without waiting hours.
    private async Task MakeBreachedAsync(Guid workOrderId)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkOrdersDbContext>();
        var past = DateTimeOffset.UtcNow.AddHours(-2);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [workorders].[WorkOrders] SET [DueAt] = {past} WHERE [Id] = {workOrderId}", Ct);
    }

    private async Task<List<JsonElement>> History(HttpClient client, string path, Guid id) =>
        [.. (await client.GetFromJsonAsync<JsonElement>($"/api/{path}/{id}/history", Ct)).EnumerateArray()];

    private async Task<List<JsonElement>> WorkOrdersOf(HttpClient client, Guid assetId, string extraQuery = "")
    {
        var page = await client.GetFromJsonAsync<JsonElement>($"/api/work-orders?assetId={assetId}&pageSize=100{extraQuery}", Ct);
        return [.. page.GetProperty("items").EnumerateArray()];
    }

    private static object ScheduleBody(Guid assetId, DateOnly nextDueOn, string title = "Clean reflow oven", int interval = 30, int lead = 1, string priority = "P3") => new
    {
        assetId,
        title,
        instructions = "Remove flux residue.",
        intervalDays = interval,
        leadDays = lead,
        priority,
        nextDueOn = Iso(nextDueOn),
    };

    private static object ScheduleUpdate(DateOnly nextDueOn, string title = "Clean reflow oven", int interval = 30, int lead = 1, string priority = "P3") => new
    {
        title,
        instructions = "Remove flux residue.",
        intervalDays = interval,
        leadDays = lead,
        priority,
        nextDueOn = Iso(nextDueOn),
    };

    private async Task<(Guid Id, string ETag)> CreateSchedule(Guid assetId, DateOnly nextDueOn, int interval = 30, int lead = 1)
    {
        using var sam = Sam();
        var response = await sam.PostAsJsonAsync("/api/pm-schedules", ScheduleBody(assetId, nextDueOn, interval: interval, lead: lead), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return ((await ReadJson(response)).GetProperty("id").GetGuid(), ETagOf(response));
    }

    private static Task<HttpResponseMessage> PmCommand(HttpClient client, HttpMethod method, string path, string? etag, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (etag is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", etag);
        }

        request.Content = JsonContent.Create(body ?? new { });
        return client.SendAsync(request, Ct);
    }

    private static async Task<JsonElement> ScheduleDetail(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"/api/pm-schedules/{id}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJson(response);
    }

    // ---- SLA escalation ----

    [IntegrationFact]
    public async Task Escalation_escalates_only_breached_open_work_orders_and_only_once()
    {
        var assetId = await RegisterAsset();
        using var oscar = Oscar();
        using var sam = Sam();
        var (breached, _) = await Raise(oscar, assetId);
        var (onTime, _) = await Raise(oscar, assetId);
        var (cancelled, cancelEtag) = await Raise(oscar, assetId);
        await Succeed(sam, cancelled, "cancel", cancelEtag, new { reason = "Reported by mistake" });
        await MakeBreachedAsync(breached);
        await MakeBreachedAsync(cancelled); // past its deadline, but nobody owes that repair any more

        var firstRun = await RunEscalationAsync();

        Assert.True(firstRun >= 1);
        var escalatedDetail = await Detail(sam, breached);
        Assert.NotEqual(JsonValueKind.Null, escalatedDetail.GetProperty("escalatedAt").ValueKind);
        Assert.Equal("Reactive", escalatedDetail.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, (await Detail(sam, onTime)).GetProperty("escalatedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, (await Detail(sam, cancelled)).GetProperty("escalatedAt").ValueKind);

        // Running again escalates nothing: still one audit event, still one outbox row.
        Assert.Equal(0, await RunEscalationAsync());
        var breaches = (await History(sam, "work-orders", breached)).Where(e => e.GetProperty("eventType").GetString() == "WorkOrderSlaBreached").ToList();
        var breach = Assert.Single(breaches);
        Assert.Equal("System", breach.GetProperty("actorName").GetString());
        Assert.Equal("P2", breach.GetProperty("payload").GetProperty("priority").GetString());

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkOrdersDbContext>();
        var eventName = typeof(WorkOrderSlaBreachedIntegrationEvent).FullName;
        var idText = breached.ToString();
        Assert.Equal(1, await db.Set<OutboxMessage>().CountAsync(m => m.Type == eventName && m.Payload.Contains(idText), Ct));
    }

    [IntegrationFact]
    public async Task The_escalated_filter_lists_only_escalated_work_orders()
    {
        var assetId = await RegisterAsset();
        using var oscar = Oscar();
        using var sam = Sam();
        var (breached, _) = await Raise(oscar, assetId);
        var (onTime, _) = await Raise(oscar, assetId);
        await MakeBreachedAsync(breached);
        await RunEscalationAsync();

        var escalated = await WorkOrdersOf(sam, assetId, "&escalated=true");
        var all = await WorkOrdersOf(sam, assetId);

        var only = Assert.Single(escalated);
        Assert.Equal(breached, only.GetProperty("id").GetGuid());
        Assert.NotEqual(JsonValueKind.Null, only.GetProperty("escalatedAt").ValueKind);
        Assert.Equal("Reactive", only.GetProperty("source").GetString());
        Assert.Equal(2, all.Count);
        Assert.Contains(all, w => w.GetProperty("id").GetGuid() == onTime && w.GetProperty("escalatedAt").ValueKind == JsonValueKind.Null);
    }

    [IntegrationFact]
    public async Task Delivering_the_breach_event_with_the_logging_publisher_succeeds()
    {
        var assetId = await RegisterAsset();
        using var oscar = Oscar();
        var (breached, _) = await Raise(oscar, assetId);
        await MakeBreachedAsync(breached);
        await RunEscalationAsync();

        // The test host has no Service Bus settings, so the forwarder uses the logging publisher. Drain the outbox
        // the way the dispatcher would (its own timer may claim the row first, so poll until it is processed).
        var processor = fixture.Factory.Services.GetRequiredService<IOutboxProcessor<WorkOrdersDbContext>>();
        var eventName = typeof(WorkOrderSlaBreachedIntegrationEvent).FullName;
        var idText = breached.ToString();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        OutboxMessage? message;
        do
        {
            await processor.ProcessOnceAsync(Ct);
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<WorkOrdersDbContext>();
            message = await db.Set<OutboxMessage>().AsNoTracking().SingleAsync(m => m.Type == eventName && m.Payload.Contains(idText), Ct);
            if (message.ProcessedAt is null)
            {
                await Task.Delay(200, Ct);
            }
        }
        while (message.ProcessedAt is null && DateTimeOffset.UtcNow < deadline);

        Assert.NotNull(message.ProcessedAt);
        Assert.Null(message.LastError);
        Assert.Equal(0, message.Attempts);
    }

    // ---- preventive maintenance ----

    [IntegrationFact]
    public async Task The_pm_runner_generates_an_approved_preventive_work_order_advances_the_schedule_and_does_not_repeat()
    {
        var assetId = await RegisterAsset();
        var dueOn = FactoryToday().AddDays(1);
        var (scheduleId, _) = await CreateSchedule(assetId, dueOn, interval: 30, lead: 1);
        using var sam = Sam();

        Assert.True(await RunPreventiveMaintenanceAsync() >= 1);

        var generated = Assert.Single(await WorkOrdersOf(sam, assetId));
        Assert.Equal("Preventive", generated.GetProperty("source").GetString());
        Assert.Equal("Approved", generated.GetProperty("status").GetString());
        var detail = await Detail(sam, generated.GetProperty("id").GetGuid());
        Assert.Equal(scheduleId, detail.GetProperty("pmScheduleId").GetGuid());
        Assert.Equal(Iso(dueOn), detail.GetProperty("pmDueOn").GetString());
        Assert.Equal("System", detail.GetProperty("reportedBy").GetProperty("name").GetString());
        Assert.Equal("Clean reflow oven", detail.GetProperty("title").GetString());
        // The deadline is the end of the due day in factory time, not a priority target.
        Assert.Equal(new DateTimeOffset(dueOn.ToDateTime(TimeOnly.MaxValue), TimeSpan.FromHours(8)), detail.GetProperty("dueAt").GetDateTimeOffset());
        Assert.Equal(Iso(dueOn.AddDays(30)), (await ScheduleDetail(sam, scheduleId)).GetProperty("nextDueOn").GetString());

        // A second run the same day finds nothing due.
        Assert.Equal(0, await RunPreventiveMaintenanceAsync());
        Assert.Single(await WorkOrdersOf(sam, assetId));

        var scheduleHistory = await History(sam, "pm-schedules", scheduleId);
        Assert.Single(scheduleHistory, e => e.GetProperty("eventType").GetString() == "PmWorkOrderGenerated");
    }

    [IntegrationFact]
    public async Task Racing_pm_runners_generate_exactly_one_work_order()
    {
        var assetId = await RegisterAsset();
        var (scheduleId, _) = await CreateSchedule(assetId, FactoryToday().AddDays(1), lead: 1);
        using var sam = Sam();

        // Four runners start together: each reads the schedule as due and tries to insert. The filtered unique index
        // on (PmScheduleId, PmDueOn) (and the schedule's rowversion) lets exactly one commit; the rest must treat
        // that as "already generated", not fail.
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(RunPreventiveMaintenanceAsync, Ct)));

        Assert.Equal(1, results.Sum());
        Assert.Single(await WorkOrdersOf(sam, assetId));
        Assert.Single((await History(sam, "pm-schedules", scheduleId)), e => e.GetProperty("eventType").GetString() == "PmWorkOrderGenerated");
    }

    [IntegrationFact]
    public async Task A_schedule_pointed_back_at_an_occurrence_that_already_has_a_work_order_advances_without_a_duplicate()
    {
        var assetId = await RegisterAsset();
        var dueOn = FactoryToday().AddDays(1);
        var (scheduleId, _) = await CreateSchedule(assetId, dueOn, lead: 1);
        using var sam = Sam();
        await RunPreventiveMaintenanceAsync();
        var etag = ETagOf(await sam.GetAsync($"/api/pm-schedules/{scheduleId}", Ct));

        // A supervisor edits the date back to the occurrence that was already generated.
        var put = await PmCommand(sam, HttpMethod.Put, $"/api/pm-schedules/{scheduleId}", etag, ScheduleUpdate(dueOn, lead: 1));
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        await RunPreventiveMaintenanceAsync();

        Assert.Single(await WorkOrdersOf(sam, assetId));
        Assert.Equal(Iso(dueOn.AddDays(30)), (await ScheduleDetail(sam, scheduleId)).GetProperty("nextDueOn").GetString());
        Assert.Contains(await History(sam, "pm-schedules", scheduleId), e => e.GetProperty("eventType").GetString() == "PmOccurrenceAlreadyGenerated");
    }

    [IntegrationFact]
    public async Task An_inactive_schedule_generates_nothing_until_it_is_activated()
    {
        var assetId = await RegisterAsset();
        var (scheduleId, etag) = await CreateSchedule(assetId, FactoryToday().AddDays(1), lead: 1);
        using var sam = Sam();
        var deactivate = await PmCommand(sam, HttpMethod.Post, $"/api/pm-schedules/{scheduleId}/deactivate", etag);
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);

        await RunPreventiveMaintenanceAsync();
        Assert.Empty(await WorkOrdersOf(sam, assetId));

        var activate = await PmCommand(sam, HttpMethod.Post, $"/api/pm-schedules/{scheduleId}/activate", ETagOf(deactivate));
        Assert.Equal(HttpStatusCode.NoContent, activate.StatusCode);
        await RunPreventiveMaintenanceAsync();
        Assert.Single(await WorkOrdersOf(sam, assetId));
    }

    // ---- /api/pm-schedules ----

    [IntegrationFact]
    public async Task Creating_a_schedule_returns_201_with_location_etag_and_the_detail()
    {
        var assetId = await RegisterAsset();
        using var sam = Sam();
        var dueOn = FactoryToday().AddDays(200);

        var response = await sam.PostAsJsonAsync("/api/pm-schedules", ScheduleBody(assetId, dueOn, lead: 0, priority: "P2"), Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJson(response);
        var id = body.GetProperty("id").GetGuid();
        Assert.Equal($"/api/pm-schedules/{id}", response.Headers.Location?.ToString());
        Assert.NotEmpty(ETagOf(response));
        Assert.Equal(assetId, body.GetProperty("assetId").GetGuid());
        Assert.Equal("Pick and place", body.GetProperty("assetName").GetString());
        Assert.Equal("Clean reflow oven", body.GetProperty("title").GetString());
        Assert.Equal("Remove flux residue.", body.GetProperty("instructions").GetString());
        Assert.Equal(30, body.GetProperty("intervalDays").GetInt32());
        Assert.Equal(0, body.GetProperty("leadDays").GetInt32());
        Assert.Equal("P2", body.GetProperty("priority").GetString());
        Assert.Equal(Iso(dueOn), body.GetProperty("nextDueOn").GetString());
        Assert.True(body.GetProperty("isActive").GetBoolean());
    }

    [IntegrationFact]
    public async Task The_list_filters_by_asset_and_active_and_any_signed_in_user_may_read()
    {
        var assetId = await RegisterAsset();
        var dueOn = FactoryToday().AddDays(200);
        var (activeId, _) = await CreateSchedule(assetId, dueOn, lead: 0);
        var (inactiveId, inactiveEtag) = await CreateSchedule(assetId, dueOn.AddDays(1), lead: 0);
        using var sam = Sam();
        Assert.Equal(HttpStatusCode.NoContent, (await PmCommand(sam, HttpMethod.Post, $"/api/pm-schedules/{inactiveId}/deactivate", inactiveEtag)).StatusCode);
        using var oscar = Oscar();

        var all = await oscar.GetFromJsonAsync<JsonElement>($"/api/pm-schedules?assetId={assetId}", Ct);
        var active = await oscar.GetFromJsonAsync<JsonElement>($"/api/pm-schedules?assetId={assetId}&active=true", Ct);
        var inactive = await oscar.GetFromJsonAsync<JsonElement>($"/api/pm-schedules?assetId={assetId}&active=false", Ct);

        Assert.Equal([activeId, inactiveId], all.EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).ToArray()); // by next due date
        Assert.Equal(activeId, Assert.Single(active.EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(inactiveId, Assert.Single(inactive.EnumerateArray()).GetProperty("id").GetGuid());
        var first = all.EnumerateArray().First();
        Assert.False(first.TryGetProperty("instructions", out _)); // the list item omits it; the detail has it
        Assert.Equal(Iso(dueOn), first.GetProperty("nextDueOn").GetString());
    }

    [IntegrationFact]
    public async Task Get_returns_the_detail_with_an_etag_and_an_unknown_id_is_404()
    {
        var assetId = await RegisterAsset();
        var (id, createEtag) = await CreateSchedule(assetId, FactoryToday().AddDays(200), lead: 0);
        using var oscar = Oscar();

        var response = await oscar.GetAsync($"/api/pm-schedules/{id}", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(createEtag, ETagOf(response));
        Assert.Equal("Remove flux residue.", (await ReadJson(response)).GetProperty("instructions").GetString());
        await AssertProblem(await oscar.GetAsync($"/api/pm-schedules/{Guid.NewGuid()}", Ct), HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task Invalid_schedules_are_rejected_with_400()
    {
        var assetId = await RegisterAsset();
        using var sam = Sam();
        var due = FactoryToday().AddDays(200);

        foreach (var body in new object[]
        {
            ScheduleBody(assetId, due, interval: 0),
            ScheduleBody(assetId, due, interval: 366),
            ScheduleBody(assetId, due, lead: 31),
            ScheduleBody(assetId, due, lead: -1),
            ScheduleBody(assetId, due, title: " "),
            new { assetId, title = "No date", instructions = "", intervalDays = 30, leadDays = 1, priority = "P3" },
        })
        {
            await AssertProblem(await sam.PostAsJsonAsync("/api/pm-schedules", body, Ct), HttpStatusCode.BadRequest);
        }
    }

    [IntegrationFact]
    public async Task An_unknown_or_decommissioned_asset_is_400()
    {
        var retired = await RegisterAsset();
        using var sam = Sam();
        var decommission = await sam.PostAsJsonAsync($"/api/assets/{retired}/decommission", new { on = Iso(FactoryToday()), reason = "Scrapped" }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, decommission.StatusCode);
        var due = FactoryToday().AddDays(200);

        await AssertProblem(await sam.PostAsJsonAsync("/api/pm-schedules", ScheduleBody(Guid.NewGuid(), due), Ct), HttpStatusCode.BadRequest);
        await AssertProblem(await sam.PostAsJsonAsync("/api/pm-schedules", ScheduleBody(retired, due), Ct), HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task Updating_requires_if_match_rejects_a_stale_one_and_returns_the_new_etag()
    {
        var assetId = await RegisterAsset();
        var due = FactoryToday().AddDays(200);
        var (id, etag) = await CreateSchedule(assetId, due, lead: 0);
        using var sam = Sam();
        var path = $"/api/pm-schedules/{id}";

        await AssertProblem(await PmCommand(sam, HttpMethod.Put, path, etag: null, ScheduleUpdate(due)), HttpStatusCode.PreconditionRequired);
        await AssertProblem(await PmCommand(sam, HttpMethod.Put, path, "not-an-etag", ScheduleUpdate(due)), HttpStatusCode.BadRequest);

        var updated = await PmCommand(sam, HttpMethod.Put, path, etag, ScheduleUpdate(due.AddDays(5), title: "Deep clean", interval: 14, lead: 2, priority: "P1"));
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
        var next = ETagOf(updated);
        Assert.NotEqual(etag, next);
        var detail = await ScheduleDetail(sam, id);
        Assert.Equal("Deep clean", detail.GetProperty("title").GetString());
        Assert.Equal(14, detail.GetProperty("intervalDays").GetInt32());
        Assert.Equal("P1", detail.GetProperty("priority").GetString());
        Assert.Equal(Iso(due.AddDays(5)), detail.GetProperty("nextDueOn").GetString());

        // The old ETag is now stale.
        await AssertProblem(await PmCommand(sam, HttpMethod.Put, path, etag, ScheduleUpdate(due)), HttpStatusCode.PreconditionFailed);
        // The new one chains without a GET.
        Assert.Equal(HttpStatusCode.NoContent, (await PmCommand(sam, HttpMethod.Put, path, next, ScheduleUpdate(due, title: "Deep clean"))).StatusCode);
        // A business-rule violation with a fresh ETag is a 400, not a 412.
        var fresh = ETagOf(await sam.GetAsync(path, Ct));
        await AssertProblem(await PmCommand(sam, HttpMethod.Put, path, fresh, ScheduleUpdate(due, interval: 0)), HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task Deactivate_and_activate_need_a_current_etag_and_chain()
    {
        var assetId = await RegisterAsset();
        var (id, etag) = await CreateSchedule(assetId, FactoryToday().AddDays(200), lead: 0);
        using var sam = Sam();
        var deactivate = $"/api/pm-schedules/{id}/deactivate";
        var activate = $"/api/pm-schedules/{id}/activate";

        await AssertProblem(await PmCommand(sam, HttpMethod.Post, deactivate, etag: null), HttpStatusCode.PreconditionRequired);

        var off = await PmCommand(sam, HttpMethod.Post, deactivate, etag);
        Assert.Equal(HttpStatusCode.NoContent, off.StatusCode);
        var offEtag = ETagOf(off);
        Assert.False((await ScheduleDetail(sam, id)).GetProperty("isActive").GetBoolean());

        await AssertProblem(await PmCommand(sam, HttpMethod.Post, deactivate, etag), HttpStatusCode.PreconditionFailed);
        await AssertProblem(await PmCommand(sam, HttpMethod.Post, deactivate, offEtag), HttpStatusCode.BadRequest); // already inactive

        var on = await PmCommand(sam, HttpMethod.Post, activate, offEtag);
        Assert.Equal(HttpStatusCode.NoContent, on.StatusCode);
        Assert.True((await ScheduleDetail(sam, id)).GetProperty("isActive").GetBoolean());
        await AssertProblem(await PmCommand(sam, HttpMethod.Post, $"/api/pm-schedules/{Guid.NewGuid()}/activate", ETagOf(on)), HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task Only_supervisors_may_change_schedules()
    {
        var assetId = await RegisterAsset();
        var due = FactoryToday().AddDays(200);
        var (id, etag) = await CreateSchedule(assetId, due, lead: 0);

        foreach (var client in new[] { Tom(), Oscar() })
        {
            using (client)
            {
                Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/pm-schedules", ScheduleBody(assetId, due), Ct)).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await PmCommand(client, HttpMethod.Put, $"/api/pm-schedules/{id}", etag, ScheduleUpdate(due))).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await PmCommand(client, HttpMethod.Post, $"/api/pm-schedules/{id}/deactivate", etag)).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await PmCommand(client, HttpMethod.Post, $"/api/pm-schedules/{id}/activate", etag)).StatusCode);
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/pm-schedules/{id}", Ct)).StatusCode);
            }
        }

        using var ada = Ada(); // admins hold the supervise policy too
        Assert.Equal(HttpStatusCode.NoContent, (await PmCommand(ada, HttpMethod.Post, $"/api/pm-schedules/{id}/deactivate", etag)).StatusCode);
    }

    [IntegrationFact]
    public async Task Anonymous_requests_to_pm_schedules_get_401()
    {
        using var anonymous = fixture.Factory.CreateClient().WithCsrf();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/pm-schedules", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/pm-schedules", new { }, Ct)).StatusCode);
    }
}
