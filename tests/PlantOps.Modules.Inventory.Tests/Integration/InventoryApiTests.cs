using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlantOps.Api.Tests;
using PlantOps.BuildingBlocks.Infrastructure;
using PlantOps.Modules.Assets.Infrastructure;
using PlantOps.Modules.Assets.Tests.Integration;
using PlantOps.Modules.Identity.Contracts;
using PlantOps.Modules.WorkOrders.Infrastructure;

namespace PlantOps.Modules.Inventory.Tests.Integration;

// All tests share one database, so each builds its own asset, part and work orders (unique names) and asserts only on those.
[Collection(InventoryCollection.Name)]
public class InventoryApiTests(InventoryFixture fixture)
{
    private static readonly Guid Smt1 = ProductionLineConfiguration.Smt1.Value;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // The seeded directory (see the fixture): tom and lee are technicians, sam a supervisor, ada an admin.
    private HttpClient As(string name, params string[] roles) => fixture.Factory.CreateClient().AsUser(name, roles).WithCsrf();

    private HttpClient Sam() => As("Sam", Roles.Supervisor);

    private HttpClient Tom() => As("Tom", Roles.Technician);

    private HttpClient Lee() => As("Lee", Roles.Technician);

    private HttpClient Olivia() => As("Olivia", Roles.Operator);

    private static string NewPrefix() => "I" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private static async Task<JsonElement> AssertProblem(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return await ReadJson(response);
    }

    private static string ETagOf(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues("ETag", out var values), "response has no ETag header");
        return Assert.Single(values);
    }

    // ---- arrange helpers ----

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

    // A part with a unique number and the given stock; returns its id and number.
    private async Task<(Guid Id, string PartNumber)> CreatePart(int onHand, int reorderLevel = 1)
    {
        using var sam = Sam();
        var partNumber = NewPrefix() + "-PART";
        var created = await sam.PostAsJsonAsync("/api/inventory/parts", new
        {
            partNumber,
            name = "Feeder spring",
            unit = "pcs",
            binLocation = "A-03-2",
            reorderLevel,
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await ReadJson(created)).GetProperty("id").GetGuid();

        if (onHand > 0)
        {
            var received = await sam.PostAsJsonAsync($"/api/inventory/parts/{id}/receive", new { quantity = onHand }, Ct);
            Assert.Equal(HttpStatusCode.NoContent, received.StatusCode);
        }

        return (id, partNumber);
    }

    private static Task<HttpResponseMessage> Command(HttpClient client, Guid id, string action, string etag, object? body = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/work-orders/{id}/{action}");
        request.Headers.TryAddWithoutValidation("If-Match", etag);

        // Always a JSON body (possibly "{}"): endpoints that bind a body only match application/json requests.
        request.Content = JsonContent.Create(body ?? new { });
        return client.SendAsync(request, Ct);
    }

    private static async Task<string> Succeed(HttpClient client, Guid id, string action, string etag, object? body = null)
    {
        var response = await Command(client, id, action, etag, body);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return ETagOf(response);
    }

    // A work order raised and approved, optionally assigned to Tom. Returns its id, ETag and display number.
    private async Task<(Guid Id, string ETag, string Number)> NewWorkOrder(Guid assetId, bool assignToTom = true)
    {
        using var oscar = As("Oscar", Roles.Operator);
        using var sam = Sam();
        var raised = await oscar.PostAsJsonAsync("/api/work-orders", new
        {
            assetId,
            title = "Feeder 4 jams",
            description = "Jams every few minutes.",
            priority = "P2",
            assetDown = true,
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, raised.StatusCode);
        var etag = ETagOf(raised);
        var body = await ReadJson(raised);
        var id = body.GetProperty("id").GetGuid();
        var number = body.GetProperty("number").GetString()!;

        etag = await Succeed(sam, id, "approve", etag);
        if (assignToTom)
        {
            etag = await Succeed(sam, id, "assign", etag, new { technicianId = "tom" });
        }

        return (id, etag, number);
    }

    private static Task<HttpResponseMessage> Reserve(HttpClient client, Guid partId, Guid workOrderId, int quantity) =>
        client.PostAsJsonAsync("/api/inventory/reservations", new { partId, workOrderId, quantity }, Ct);

    private static async Task<JsonElement> GetPart(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"/api/inventory/parts/{id}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJson(response);
    }

    private static async Task<JsonElement[]> ReservationsFor(HttpClient client, Guid workOrderId)
    {
        var page = await client.GetFromJsonAsync<JsonElement>($"/api/inventory/reservations?workOrderId={workOrderId}", Ct);
        return [.. page.GetProperty("items").EnumerateArray()];
    }

    // ---- parts ----

    [IntegrationFact]
    public async Task Creating_a_part_returns_its_detail_and_a_duplicate_part_number_is_409()
    {
        using var sam = Sam();
        var (id, partNumber) = await CreatePart(onHand: 0, reorderLevel: 2);

        var detail = await GetPart(sam, id);
        Assert.Equal(partNumber, detail.GetProperty("partNumber").GetString());
        Assert.Equal(0, detail.GetProperty("quantityOnHand").GetInt32());
        Assert.Equal(0, detail.GetProperty("quantityAvailable").GetInt32());
        Assert.True(detail.GetProperty("isLowStock").GetBoolean());
        Assert.Empty(detail.GetProperty("reservations").EnumerateArray());

        var duplicate = await sam.PostAsJsonAsync("/api/inventory/parts", new
        {
            partNumber = partNumber.ToLowerInvariant(),
            name = "Other",
            unit = "pcs",
            binLocation = "B-1",
            reorderLevel = 0,
        }, Ct);
        var problem = await AssertProblem(duplicate, HttpStatusCode.Conflict);
        Assert.Contains(partNumber, problem.GetProperty("detail").GetString());
    }

    [IntegrationFact]
    public async Task Only_managers_may_register_parts_and_receive_stock()
    {
        var (id, _) = await CreatePart(onHand: 1);
        using var tom = Tom();

        var create = await tom.PostAsJsonAsync("/api/inventory/parts", new
        {
            partNumber = NewPrefix(),
            name = "x",
            unit = "pcs",
            binLocation = "A",
            reorderLevel = 0,
        }, Ct);
        var receive = await tom.PostAsJsonAsync($"/api/inventory/parts/{id}/receive", new { quantity = 1 }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, receive.StatusCode);
    }

    [IntegrationFact]
    public async Task The_list_filters_low_stock_and_pages()
    {
        using var sam = Sam();
        var (lowId, lowNumber) = await CreatePart(onHand: 1, reorderLevel: 5);
        var (okId, okNumber) = await CreatePart(onHand: 50, reorderLevel: 5);

        var low = await sam.GetFromJsonAsync<JsonElement>($"/api/inventory/parts?lowStock=true&search={lowNumber}", Ct);
        var ok = await sam.GetFromJsonAsync<JsonElement>($"/api/inventory/parts?lowStock=true&search={okNumber}", Ct);

        Assert.Equal(lowId, Assert.Single(low.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(1, low.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, ok.GetProperty("totalCount").GetInt32());
        Assert.NotEqual(lowId, okId);
    }

    [IntegrationFact]
    public async Task An_unknown_part_is_404()
    {
        using var sam = Sam();

        var response = await sam.GetAsync($"/api/inventory/parts/{Guid.NewGuid()}", Ct);

        await AssertProblem(response, HttpStatusCode.NotFound);
    }

    // ---- reserving ----

    [IntegrationFact]
    public async Task The_assigned_technician_reserves_a_part_and_it_shows_in_the_detail_and_the_list()
    {
        var assetId = await RegisterAsset();
        var (partId, partNumber) = await CreatePart(onHand: 5);
        var (workOrderId, _, number) = await NewWorkOrder(assetId);
        using var tom = Tom();

        var response = await Reserve(tom, partId, workOrderId, 2);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var item = await ReadJson(response);
        Assert.Equal(partNumber, item.GetProperty("partNumber").GetString());
        Assert.Equal("pcs", item.GetProperty("unit").GetString());
        Assert.Equal(2, item.GetProperty("quantity").GetInt32());
        Assert.Equal("Active", item.GetProperty("status").GetString());
        Assert.Equal("Tom", item.GetProperty("reservedByName").GetString());

        var detail = await GetPart(tom, partId);
        Assert.Equal(5, detail.GetProperty("quantityOnHand").GetInt32());
        Assert.Equal(2, detail.GetProperty("quantityReserved").GetInt32());
        Assert.Equal(3, detail.GetProperty("quantityAvailable").GetInt32());
        var reservation = Assert.Single(detail.GetProperty("reservations").EnumerateArray());
        Assert.Equal(workOrderId, reservation.GetProperty("workOrderId").GetGuid());
        Assert.Equal(number, reservation.GetProperty("workOrderNumber").GetString());

        var listed = Assert.Single(await ReservationsFor(tom, workOrderId));
        Assert.Equal(item.GetProperty("id").GetGuid(), listed.GetProperty("id").GetGuid());
    }

    [IntegrationFact]
    public async Task Reserving_again_for_the_same_work_order_grows_the_one_reservation()
    {
        var assetId = await RegisterAsset();
        var (partId, _) = await CreatePart(onHand: 5);
        var (workOrderId, _, _) = await NewWorkOrder(assetId);
        using var tom = Tom();

        var first = await ReadJson(await Reserve(tom, partId, workOrderId, 1));
        var second = await ReadJson(await Reserve(tom, partId, workOrderId, 2));

        Assert.Equal(first.GetProperty("id").GetGuid(), second.GetProperty("id").GetGuid());
        Assert.Equal(3, second.GetProperty("quantity").GetInt32());
        Assert.Equal(3, (await GetPart(tom, partId)).GetProperty("quantityReserved").GetInt32());
    }

    [IntegrationFact]
    public async Task Reserving_more_than_is_available_is_409_with_the_available_quantity_in_the_message()
    {
        var assetId = await RegisterAsset();
        var (partId, partNumber) = await CreatePart(onHand: 2);
        var (workOrderId, _, _) = await NewWorkOrder(assetId);
        using var tom = Tom();

        var response = await Reserve(tom, partId, workOrderId, 3);

        var problem = await AssertProblem(response, HttpStatusCode.Conflict);
        Assert.Equal($"Only 2 pcs of {partNumber} available", problem.GetProperty("detail").GetString());
        Assert.Equal(0, (await GetPart(tom, partId)).GetProperty("quantityReserved").GetInt32());
    }

    [IntegrationFact]
    public async Task Two_simultaneous_reservations_of_the_last_unit_give_exactly_one_201_and_one_409()
    {
        var assetId = await RegisterAsset();
        var (partId, _) = await CreatePart(onHand: 1);
        var first = await NewWorkOrder(assetId);
        var second = await NewWorkOrder(assetId);
        using var tom1 = Tom();
        using var tom2 = Tom();

        // Both read Available = 1. One UPDATE wins; the other matches zero rows (RowVersion), is retried from a fresh
        // read, sees Available = 0 and answers 409. A broken invariant would show as two 201s and Reserved = 2.
        var responses = await Task.WhenAll(
            Reserve(tom1, partId, first.Id, 1),
            Reserve(tom2, partId, second.Id, 1));

        Assert.Equal(
            [HttpStatusCode.Created, HttpStatusCode.Conflict],
            responses.Select(r => r.StatusCode).Order());
        var detail = await GetPart(tom1, partId);
        Assert.Equal(1, detail.GetProperty("quantityReserved").GetInt32());
        Assert.Equal(0, detail.GetProperty("quantityAvailable").GetInt32());
        Assert.Single(detail.GetProperty("reservations").EnumerateArray());
    }

    [IntegrationFact]
    public async Task Operators_and_unassigned_technicians_get_403_but_supervisors_may_reserve()
    {
        var assetId = await RegisterAsset();
        var (partId, _) = await CreatePart(onHand: 5);
        var (workOrderId, _, _) = await NewWorkOrder(assetId);
        using var olivia = Olivia();
        using var lee = Lee();
        using var sam = Sam();

        var operatorResponse = await Reserve(olivia, partId, workOrderId, 1);
        var otherTechnician = await Reserve(lee, partId, workOrderId, 1);
        var supervisor = await Reserve(sam, partId, workOrderId, 1);

        Assert.Equal(HttpStatusCode.Forbidden, operatorResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, otherTechnician.StatusCode);
        Assert.Equal(HttpStatusCode.Created, supervisor.StatusCode);
        Assert.Equal(1, (await GetPart(sam, partId)).GetProperty("quantityReserved").GetInt32());
    }

    [IntegrationFact]
    public async Task A_work_order_that_is_not_assigned_or_in_progress_is_400()
    {
        var assetId = await RegisterAsset();
        var (partId, _) = await CreatePart(onHand: 5);
        var (approvedOnly, _, _) = await NewWorkOrder(assetId, assignToTom: false);
        using var sam = Sam();

        var notReservable = await Reserve(sam, partId, approvedOnly, 1);
        var unknown = await Reserve(sam, partId, Guid.NewGuid(), 1);

        await AssertProblem(notReservable, HttpStatusCode.BadRequest);
        await AssertProblem(unknown, HttpStatusCode.BadRequest);
        Assert.Equal(0, (await GetPart(sam, partId)).GetProperty("quantityReserved").GetInt32());
    }

    [IntegrationFact]
    public async Task A_non_positive_quantity_is_400()
    {
        var assetId = await RegisterAsset();
        var (partId, _) = await CreatePart(onHand: 5);
        var (workOrderId, _, _) = await NewWorkOrder(assetId);
        using var tom = Tom();

        var response = await Reserve(tom, partId, workOrderId, 0);

        await AssertProblem(response, HttpStatusCode.BadRequest);
    }

    // ---- releasing ----

    [IntegrationFact]
    public async Task The_assigned_technician_releases_a_reservation_and_a_second_release_is_400()
    {
        var assetId = await RegisterAsset();
        var (partId, _) = await CreatePart(onHand: 5);
        var (workOrderId, _, _) = await NewWorkOrder(assetId);
        using var tom = Tom();
        using var lee = Lee();
        var reservationId = (await ReadJson(await Reserve(tom, partId, workOrderId, 2))).GetProperty("id").GetGuid();

        var forbidden = await lee.PostAsJsonAsync($"/api/inventory/reservations/{reservationId}/release", new { }, Ct);
        var released = await tom.PostAsJsonAsync($"/api/inventory/reservations/{reservationId}/release", new { }, Ct);
        var again = await tom.PostAsJsonAsync($"/api/inventory/reservations/{reservationId}/release", new { }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, released.StatusCode);
        await AssertProblem(again, HttpStatusCode.BadRequest);
        var detail = await GetPart(tom, partId);
        Assert.Equal(0, detail.GetProperty("quantityReserved").GetInt32());
        Assert.Empty(detail.GetProperty("reservations").EnumerateArray());
        Assert.Equal("Released", Assert.Single(await ReservationsFor(tom, workOrderId)).GetProperty("status").GetString());
    }

    [IntegrationFact]
    public async Task Releasing_an_unknown_reservation_is_404()
    {
        using var sam = Sam();

        var response = await sam.PostAsJsonAsync($"/api/inventory/reservations/{Guid.NewGuid()}/release", new { }, Ct);

        await AssertProblem(response, HttpStatusCode.NotFound);
    }

    // ---- events between modules (the outbox) ----

    // Raise -> approve -> assign -> reserve -> start -> complete, as Tom with Sam supervising.
    private async Task<(Guid AssetId, Guid PartId, Guid WorkOrderId, string Number)> CompletedWorkOrderWithPart()
    {
        var assetId = await RegisterAsset();
        var (partId, _) = await CreatePart(onHand: 5);
        var (workOrderId, etag, number) = await NewWorkOrder(assetId);
        using var tom = Tom();

        var reserved = await Reserve(tom, partId, workOrderId, 2);
        Assert.Equal(HttpStatusCode.Created, reserved.StatusCode);
        etag = await Succeed(tom, workOrderId, "start", etag);
        await Succeed(tom, workOrderId, "complete", etag, new { resolution = "Replaced the feeder spring." });
        return (assetId, partId, workOrderId, number);
    }

    [IntegrationFact]
    public async Task Completing_a_work_order_consumes_its_parts_and_records_the_maintenance_after_the_outbox_is_delivered()
    {
        var (assetId, partId, workOrderId, number) = await CompletedWorkOrderWithPart();
        using var sam = Sam();

        // Before delivery: eventually consistent, the stock is still only reserved.
        var before = await GetPart(sam, partId);
        Assert.Equal(5, before.GetProperty("quantityOnHand").GetInt32());
        Assert.Equal(2, before.GetProperty("quantityReserved").GetInt32());
        var emptyHistory = await sam.GetFromJsonAsync<JsonElement>($"/api/assets/{assetId}/maintenance", Ct);
        Assert.Empty(emptyHistory.EnumerateArray());

        var delivered = await fixture.DrainWorkOrdersOutboxAsync(Ct);

        Assert.True(delivered >= 1, "the completion event should have been in the outbox");
        var after = await GetPart(sam, partId);
        Assert.Equal(3, after.GetProperty("quantityOnHand").GetInt32());
        Assert.Equal(0, after.GetProperty("quantityReserved").GetInt32());
        Assert.Equal(3, after.GetProperty("quantityAvailable").GetInt32());
        Assert.Empty(after.GetProperty("reservations").EnumerateArray());
        Assert.Equal("Consumed", Assert.Single(await ReservationsFor(sam, workOrderId)).GetProperty("status").GetString());

        var history = (await sam.GetFromJsonAsync<JsonElement>($"/api/assets/{assetId}/maintenance", Ct)).EnumerateArray().ToList();
        var record = Assert.Single(history);
        Assert.Equal(workOrderId, record.GetProperty("workOrderId").GetGuid());
        Assert.Equal(number, record.GetProperty("number").GetString());
        Assert.Equal("Feeder 4 jams", record.GetProperty("title").GetString());
        Assert.Equal("Replaced the feeder spring.", record.GetProperty("resolution").GetString());
        Assert.Equal("Tom", record.GetProperty("technicianName").GetString());
        // The asset was reported down, so downtime is report-to-completion in minutes (a few seconds here, so 0 or more).
        Assert.True(record.GetProperty("downtimeMinutes").GetInt32() >= 0);
    }

    [IntegrationFact]
    public async Task Delivering_the_same_outbox_message_twice_applies_its_effect_once()
    {
        var (assetId, partId, workOrderId, _) = await CompletedWorkOrderWithPart();
        using var sam = Sam();
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
        var part = await GetPart(sam, partId);
        Assert.Equal(3, part.GetProperty("quantityOnHand").GetInt32()); // 5 - 2, not 5 - 4
        Assert.Equal(0, part.GetProperty("quantityReserved").GetInt32());
        var history = await sam.GetFromJsonAsync<JsonElement>($"/api/assets/{assetId}/maintenance", Ct);
        Assert.Single(history.EnumerateArray());
    }

    [IntegrationFact]
    public async Task Cancelling_a_work_order_releases_its_reservations_after_the_outbox_is_delivered()
    {
        var assetId = await RegisterAsset();
        var (partId, _) = await CreatePart(onHand: 5);
        var (workOrderId, etag, _) = await NewWorkOrder(assetId);
        using var tom = Tom();
        using var sam = Sam();
        Assert.Equal(HttpStatusCode.Created, (await Reserve(tom, partId, workOrderId, 2)).StatusCode);
        Assert.Equal(2, (await GetPart(sam, partId)).GetProperty("quantityReserved").GetInt32());

        await Succeed(sam, workOrderId, "cancel", etag, new { reason = "Machine scrapped" });
        await fixture.DrainWorkOrdersOutboxAsync(Ct);

        var part = await GetPart(sam, partId);
        Assert.Equal(5, part.GetProperty("quantityOnHand").GetInt32());
        Assert.Equal(0, part.GetProperty("quantityReserved").GetInt32());
        Assert.Equal("Released", Assert.Single(await ReservationsFor(sam, workOrderId)).GetProperty("status").GetString());
    }

    // ---- maintenance history ----

    [IntegrationFact]
    public async Task Maintenance_history_is_empty_for_a_new_asset_and_404_for_an_unknown_one()
    {
        var assetId = await RegisterAsset();
        using var sam = Sam();

        var empty = await sam.GetFromJsonAsync<JsonElement>($"/api/assets/{assetId}/maintenance", Ct);
        var unknown = await sam.GetAsync($"/api/assets/{Guid.NewGuid()}/maintenance", Ct);

        Assert.Empty(empty.EnumerateArray());
        await AssertProblem(unknown, HttpStatusCode.NotFound);
    }
}
