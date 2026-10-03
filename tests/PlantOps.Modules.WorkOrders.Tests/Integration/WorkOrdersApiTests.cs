using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using PlantOps.Api.Tests;
using PlantOps.Modules.Assets.Infrastructure;
using PlantOps.Modules.Assets.Tests.Integration;
using PlantOps.Modules.Identity.Contracts;

namespace PlantOps.Modules.WorkOrders.Tests.Integration;

// All tests share one database, so each builds its own asset (unique tag) and filters on it.
[Collection(WorkOrdersCollection.Name)]
public partial class WorkOrdersApiTests(WorkOrdersFixture fixture)
{
    private static readonly Guid Smt1 = ProductionLineConfiguration.Smt1.Value;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // The seeded directory (see the fixture): tom and lee are technicians, sam a supervisor, ada an admin.
    private HttpClient As(string name, params string[] roles) => fixture.Factory.CreateClient().AsUser(name, roles).WithCsrf();

    private HttpClient Oscar() => As("Oscar", Roles.Operator);

    private HttpClient Sam() => As("Sam", Roles.Supervisor);

    private HttpClient Tom() => As("Tom", Roles.Technician);

    private HttpClient Lee() => As("Lee", Roles.Technician);

    private HttpClient Ada() => As("Ada", Roles.Admin);

    [GeneratedRegex(@"^WO-\d{6}$")]
    private static partial Regex NumberPattern();

    private static string NewPrefix() => "W" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private async Task<Guid> RegisterAsset()
    {
        using var sam = Sam();
        var response = await sam.PostAsJsonAsync("/api/assets", new
        {
            tag = NewPrefix() + "-01",
            name = "Pick and place",
            manufacturer = "Fuji",
            model = "NXT III",
            serialNumber = (string?)null,
            lineId = Smt1,
            station = "Station 1",
            criticality = "A",
            commissionedOn = "2024-03-01",
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadJson(response)).GetProperty("id").GetGuid();
    }

    private static string ETagOf(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues("ETag", out var values), "response has no ETag header");
        return Assert.Single(values);
    }

    // Raises as the given client and returns the new work order's id and ETag.
    private async Task<(Guid Id, string ETag)> Raise(HttpClient client, Guid assetId, string priority = "P2", bool assetDown = true)
    {
        var response = await client.PostAsJsonAsync("/api/work-orders", new
        {
            assetId,
            title = "Feeder 4 jams",
            description = "Jams every few minutes.",
            priority,
            assetDown,
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var etag = ETagOf(response);
        var body = await ReadJson(response);
        var id = body.GetProperty("id").GetGuid();
        Assert.Equal($"/api/work-orders/{id}", response.Headers.Location?.ToString());
        return (id, etag);
    }

    private static Task<HttpResponseMessage> Command(HttpClient client, Guid id, string action, string? etag, object? body = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/work-orders/{id}/{action}");
        if (etag is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", etag);
        }

        // Always a JSON body (possibly "{}"): endpoints that bind a body only match application/json requests,
        // exactly like the SPA's HttpClient calls.
        request.Content = JsonContent.Create(body ?? new { });

        return client.SendAsync(request, Ct);
    }

    // Runs a command that must succeed and returns the ETag it handed back.
    private static async Task<string> Succeed(HttpClient client, Guid id, string action, string etag, object? body = null)
    {
        var response = await Command(client, id, action, etag, body);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var next = ETagOf(response);
        Assert.NotEqual(etag, next);
        return next;
    }

    private static async Task<JsonElement> AssertProblem(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await ReadJson(response);
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
        return problem;
    }

    private static async Task<JsonElement> Detail(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"/api/work-orders/{id}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJson(response);
    }

    private static string[] Actions(JsonElement detail) =>
        [.. detail.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()!)];

    [IntegrationFact]
    public async Task Full_lifecycle_works_and_the_history_records_each_step_by_the_right_person()
    {
        var assetId = await RegisterAsset();
        using var oscar = Oscar();
        using var sam = Sam();
        using var tom = Tom();

        var (id, etag) = await Raise(oscar, assetId);
        etag = await Succeed(sam, id, "approve", etag);
        etag = await Succeed(sam, id, "assign", etag, new { technicianId = "tom" });
        etag = await Succeed(tom, id, "start", etag);
        etag = await Succeed(tom, id, "complete", etag, new { resolution = "Replaced the feeder spring." });
        etag = await Succeed(sam, id, "close", etag);

        var detailResponse = await sam.GetAsync($"/api/work-orders/{id}", Ct);
        Assert.Equal(etag, ETagOf(detailResponse));
        var detail = await ReadJson(detailResponse);
        Assert.Matches(NumberPattern(), detail.GetProperty("number").GetString()!);
        Assert.Equal("Closed", detail.GetProperty("status").GetString());
        Assert.Equal("Met", detail.GetProperty("slaState").GetString());
        Assert.Equal("Oscar", detail.GetProperty("reportedBy").GetProperty("name").GetString());
        Assert.Equal("Sam", detail.GetProperty("approvedBy").GetProperty("name").GetString());
        Assert.Equal("Tom", detail.GetProperty("assignedTo").GetProperty("name").GetString());
        Assert.Equal("Replaced the feeder spring.", detail.GetProperty("resolution").GetString());
        Assert.Empty(Actions(detail));

        var history = (await sam.GetFromJsonAsync<JsonElement>($"/api/work-orders/{id}/history", Ct)).EnumerateArray().ToList();
        // Newest first.
        Assert.Equal(
            ["WorkOrderClosed", "WorkOrderCompleted", "WorkOrderStarted", "WorkOrderAssigned", "WorkOrderApproved", "WorkOrderSubmitted"],
            history.Select(e => e.GetProperty("eventType").GetString()));
        Assert.Equal(
            ["Sam", "Tom", "Tom", "Sam", "Sam", "Oscar"],
            history.Select(e => e.GetProperty("actorName").GetString()));
        var times = history.Select(e => e.GetProperty("occurredAt").GetDateTimeOffset()).ToList();
        Assert.Equal(times.OrderByDescending(t => t), times);
        var assigned = history[3].GetProperty("payload");
        Assert.Equal("tom", assigned.GetProperty("technicianId").GetString());
        Assert.Equal(JsonValueKind.Null, assigned.GetProperty("previousTechnicianId").ValueKind);
        Assert.Equal("Replaced the feeder spring.", history[1].GetProperty("payload").GetProperty("resolution").GetString());
    }

    [IntegrationFact]
    public async Task Two_commands_with_the_same_etag_one_wins_and_one_gets_412()
    {
        var assetId = await RegisterAsset();
        using var oscar = Oscar();
        using var sam = Sam();
        using var ada = Ada();
        var (id, etag) = await Raise(oscar, assetId);
        etag = await Succeed(sam, id, "approve", etag);

        var tomTask = Command(sam, id, "assign", etag, new { technicianId = "tom" });
        var leeTask = Command(ada, id, "assign", etag, new { technicianId = "lee" });
        var responses = await Task.WhenAll(tomTask, leeTask);

        Assert.Equal(
            [HttpStatusCode.NoContent, HttpStatusCode.PreconditionFailed],
            responses.Select(r => r.StatusCode).Order());
        var loser = responses.Single(r => r.StatusCode == HttpStatusCode.PreconditionFailed);
        await AssertProblem(loser, HttpStatusCode.PreconditionFailed);
        var winnerIsTom = responses[0].StatusCode == HttpStatusCode.NoContent;
        var detail = await Detail(sam, id);
        Assert.Equal(winnerIsTom ? "Tom" : "Lee", detail.GetProperty("assignedTo").GetProperty("name").GetString());
    }

    [IntegrationFact]
    public async Task A_stale_etag_gets_412_even_when_the_command_would_also_break_a_business_rule()
    {
        var assetId = await RegisterAsset();
        using var oscar = Oscar();
        using var sam = Sam();
        var (id, staleEtag) = await Raise(oscar, assetId);
        await Succeed(sam, id, "approve", staleEtag);

        // Approving again is illegal (already Approved), but the client is out of date, so "reload" comes first.
        var response = await Command(sam, id, "approve", staleEtag);

        await AssertProblem(response, HttpStatusCode.PreconditionFailed);
    }

    [IntegrationFact]
    public async Task Command_without_if_match_returns_428()
    {
        var assetId = await RegisterAsset();
        using var oscar = Oscar();
        using var sam = Sam();
        var (id, _) = await Raise(oscar, assetId);

        var response = await Command(sam, id, "approve", etag: null);

        await AssertProblem(response, HttpStatusCode.PreconditionRequired);
        Assert.Equal("Submitted", (await Detail(sam, id)).GetProperty("status").GetString());
    }

    [IntegrationFact]
    public async Task Command_with_a_malformed_if_match_returns_400()
    {
        var assetId = await RegisterAsset();
        using var oscar = Oscar();
        using var sam = Sam();
        var (id, _) = await Raise(oscar, assetId);

        var response = await Command(sam, id, "approve", "not-an-etag");

        await AssertProblem(response, HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task Command_on_an_unknown_work_order_returns_404()
    {
        using var sam = Sam();

        var response = await Command(sam, Guid.NewGuid(), "approve", "\"AAAAAAAAB9E=\"");

        await AssertProblem(response, HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task The_etag_changes_after_every_command_and_matches_the_next_get()
    {
        var assetId = await RegisterAsset();
        using var oscar = Oscar();
        using var sam = Sam();
        var (id, created) = await Raise(oscar, assetId);

        var approved = await Succeed(sam, id, "approve", created);
        var assigned = await Succeed(sam, id, "assign", approved, new { technicianId = "tom" });
        var reassigned = await Succeed(sam, id, "assign", assigned, new { technicianId = "lee" });

        Assert.Equal(4, new[] { created, approved, assigned, reassigned }.Distinct().Count());
        var get = await sam.GetAsync($"/api/work-orders/{id}", Ct);
        Assert.Equal(reassigned, ETagOf(get));
        Assert.Equal("Lee", (await ReadJson(get)).GetProperty("assignedTo").GetProperty("name").GetString());
    }

    [IntegrationFact]
    public async Task Technician_who_is_not_assigned_cannot_start_but_the_assigned_one_and_an_admin_can()
    {
        var assetId = await RegisterAsset();
        using var oscar = Oscar();
        using var sam = Sam();
        using var tom = Tom();
        using var lee = Lee();
        using var ada = Ada();
        var (id, etag) = await Raise(oscar, assetId);
        etag = await Succeed(sam, id, "approve", etag);
        etag = await Succeed(sam, id, "assign", etag, new { technicianId = "tom" });

        var forbidden = await Command(lee, id, "start", etag);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal("Assigned", (await Detail(sam, id)).GetProperty("status").GetString());

        // Tom is assigned; an admin may also work any order.
        var other = await RaiseAssigned(assetId, "tom");
        await Succeed(ada, other.Id, "start", other.ETag);
        await Succeed(tom, id, "start", etag);
    }

    private async Task<(Guid Id, string ETag)> RaiseAssigned(Guid assetId, string technicianId)
    {
        using var oscar = Oscar();
        using var sam = Sam();
        var (id, etag) = await Raise(oscar, assetId);
        etag = await Succeed(sam, id, "approve", etag);
        etag = await Succeed(sam, id, "assign", etag, new { technicianId });
        return (id, etag);
    }

    [IntegrationFact]
    public async Task Operator_cannot_approve()
    {
        var assetId = await RegisterAsset();
        using var oscar = Oscar();
        var (id, etag) = await Raise(oscar, assetId);

        var response = await Command(oscar, id, "approve", etag);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [IntegrationFact]
    public async Task Technician_cannot_assign_or_close()
    {
        var assetId = await RegisterAsset();
        using var tom = Tom();
        var (id, etag) = await RaiseAssigned(assetId, "tom");

        Assert.Equal(HttpStatusCode.Forbidden, (await Command(tom, id, "assign", etag, new { technicianId = "lee" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Command(tom, id, "close", etag)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Command(tom, id, "cancel", etag, new { reason = "x" })).StatusCode);
    }

    [IntegrationFact]
    public async Task Raising_on_a_decommissioned_asset_returns_400()
    {
        var assetId = await RegisterAsset();
        using var sam = Sam();
        using var oscar = Oscar();
        var decommission = await sam.PostAsJsonAsync($"/api/assets/{assetId}/decommission", new { on = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"), reason = "Scrapped" }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, decommission.StatusCode);

        var response = await oscar.PostAsJsonAsync("/api/work-orders", new { assetId, title = "Broken", description = "", priority = "P3", assetDown = false }, Ct);

        var problem = await AssertProblem(response, HttpStatusCode.BadRequest);
        Assert.Contains("decommissioned", problem.GetProperty("detail").GetString());
    }

    [IntegrationFact]
    public async Task Raising_on_an_unknown_asset_returns_400()
    {
        using var oscar = Oscar();

        var response = await oscar.PostAsJsonAsync("/api/work-orders", new { assetId = Guid.NewGuid(), title = "Broken", description = "", priority = "P3", assetDown = false }, Ct);

        await AssertProblem(response, HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task Raising_with_a_blank_title_returns_400()
    {
        var assetId = await RegisterAsset();
        using var oscar = Oscar();

        var response = await oscar.PostAsJsonAsync("/api/work-orders", new { assetId, title = " ", description = "", priority = "P3", assetDown = false }, Ct);

        await AssertProblem(response, HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task Assigning_a_non_technician_or_an_unknown_user_returns_400()
    {
        var assetId = await RegisterAsset();
        using var oscar = Oscar();
        using var sam = Sam();
        var (id, etag) = await Raise(oscar, assetId);
        etag = await Succeed(sam, id, "approve", etag);

        // Olivia exists but is an operator; "ghost" never signed in.
        await AssertProblem(await Command(sam, id, "assign", etag, new { technicianId = "olivia" }), HttpStatusCode.BadRequest);
        await AssertProblem(await Command(sam, id, "assign", etag, new { technicianId = "ghost" }), HttpStatusCode.BadRequest);
        Assert.Equal("Approved", (await Detail(sam, id)).GetProperty("status").GetString());
    }

    [IntegrationFact]
    public async Task Illegal_transition_returns_400_naming_the_transition()
    {
        var assetId = await RegisterAsset();
        using var oscar = Oscar();
        using var sam = Sam();
        var (id, etag) = await Raise(oscar, assetId);

        var response = await Command(sam, id, "close", etag);

        var problem = await AssertProblem(response, HttpStatusCode.BadRequest);
        Assert.Contains("Cannot close", problem.GetProperty("detail").GetString());
    }

    [IntegrationFact]
    public async Task Reject_and_cancel_need_a_reason_and_end_the_work_order()
    {
        var assetId = await RegisterAsset();
        using var oscar = Oscar();
        using var sam = Sam();
        var (rejectedId, rejectEtag) = await Raise(oscar, assetId);
        var (cancelledId, cancelEtag) = await Raise(oscar, assetId);

        await AssertProblem(await Command(sam, rejectedId, "reject", rejectEtag, new { reason = " " }), HttpStatusCode.BadRequest);
        await Succeed(sam, rejectedId, "reject", rejectEtag, new { reason = "Duplicate" });
        await Succeed(sam, cancelledId, "cancel", cancelEtag, new { reason = "Machine scrapped" });

        var rejected = await Detail(sam, rejectedId);
        Assert.Equal("Rejected", rejected.GetProperty("status").GetString());
        Assert.Equal("Duplicate", rejected.GetProperty("rejectionReason").GetString());
        Assert.Equal(JsonValueKind.Null, rejected.GetProperty("slaState").ValueKind);
        var cancelled = await Detail(sam, cancelledId);
        Assert.Equal("Cancelled", cancelled.GetProperty("status").GetString());
        Assert.Equal("Machine scrapped", cancelled.GetProperty("cancellationReason").GetString());
    }

    [IntegrationFact]
    public async Task Approve_can_retriage_the_priority()
    {
        var assetId = await RegisterAsset();
        using var oscar = Oscar();
        using var sam = Sam();
        var (id, etag) = await Raise(oscar, assetId, priority: "P4");
        var before = await Detail(sam, id);

        await Succeed(sam, id, "approve", etag, new { priority = "P1" });

        var after = await Detail(sam, id);
        Assert.Equal("P1", after.GetProperty("priority").GetString());
        Assert.True(after.GetProperty("dueAt").GetDateTimeOffset() < before.GetProperty("dueAt").GetDateTimeOffset());
    }

    [IntegrationFact]
    public async Task Allowed_actions_depend_on_status_role_and_assignment()
    {
        var assetId = await RegisterAsset();
        using var oscar = Oscar();
        using var sam = Sam();
        using var tom = Tom();
        using var lee = Lee();
        var (id, etag) = await Raise(oscar, assetId);

        Assert.Equal(["approve", "reject", "cancel"], Actions(await Detail(sam, id)));
        Assert.Empty(Actions(await Detail(oscar, id)));

        etag = await Succeed(sam, id, "approve", etag);
        Assert.Equal(["assign", "cancel"], Actions(await Detail(sam, id)));

        await Succeed(sam, id, "assign", etag, new { technicianId = "tom" });
        Assert.Equal(["assign", "cancel"], Actions(await Detail(sam, id)));
        Assert.Equal(["start"], Actions(await Detail(tom, id)));
        Assert.Empty(Actions(await Detail(lee, id)));
    }

    [IntegrationFact]
    public async Task List_mine_returns_only_work_assigned_to_the_current_user()
    {
        var assetId = await RegisterAsset();
        await RaiseAssigned(assetId, "tom");
        await RaiseAssigned(assetId, "tom");
        await RaiseAssigned(assetId, "lee");
        using var oscar = Oscar();
        await Raise(oscar, assetId);
        using var tom = Tom();

        var mine = await tom.GetFromJsonAsync<JsonElement>($"/api/work-orders?mine=true&assetId={assetId}", Ct);
        var all = await tom.GetFromJsonAsync<JsonElement>($"/api/work-orders?assetId={assetId}", Ct);

        Assert.Equal(2, mine.GetProperty("totalCount").GetInt32());
        Assert.All(mine.GetProperty("items").EnumerateArray(), i => Assert.Equal("tom", i.GetProperty("assignedTo").GetProperty("id").GetString()));
        Assert.Equal(4, all.GetProperty("totalCount").GetInt32());
    }

    [IntegrationFact]
    public async Task List_filters_by_status_and_priority_pages_and_includes_number_and_sla_state()
    {
        var assetId = await RegisterAsset();
        using var oscar = Oscar();
        using var sam = Sam();
        var (approvedId, etag) = await Raise(oscar, assetId, "P1");
        await Succeed(sam, approvedId, "approve", etag);
        await Raise(oscar, assetId, "P3");
        await Raise(oscar, assetId, "P3");

        var submitted = await sam.GetFromJsonAsync<JsonElement>($"/api/work-orders?assetId={assetId}&status=Submitted", Ct);
        var p1 = await sam.GetFromJsonAsync<JsonElement>($"/api/work-orders?assetId={assetId}&priority=P1", Ct);
        var page = await sam.GetFromJsonAsync<JsonElement>($"/api/work-orders?assetId={assetId}&page=2&pageSize=2", Ct);

        Assert.Equal(2, submitted.GetProperty("totalCount").GetInt32());
        Assert.All(submitted.GetProperty("items").EnumerateArray(), i => Assert.Equal("Submitted", i.GetProperty("status").GetString()));
        var onlyP1 = Assert.Single(p1.GetProperty("items").EnumerateArray());
        Assert.Equal(approvedId, onlyP1.GetProperty("id").GetGuid());
        Assert.Matches(NumberPattern(), onlyP1.GetProperty("number").GetString()!);
        Assert.Equal("OnTrack", onlyP1.GetProperty("slaState").GetString());
        Assert.Equal(3, page.GetProperty("totalCount").GetInt32());
        Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal(2, page.GetProperty("page").GetInt32());
    }

    [IntegrationFact]
    public async Task List_is_newest_first()
    {
        var assetId = await RegisterAsset();
        using var oscar = Oscar();
        var (first, _) = await Raise(oscar, assetId);
        var (second, _) = await Raise(oscar, assetId);

        var list = await oscar.GetFromJsonAsync<JsonElement>($"/api/work-orders?assetId={assetId}", Ct);

        Assert.Equal([second, first], list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()));
    }

    [IntegrationFact]
    public async Task Work_order_numbers_come_from_the_sequence_and_increase()
    {
        var assetId = await RegisterAsset();
        using var oscar = Oscar();
        var (first, _) = await Raise(oscar, assetId);
        var (second, _) = await Raise(oscar, assetId);

        var a = (await Detail(oscar, first)).GetProperty("number").GetString()!;
        var b = (await Detail(oscar, second)).GetProperty("number").GetString()!;

        Assert.Matches(NumberPattern(), a);
        Assert.True(string.CompareOrdinal(b, a) > 0, $"{b} should be after {a}");
    }

    [IntegrationFact]
    public async Task Get_and_history_of_an_unknown_work_order_return_404()
    {
        using var sam = Sam();

        await AssertProblem(await sam.GetAsync($"/api/work-orders/{Guid.NewGuid()}", Ct), HttpStatusCode.NotFound);
        await AssertProblem(await sam.GetAsync($"/api/work-orders/{Guid.NewGuid()}/history", Ct), HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task Anonymous_requests_get_401()
    {
        using var anonymous = fixture.Factory.CreateClient().WithCsrf();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/work-orders", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/work-orders", new { }, Ct)).StatusCode);
    }

    [IntegrationFact]
    public async Task Identity_users_lists_technicians_for_supervisors_only()
    {
        using var sam = Sam();
        using var oscar = Oscar();

        var technicians = await sam.GetFromJsonAsync<JsonElement>("/api/identity/users?role=technician", Ct);
        var ids = technicians.EnumerateArray().Select(u => u.GetProperty("id").GetString()).ToList();

        Assert.Contains("tom", ids);
        Assert.Contains("lee", ids);
        Assert.Contains("ada", ids);
        Assert.DoesNotContain("olivia", ids);
        Assert.DoesNotContain("sam", ids);
        Assert.Equal(HttpStatusCode.Forbidden, (await oscar.GetAsync("/api/identity/users?role=technician", Ct)).StatusCode);
        await AssertProblem(await sam.GetAsync("/api/identity/users", Ct), HttpStatusCode.BadRequest);
    }
}
