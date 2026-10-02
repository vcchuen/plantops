using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PlantOps.Modules.Assets.Infrastructure;

namespace PlantOps.Modules.Assets.Tests.Integration;

// All tests share one database, so every test builds its data from a unique random prefix and filters on it.
[Collection(IntegrationCollection.Name)]
public class AssetsApiTests(SqlServerFixture fixture)
{
    private static readonly Guid Smt1 = ProductionLineConfiguration.Smt1.Value;
    private static readonly Guid Fa1 = ProductionLineConfiguration.Fa1.Value;
    private static readonly Guid Test1 = ProductionLineConfiguration.Test1.Value;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient CreateClient() => fixture.Factory.CreateClient();

    private static string NewPrefix() => "T" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private static object NewAssetBody(string tag, Guid? lineId = null, string criticality = "B", string? name = null) => new
    {
        tag,
        name = name ?? "Pick and place",
        manufacturer = "Fuji",
        model = "NXT III",
        serialNumber = "SN-" + tag,
        lineId = lineId ?? Smt1,
        station = "Station 1",
        criticality,
        commissionedOn = "2024-03-01",
    };

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private static async Task<Guid> RegisterAndGetId(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/assets", body, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadJson(response)).GetProperty("id").GetGuid();
    }

    private static string Today() => DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await ReadJson(response);
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("detail").GetString()));
    }

    // Registers `count` assets tagged {prefix}-01, {prefix}-02, ...; the first `onFa1` go on FA-1 with criticality A,
    // the rest on TEST-1 with criticality C. Names contain the prefix but do not start with it.
    private async Task SeedAssets(HttpClient client, string prefix, int count, int onFa1)
    {
        for (var i = 1; i <= count; i++)
        {
            var onFirstLine = i <= onFa1;
            var body = NewAssetBody($"{prefix}-{i:00}", onFirstLine ? Fa1 : Test1, onFirstLine ? "A" : "C", $"Oven {prefix}-{i:00}");
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/assets", body, Ct)).StatusCode);
        }
    }

    [IntegrationFact]
    public async Task Lines_returns_the_seeded_lines_ordered_by_code()
    {
        using var client = CreateClient();

        var lines = await client.GetFromJsonAsync<JsonElement>("/api/assets/lines", Ct);

        var codes = lines.EnumerateArray().Select(l => l.GetProperty("code").GetString()).ToList();
        Assert.Equal(["FA-1", "SMT-1", "SMT-2", "TEST-1"], codes);
    }

    [IntegrationFact]
    public async Task Register_returns_201_with_location_header_and_detail_body()
    {
        using var client = CreateClient();
        var tag = NewPrefix() + "-pnp";

        var response = await client.PostAsJsonAsync("/api/assets", NewAssetBody(tag), Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJson(response);
        var id = body.GetProperty("id").GetGuid();
        Assert.Equal($"/api/assets/{id}", response.Headers.Location?.OriginalString);
        Assert.Equal(tag.ToUpperInvariant(), body.GetProperty("tag").GetString());
        Assert.Equal("SMT-1", body.GetProperty("lineCode").GetString());
        Assert.Equal("SMT Line 1", body.GetProperty("lineName").GetString());
        Assert.Equal("Station 1", body.GetProperty("station").GetString());
        Assert.Equal("B", body.GetProperty("criticality").GetString());
        Assert.Equal("InService", body.GetProperty("status").GetString());
        Assert.Equal("2024-03-01", body.GetProperty("commissionedOn").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("decommissionedOn").ValueKind);
    }

    [IntegrationFact]
    public async Task Get_by_id_returns_the_registered_asset()
    {
        using var client = CreateClient();
        var id = await RegisterAndGetId(client, NewAssetBody(NewPrefix() + "-GET"));

        var response = await client.GetAsync($"/api/assets/{id}", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJson(response);
        Assert.Equal(id, body.GetProperty("id").GetGuid());
        Assert.Equal("Fuji", body.GetProperty("manufacturer").GetString());
    }

    [IntegrationFact]
    public async Task Get_unknown_id_returns_404_problem()
    {
        using var client = CreateClient();

        var response = await client.GetAsync($"/api/assets/{Guid.NewGuid()}", Ct);

        await AssertProblem(response, HttpStatusCode.NotFound);
    }

    [IntegrationFact]
    public async Task Register_duplicate_tag_returns_409_problem()
    {
        using var client = CreateClient();
        var tag = NewPrefix() + "-DUP";
        await RegisterAndGetId(client, NewAssetBody(tag));

        var response = await client.PostAsJsonAsync("/api/assets", NewAssetBody(tag.ToLowerInvariant()), Ct);

        await AssertProblem(response, HttpStatusCode.Conflict);
        Assert.Contains(tag, (await ReadJson(response)).GetProperty("detail").GetString());
    }

    [IntegrationFact]
    public async Task Concurrent_registrations_of_the_same_tag_yield_exactly_one_201_and_one_409()
    {
        using var client = CreateClient();
        var body = NewAssetBody(NewPrefix() + "-RACE");

        var responses = await Task.WhenAll(
            client.PostAsJsonAsync("/api/assets", body, Ct),
            client.PostAsJsonAsync("/api/assets", body, Ct));

        var statuses = responses.Select(r => r.StatusCode).OrderBy(s => s).ToArray();
        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], statuses);
    }

    [IntegrationFact]
    public async Task Register_with_invalid_tag_returns_400_problem()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/assets", NewAssetBody("bad_tag!"), Ct);

        await AssertProblem(response, HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task Register_with_unknown_line_returns_400_problem()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/assets", NewAssetBody(NewPrefix() + "-LINE", Guid.NewGuid()), Ct);

        await AssertProblem(response, HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task List_filters_by_search_prefix_line_criticality_and_pages_with_totals()
    {
        using var client = CreateClient();
        var prefix = NewPrefix();
        await SeedAssets(client, prefix, count: 12, onFa1: 5);

        var all = await client.GetFromJsonAsync<JsonElement>($"/api/assets?search={prefix}&pageSize=5", Ct);
        Assert.Equal(12, all.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, all.GetProperty("page").GetInt32());
        Assert.Equal(5, all.GetProperty("pageSize").GetInt32());
        var firstPageTags = all.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("tag").GetString()).ToList();
        Assert.Equal(Enumerable.Range(1, 5).Select(i => $"{prefix}-{i:00}"), firstPageTags);

        var lastPage = await client.GetFromJsonAsync<JsonElement>($"/api/assets?search={prefix}&pageSize=5&page=3", Ct);
        Assert.Equal(2, lastPage.GetProperty("items").GetArrayLength());
        Assert.Equal(12, lastPage.GetProperty("totalCount").GetInt32());

        var onLine = await client.GetFromJsonAsync<JsonElement>($"/api/assets?search={prefix}&lineId={Fa1}", Ct);
        Assert.Equal(5, onLine.GetProperty("totalCount").GetInt32());
        Assert.All(onLine.GetProperty("items").EnumerateArray(), i => Assert.Equal("Final Assembly 1", i.GetProperty("lineName").GetString()));

        var critical = await client.GetFromJsonAsync<JsonElement>($"/api/assets?search={prefix}&criticality=C", Ct);
        Assert.Equal(7, critical.GetProperty("totalCount").GetInt32());
        Assert.All(critical.GetProperty("items").EnumerateArray(), i => Assert.Equal("C", i.GetProperty("criticality").GetString()));

        var lowerCasePrefix = await client.GetFromJsonAsync<JsonElement>($"/api/assets?search={prefix.ToLowerInvariant()}", Ct);
        Assert.Equal(12, lowerCasePrefix.GetProperty("totalCount").GetInt32());

        // "Oven T1234-01" is only in the name, never at the start of a tag, so this exercises the Contains branch.
        var byName = await client.GetFromJsonAsync<JsonElement>($"/api/assets?search={Uri.EscapeDataString($"Oven {prefix}-07")}", Ct);
        Assert.Equal(1, byName.GetProperty("totalCount").GetInt32());
    }

    [IntegrationFact]
    public async Task List_search_treats_like_wildcards_literally()
    {
        using var client = CreateClient();
        await RegisterAndGetId(client, NewAssetBody(NewPrefix() + "-WLD"));

        var percent = await client.GetFromJsonAsync<JsonElement>("/api/assets?search=%25", Ct);
        var underscore = await client.GetFromJsonAsync<JsonElement>("/api/assets?search=_", Ct);

        Assert.Equal(0, percent.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, underscore.GetProperty("totalCount").GetInt32());
    }

    [IntegrationFact]
    public async Task List_filters_by_status()
    {
        using var client = CreateClient();
        var prefix = NewPrefix();
        await SeedAssets(client, prefix, count: 3, onFa1: 3);
        var id = (await client.GetFromJsonAsync<JsonElement>($"/api/assets?search={prefix}-02", Ct))
            .GetProperty("items")[0].GetProperty("id").GetGuid();
        var decommission = await client.PostAsJsonAsync($"/api/assets/{id}/decommission", new { on = Today(), reason = "Scrapped" }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, decommission.StatusCode);

        var retired = await client.GetFromJsonAsync<JsonElement>($"/api/assets?search={prefix}&status=Decommissioned", Ct);
        var active = await client.GetFromJsonAsync<JsonElement>($"/api/assets?search={prefix}&status=InService", Ct);

        Assert.Equal(1, retired.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, active.GetProperty("totalCount").GetInt32());
    }

    [IntegrationFact]
    public async Task List_clamps_page_and_page_size()
    {
        using var client = CreateClient();

        var result = await client.GetFromJsonAsync<JsonElement>("/api/assets?page=0&pageSize=1000", Ct);

        Assert.Equal(1, result.GetProperty("page").GetInt32());
        Assert.Equal(100, result.GetProperty("pageSize").GetInt32());
    }

    [IntegrationFact]
    public async Task List_runs_exactly_two_sql_commands_regardless_of_page_size()
    {
        using var client = CreateClient();
        var prefix = NewPrefix();
        await SeedAssets(client, prefix, count: 12, onFa1: 6);

        fixture.Commands.Reset();
        var result = await client.GetFromJsonAsync<JsonElement>($"/api/assets?search={prefix}&pageSize=12", Ct);

        Assert.Equal(12, result.GetProperty("items").GetArrayLength());
        Assert.Equal(2, fixture.Commands.Count);
    }

    [IntegrationFact]
    public async Task Update_details_returns_204_and_is_reflected_in_detail()
    {
        using var client = CreateClient();
        var id = await RegisterAndGetId(client, NewAssetBody(NewPrefix() + "-UPD"));

        var response = await client.PutAsJsonAsync(
            $"/api/assets/{id}/details",
            new { name = "Reflow oven", manufacturer = "Heller", model = "1913", serialNumber = (string?)null },
            Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/assets/{id}", Ct);
        Assert.Equal("Reflow oven", detail.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("serialNumber").ValueKind);
    }

    [IntegrationFact]
    public async Task Relocate_returns_204_and_moves_the_asset()
    {
        using var client = CreateClient();
        var id = await RegisterAndGetId(client, NewAssetBody(NewPrefix() + "-MOVE"));

        var response = await client.PostAsJsonAsync($"/api/assets/{id}/relocate", new { lineId = Test1, station = "Station 7" }, Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/assets/{id}", Ct);
        Assert.Equal("TEST-1", detail.GetProperty("lineCode").GetString());
        Assert.Equal("Station 7", detail.GetProperty("station").GetString());
    }

    [IntegrationFact]
    public async Task Relocate_to_unknown_line_returns_400_problem()
    {
        using var client = CreateClient();
        var id = await RegisterAndGetId(client, NewAssetBody(NewPrefix() + "-NOLN"));

        var response = await client.PostAsJsonAsync($"/api/assets/{id}/relocate", new { lineId = Guid.NewGuid(), station = "S1" }, Ct);

        await AssertProblem(response, HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task Change_criticality_returns_204_and_updates_the_asset()
    {
        using var client = CreateClient();
        var id = await RegisterAndGetId(client, NewAssetBody(NewPrefix() + "-CRIT", criticality: "C"));

        var response = await client.PostAsJsonAsync($"/api/assets/{id}/criticality", new { criticality = "A" }, Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/assets/{id}", Ct);
        Assert.Equal("A", detail.GetProperty("criticality").GetString());
    }

    [IntegrationFact]
    public async Task Decommission_returns_204_and_records_date_reason_and_status()
    {
        using var client = CreateClient();
        var id = await RegisterAndGetId(client, NewAssetBody(NewPrefix() + "-DEC"));

        var response = await client.PostAsJsonAsync($"/api/assets/{id}/decommission", new { on = Today(), reason = "End of life" }, Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/assets/{id}", Ct);
        Assert.Equal("Decommissioned", detail.GetProperty("status").GetString());
        Assert.Equal(Today(), detail.GetProperty("decommissionedOn").GetString());
        Assert.Equal("End of life", detail.GetProperty("decommissionReason").GetString());
    }

    [IntegrationFact]
    public async Task Decommission_in_the_future_returns_400_problem()
    {
        using var client = CreateClient();
        var id = await RegisterAndGetId(client, NewAssetBody(NewPrefix() + "-FUT"));
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2).ToString("yyyy-MM-dd");

        var response = await client.PostAsJsonAsync($"/api/assets/{id}/decommission", new { on = tomorrow, reason = "Too early" }, Ct);

        await AssertProblem(response, HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task Relocating_a_decommissioned_asset_returns_400_problem()
    {
        using var client = CreateClient();
        var id = await RegisterAndGetId(client, NewAssetBody(NewPrefix() + "-RET"));
        var decommission = await client.PostAsJsonAsync($"/api/assets/{id}/decommission", new { on = Today(), reason = "Scrapped" }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, decommission.StatusCode);

        var response = await client.PostAsJsonAsync($"/api/assets/{id}/relocate", new { lineId = Test1, station = "S9" }, Ct);

        await AssertProblem(response, HttpStatusCode.BadRequest);
    }

    [IntegrationFact]
    public async Task Commands_on_an_unknown_asset_return_404_problem()
    {
        using var client = CreateClient();
        var id = Guid.NewGuid();

        var update = await client.PutAsJsonAsync($"/api/assets/{id}/details", new { name = "n", manufacturer = "m", model = "x" }, Ct);
        var relocate = await client.PostAsJsonAsync($"/api/assets/{id}/relocate", new { lineId = Smt1, station = "S1" }, Ct);
        var criticality = await client.PostAsJsonAsync($"/api/assets/{id}/criticality", new { criticality = "A" }, Ct);
        var decommission = await client.PostAsJsonAsync($"/api/assets/{id}/decommission", new { on = Today(), reason = "r" }, Ct);

        foreach (var response in new[] { update, relocate, criticality, decommission })
        {
            await AssertProblem(response, HttpStatusCode.NotFound);
        }
    }
}
