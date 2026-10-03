using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlantOps.Api.Tests;
using PlantOps.BuildingBlocks.Infrastructure;
using PlantOps.Modules.Assets.Infrastructure;
using PlantOps.Modules.Assets.Tests.Integration;
using PlantOps.Modules.Identity.Contracts;
using PlantOps.Modules.Inventory.Tests.Integration;
using PlantOps.Modules.Reporting.Domain;
using PlantOps.Modules.Reporting.Infrastructure;
using PlantOps.Modules.WorkOrders.Infrastructure;

namespace PlantOps.Modules.Reporting.Tests.Integration;

// All tests share one database. Exact-number tests therefore seed facts in a calendar window no other test uses
// (2021, 2023) and query only that window; tests that go through the real API assert on their own asset.
[Collection(InventoryCollection.Name)]
public class ReportsApiTests(InventoryFixture fixture)
{
    private static readonly Guid Smt1 = ProductionLineConfiguration.Smt1.Value;
    private static readonly TimeZoneInfo Penang = FactoryClock.FindZone(null);
    private const string Formula = "=HYPERLINK(\"http://evil.example\",\"click\")";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient As(string name, params string[] roles) => fixture.Factory.CreateClient().AsUser(name, roles).WithCsrf();

    private HttpClient Sam() => As("Sam", Roles.Supervisor);

    private HttpClient Ada() => As("Ada", Roles.Admin, Roles.Technician);

    private HttpClient Tom() => As("Tom", Roles.Technician);

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private static string ETagOf(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues("ETag", out var values), "response has no ETag header");
        return Assert.Single(values);
    }

    // ---- arrange: real work orders through the API ----

    private async Task<(Guid Id, string Tag)> RegisterAsset()
    {
        using var sam = Sam();
        var tag = "R" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant() + "-01";
        var response = await sam.PostAsJsonAsync("/api/assets", new
        {
            tag,
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
        return ((await ReadJson(response)).GetProperty("id").GetGuid(), tag);
    }

    private static async Task<string> Succeed(HttpClient client, Guid id, string action, string etag, object? body = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/work-orders/{id}/{action}");
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        // Always a JSON body (possibly "{}"): endpoints that bind a body only match application/json requests.
        request.Content = JsonContent.Create(body ?? new { });
        var response = await client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return ETagOf(response);
    }

    // Raise -> approve -> assign -> start -> complete, as Oscar, Sam and Tom.
    private async Task<Guid> CompleteWorkOrder(Guid assetId, string priority = "P2", bool assetDown = true)
    {
        using var oscar = As("Oscar", Roles.Operator);
        using var sam = Sam();
        using var tom = Tom();
        var raised = await oscar.PostAsJsonAsync("/api/work-orders", new
        {
            assetId,
            title = "Feeder 4 jams",
            description = "Jams every few minutes.",
            priority,
            assetDown,
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, raised.StatusCode);
        var etag = ETagOf(raised);
        var id = (await ReadJson(raised)).GetProperty("id").GetGuid();

        etag = await Succeed(sam, id, "approve", etag);
        etag = await Succeed(sam, id, "assign", etag, new { technicianId = "tom" });
        etag = await Succeed(tom, id, "start", etag);
        await Succeed(tom, id, "complete", etag, new { resolution = "Replaced the feeder spring." });
        return id;
    }

    // ---- arrange: facts seeded directly, for exact aggregates ----

    private static WorkOrderFact Fact(
        Guid? lineId,
        string lineName,
        string priority,
        DateTimeOffset completed,
        int repairMinutes,
        int? downtimeMinutes,
        bool metSla,
        string assetTag = "SEED-01") =>
        WorkOrderFact.Project(
            new CompletedWork(
                Guid.NewGuid(),
                "WO-900001",
                Guid.NewGuid(),
                priority,
                "Reactive",
                completed.AddMinutes(-(downtimeMinutes ?? repairMinutes)),
                completed.AddMinutes(-repairMinutes),
                completed,
                metSla ? completed : completed.AddMinutes(-1),
                downtimeMinutes is not null),
            new AssetRef(assetTag, lineId, lineName),
            Penang);

    private async Task Seed(params WorkOrderFact[] facts)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        db.WorkOrderFacts.AddRange(facts);
        await db.SaveChangesAsync(Ct);
    }

    private async Task<JsonElement> Report(HttpClient client, string path)
    {
        var response = await client.GetAsync(path, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJson(response);
    }

    private static JsonElement[] Rows(JsonElement report) => [.. report.GetProperty("rows").EnumerateArray()];

    private static async Task AssertBadRequest(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        await ReadJson(response);
    }

    // ---- the flow: complete -> outbox -> projection -> report ----

    [IntegrationFact]
    public async Task A_completed_work_order_appears_in_the_reports_only_after_the_outbox_is_delivered()
    {
        var (assetId, tag) = await RegisterAsset();
        using var sam = Sam();
        await CompleteWorkOrder(assetId, priority: "P1", assetDown: true);

        var before = Rows(await Report(sam, "/api/reports/mttr?groupBy=asset"));
        Assert.DoesNotContain(before, r => r.GetProperty("key").GetString() == assetId.ToString());

        await fixture.DrainWorkOrdersOutboxAsync(Ct);

        var byAsset = Rows(await Report(sam, "/api/reports/mttr?groupBy=asset"));
        var row = Assert.Single(byAsset, r => r.GetProperty("key").GetString() == assetId.ToString());
        Assert.Equal(tag, row.GetProperty("label").GetString());
        Assert.Equal(1, row.GetProperty("workOrders").GetInt32());
        Assert.True(row.GetProperty("meanRepairMinutes").GetDouble() >= 0);

        // The line was resolved from the Assets directory at completion time.
        var byLine = Rows(await Report(sam, "/api/reports/mttr?groupBy=line"));
        var line = Assert.Single(byLine, r => r.GetProperty("key").GetString() == Smt1.ToString());
        Assert.Equal("SMT Line 1", line.GetProperty("label").GetString());

        var downtime = Rows(await Report(sam, "/api/reports/downtime?groupBy=line"));
        Assert.True(Assert.Single(downtime, r => r.GetProperty("key").GetString() == Smt1.ToString()).GetProperty("events").GetInt32() >= 1);

        var sla = Rows(await Report(sam, "/api/reports/sla-compliance?groupBy=priority"));
        Assert.True(Assert.Single(sla, r => r.GetProperty("key").GetString() == "P1").GetProperty("completed").GetInt32() >= 1);
    }

    [IntegrationFact]
    public async Task Delivering_the_completion_message_twice_projects_one_fact()
    {
        var (assetId, _) = await RegisterAsset();
        using var sam = Sam();
        var workOrderId = await CompleteWorkOrder(assetId);
        await fixture.DrainWorkOrdersOutboxAsync(Ct);

        // Simulate the crash window: the handlers committed, but the outbox row was never marked processed.
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkOrdersDbContext>();
            var marker = workOrderId.ToString();
            var reset = await db.Set<OutboxMessage>()
                .Where(m => m.Payload.Contains(marker))
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.ProcessedAt, (DateTimeOffset?)null), Ct);
            Assert.True(reset >= 1, "the completion message should have been processed once already");
        }

        var redelivered = await fixture.DrainWorkOrdersOutboxAsync(Ct);

        Assert.True(redelivered >= 1, "the reset message should have been delivered again");
        var row = Assert.Single(Rows(await Report(sam, "/api/reports/mttr?groupBy=asset")), r => r.GetProperty("key").GetString() == assetId.ToString());
        Assert.Equal(1, row.GetProperty("workOrders").GetInt32());

        await using var check = fixture.Factory.Services.CreateAsyncScope();
        var reporting = check.ServiceProvider.GetRequiredService<ReportingDbContext>();
        Assert.Equal(1, await reporting.WorkOrderFacts.CountAsync(f => f.WorkOrderId == workOrderId, Ct));
    }

    // ---- exact aggregates over seeded facts ----

    [IntegrationFact]
    public async Task The_reports_aggregate_exactly_over_a_half_open_factory_local_range()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var nameA = "AAA line " + a.ToString("N")[..6];
        var nameB = "BBB line " + b.ToString("N")[..6];
        static DateTimeOffset Utc(int y, int m, int d, int h, int mi) => new(y, m, d, h, mi, 0, TimeSpan.Zero);

        await Seed(
            // 2020-12-31T16:00Z is 00:00 on 1 Jan in Penang: the first instant of the range, so INCLUDED (January).
            Fact(a, nameA, "P1", Utc(2020, 12, 31, 16, 0), repairMinutes: 60, downtimeMinutes: 120, metSla: true),
            // 17:00Z on 31 Jan is 01:00 on 1 Feb in Penang: February.
            Fact(a, nameA, "P1", Utc(2021, 1, 31, 17, 0), repairMinutes: 90, downtimeMinutes: null, metSla: false),
            Fact(b, nameB, "P2", Utc(2021, 2, 10, 4, 0), repairMinutes: 45, downtimeMinutes: 30, metSla: true),
            // 15:59Z on 31 Mar is 23:59 in Penang: March, the last minute of the range.
            Fact(b, nameB, "P2", Utc(2021, 3, 31, 15, 59), repairMinutes: 100, downtimeMinutes: null, metSla: true),
            // 16:00Z on 31 Mar is 00:00 on 1 Apr in Penang: the exclusive end, so NOT in the range.
            Fact(b, nameB, "P1", Utc(2021, 3, 31, 16, 0), repairMinutes: 999, downtimeMinutes: 999, metSla: false),
            // 15:59Z on 31 Dec 2020 is the minute before the range starts.
            Fact(b, nameB, "P1", Utc(2020, 12, 31, 15, 59), repairMinutes: 999, downtimeMinutes: 999, metSla: false));
        using var sam = Sam();
        const string Window = "from=2021-01-01&to=2021-04-01";

        var mttrLine = await Report(sam, $"/api/reports/mttr?{Window}&groupBy=line");
        Assert.Equal("2021-01-01", mttrLine.GetProperty("from").GetString());
        Assert.Equal("2021-04-01", mttrLine.GetProperty("to").GetString());
        Assert.Equal("line", mttrLine.GetProperty("groupBy").GetString());
        var lines = Rows(mttrLine);
        Assert.Equal(2, lines.Length);
        Assert.Equal((a.ToString(), nameA, 2, 75.0), Mttr(lines[0]));
        Assert.Equal((b.ToString(), nameB, 2, 72.5), Mttr(lines[1]));

        var mttrMonth = Rows(await Report(sam, $"/api/reports/mttr?{Window}&groupBy=month"));
        Assert.Equal(
            [("2021-01", "Jan 2021", 1, 60.0), ("2021-02", "Feb 2021", 2, 67.5), ("2021-03", "Mar 2021", 1, 100.0)],
            mttrMonth.Select(Mttr));

        var sla = Rows(await Report(sam, $"/api/reports/sla-compliance?{Window}&groupBy=priority"));
        Assert.Equal(
            [("P1", "P1 Critical", 2, 1, 50.0), ("P2", "P2 High", 2, 2, 100.0)],
            sla.Select(Sla));
        var slaMonth = Rows(await Report(sam, $"/api/reports/sla-compliance?{Window}&groupBy=month"));
        Assert.Equal(
            [("2021-01", "Jan 2021", 1, 1, 100.0), ("2021-02", "Feb 2021", 2, 1, 50.0), ("2021-03", "Mar 2021", 1, 1, 100.0)],
            slaMonth.Select(Sla));

        // Downtime counts only work that took the asset down; null is not an event.
        var downtime = Rows(await Report(sam, $"/api/reports/downtime?{Window}&groupBy=line"));
        Assert.Equal(
            [(a.ToString(), nameA, 1, 120), (b.ToString(), nameB, 1, 30)],
            downtime.Select(Down));
        var downtimeMonth = Rows(await Report(sam, $"/api/reports/downtime?{Window}&groupBy=month"));
        Assert.Equal(
            [("2021-01", "Jan 2021", 1, 120), ("2021-02", "Feb 2021", 1, 30)],
            downtimeMonth.Select(Down));
    }

    private static (string, string, int, double) Mttr(JsonElement r) =>
        (r.GetProperty("key").GetString()!, r.GetProperty("label").GetString()!, r.GetProperty("workOrders").GetInt32(), r.GetProperty("meanRepairMinutes").GetDouble());

    private static (string, string, int, int, double) Sla(JsonElement r) =>
        (r.GetProperty("key").GetString()!, r.GetProperty("label").GetString()!, r.GetProperty("completed").GetInt32(), r.GetProperty("metSla").GetInt32(), r.GetProperty("compliancePercent").GetDouble());

    private static (string, string, int, int) Down(JsonElement r) =>
        (r.GetProperty("key").GetString()!, r.GetProperty("label").GetString()!, r.GetProperty("events").GetInt32(), r.GetProperty("downtimeMinutes").GetInt32());

    [IntegrationFact]
    public async Task Without_dates_the_range_is_the_last_90_days_ending_tomorrow_in_factory_time()
    {
        using var sam = Sam();
        var today = fixture.Factory.Services.GetRequiredService<FactoryClock>().Today;

        var report = await Report(sam, "/api/reports/mttr");

        Assert.Equal(today.AddDays(1).ToString("yyyy-MM-dd"), report.GetProperty("to").GetString());
        Assert.Equal(today.AddDays(1 - 90).ToString("yyyy-MM-dd"), report.GetProperty("from").GetString());
        Assert.Equal("line", report.GetProperty("groupBy").GetString());
    }

    // ---- validation ----

    [IntegrationFact]
    public async Task Invalid_group_by_and_ranges_are_400_problems()
    {
        using var sam = Sam();

        await AssertBadRequest(await sam.GetAsync("/api/reports/mttr?groupBy=bogus", Ct));
        await AssertBadRequest(await sam.GetAsync("/api/reports/mttr?groupBy=priority", Ct));
        await AssertBadRequest(await sam.GetAsync("/api/reports/sla-compliance?groupBy=line", Ct));
        await AssertBadRequest(await sam.GetAsync("/api/reports/downtime?groupBy=asset", Ct));
        await AssertBadRequest(await sam.GetAsync("/api/reports/mttr?from=2026-01-01&to=2027-01-03", Ct)); // 367 days
        await AssertBadRequest(await sam.GetAsync("/api/reports/mttr?from=2026-05-01&to=2026-05-01", Ct));
        await AssertBadRequest(await sam.GetAsync("/api/reports/mttr?from=2026-05-02&to=2026-05-01", Ct));
        await AssertBadRequest(await sam.GetAsync("/api/reports/downtime?from=not-a-date", Ct));
        await AssertBadRequest(await sam.GetAsync("/api/reports/export.xlsx?from=2026-01-01&to=2027-01-03", Ct));

        var ok = await sam.GetAsync("/api/reports/mttr?from=2026-01-01&to=2027-01-02", Ct); // exactly 366 days
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [IntegrationFact]
    public async Task Reports_need_the_view_reports_policy_and_a_session()
    {
        string[] gets =
        [
            "/api/reports/mttr",
            "/api/reports/sla-compliance",
            "/api/reports/downtime",
            "/api/reports/export.xlsx",
        ];
        using var anonymous = fixture.Factory.CreateClient();
        using var tom = Tom();
        using var oscar = As("Oscar", Roles.Operator);
        using var sam = Sam();
        using var ada = Ada();

        foreach (var path in gets)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path, Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await tom.GetAsync(path, Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await oscar.GetAsync(path, Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await sam.GetAsync(path, Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await ada.GetAsync(path, Ct)).StatusCode);
        }
    }

    // ---- rebuild ----

    [IntegrationFact]
    public async Task Rebuild_projects_completed_work_orders_that_have_no_fact_and_is_idempotent()
    {
        var (assetId, tag) = await RegisterAsset();
        await CompleteWorkOrder(assetId, priority: "P3", assetDown: false);
        await CompleteWorkOrder(assetId, priority: "P3", assetDown: true);
        await fixture.DrainWorkOrdersOutboxAsync(Ct);

        // Simulate work completed before Reporting existed: the facts are gone, the work orders are not.
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            Assert.Equal(2, await db.WorkOrderFacts.Where(f => f.AssetId == assetId).ExecuteDeleteAsync(Ct));
        }

        using var sam = Sam();
        using var ada = Ada();
        Assert.DoesNotContain(Rows(await Report(sam, "/api/reports/mttr?groupBy=asset")), r => r.GetProperty("key").GetString() == assetId.ToString());

        var rebuilt = await ada.PostAsJsonAsync("/api/reports/rebuild", new { }, Ct);
        Assert.Equal(HttpStatusCode.OK, rebuilt.StatusCode);
        Assert.True((await ReadJson(rebuilt)).GetProperty("projected").GetInt32() >= 2);

        var row = Assert.Single(Rows(await Report(sam, "/api/reports/mttr?groupBy=asset")), r => r.GetProperty("key").GetString() == assetId.ToString());
        Assert.Equal(tag, row.GetProperty("label").GetString());
        Assert.Equal(2, row.GetProperty("workOrders").GetInt32());

        // Running it again changes nothing: an upsert by work order id.
        Assert.Equal(HttpStatusCode.OK, (await ada.PostAsJsonAsync("/api/reports/rebuild", new { }, Ct)).StatusCode);
        var again = Assert.Single(Rows(await Report(sam, "/api/reports/mttr?groupBy=asset")), r => r.GetProperty("key").GetString() == assetId.ToString());
        Assert.Equal(2, again.GetProperty("workOrders").GetInt32());

        await using var check = fixture.Factory.Services.CreateAsyncScope();
        var facts = await check.ServiceProvider.GetRequiredService<ReportingDbContext>().WorkOrderFacts
            .Where(f => f.AssetId == assetId).ToListAsync(Ct);
        Assert.Equal(2, facts.Count);
        Assert.All(facts, f => Assert.Equal(Smt1, f.LineId));
        Assert.All(facts, f => Assert.Equal("P3", f.Priority));
        Assert.Single(facts, f => f.DowntimeMinutes is null);
    }

    [IntegrationFact]
    public async Task Rebuild_is_admin_only()
    {
        using var anonymous = fixture.Factory.CreateClient().WithCsrf();
        using var tom = Tom();
        using var sam = Sam();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/reports/rebuild", new { }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await tom.PostAsJsonAsync("/api/reports/rebuild", new { }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await sam.PostAsJsonAsync("/api/reports/rebuild", new { }, Ct)).StatusCode);
    }

    // ---- export ----

    [IntegrationFact]
    public async Task Export_returns_a_valid_workbook_with_the_reports_and_escapes_formulas()
    {
        var line = Guid.NewGuid();
        await Seed(
            Fact(line, Formula, "P3", new DateTimeOffset(2023, 1, 10, 4, 0, 0, TimeSpan.Zero), repairMinutes: 30, downtimeMinutes: 40, metSla: true),
            Fact(line, Formula, "P3", new DateTimeOffset(2023, 1, 12, 4, 0, 0, TimeSpan.Zero), repairMinutes: 50, downtimeMinutes: null, metSla: false));
        using var sam = Sam();

        var response = await sam.GetAsync("/api/reports/export.xlsx?from=2023-01-01&to=2023-02-01", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType?.MediaType);
        var disposition = response.Content.Headers.ContentDisposition;
        Assert.Equal("attachment", disposition?.DispositionType);
        Assert.Equal("plantops-reports-2023-01-01-2023-02-01.xlsx", (disposition!.FileNameStar ?? disposition.FileName)!.Trim('"'));

        await using var stream = await response.Content.ReadAsStreamAsync(Ct);
        using var workbook = new XLWorkbook(stream);
        Assert.Equal(
            ["MTTR by line", "SLA by priority", "Downtime by line", "MTTR by month"],
            workbook.Worksheets.Select(w => w.Name));

        var mttr = workbook.Worksheet("MTTR by line");
        Assert.True(mttr.Cell(1, 1).Style.Font.Bold);
        var label = mttr.Cell(2, 1);
        Assert.False(label.HasFormula, "a hostile line name must never become a live formula");
        // The apostrophe is Excel's quote-prefix marker: ClosedXML stores the text and sets the prefix style flag.
        Assert.Equal(Formula, label.GetString());
        Assert.True(label.Style.IncludeQuotePrefix);
        Assert.Equal(2, mttr.Cell(2, 2).GetDouble());
        Assert.Equal(40.0, mttr.Cell(2, 3).GetDouble());

        var sla = workbook.Worksheet("SLA by priority");
        Assert.Equal("P3 Medium", sla.Cell(2, 1).GetString());
        Assert.Equal(2, sla.Cell(2, 2).GetDouble());
        Assert.Equal(1, sla.Cell(2, 3).GetDouble());
        Assert.Equal(50.0, sla.Cell(2, 4).GetDouble());

        var downtime = workbook.Worksheet("Downtime by line");
        Assert.False(downtime.Cell(2, 1).HasFormula);
        Assert.Equal(Formula, downtime.Cell(2, 1).GetString());
        Assert.Equal(1, downtime.Cell(2, 2).GetDouble());
        Assert.Equal(40, downtime.Cell(2, 3).GetDouble());

        var month = workbook.Worksheet("MTTR by month");
        Assert.Equal("Jan 2023", month.Cell(2, 1).GetString());
    }
}
