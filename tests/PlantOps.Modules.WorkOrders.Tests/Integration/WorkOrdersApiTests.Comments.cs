using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PlantOps.Modules.Assets.Tests.Integration;

namespace PlantOps.Modules.WorkOrders.Tests.Integration;

// Comments on a work order: a partial of the main API test class so it shares its clients and helpers.
public partial class WorkOrdersApiTests
{
    private static Task<HttpResponseMessage> PostComment(HttpClient client, Guid workOrderId, string body) =>
        client.PostAsJsonAsync($"/api/work-orders/{workOrderId}/comments", new { body }, Ct);

    private static Task<HttpResponseMessage> PutComment(HttpClient client, Guid workOrderId, Guid commentId, string body) =>
        client.PutAsJsonAsync($"/api/work-orders/{workOrderId}/comments/{commentId}", new { body }, Ct);

    private static async Task<List<JsonElement>> Comments(HttpClient client, Guid workOrderId, string query = "")
    {
        var response = await client.GetAsync($"/api/work-orders/{workOrderId}/comments{query}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return [.. (await ReadJson(response)).EnumerateArray()];
    }

    [IntegrationFact]
    public async Task Posting_comments_returns_201_and_numbers_them_from_one()
    {
        var assetId = await RegisterAsset();
        using var oscar = Oscar();
        using var tom = Tom();
        var (id, _) = await Raise(oscar, assetId);

        var first = await PostComment(tom, id, "Looking at it now.");
        var second = await PostComment(oscar, id, "Thanks, the line is still down.");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstBody = await ReadJson(first);
        Assert.Equal(1, firstBody.GetProperty("sequence").GetInt32());
        Assert.Equal("Looking at it now.", firstBody.GetProperty("body").GetString());
        Assert.Equal("Tom", firstBody.GetProperty("author").GetProperty("name").GetString());
        Assert.Equal("tom", firstBody.GetProperty("author").GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Null, firstBody.GetProperty("editedAt").ValueKind);

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var secondBody = await ReadJson(second);
        Assert.Equal(2, secondBody.GetProperty("sequence").GetInt32());
        Assert.Equal("Oscar", secondBody.GetProperty("author").GetProperty("name").GetString());
    }

    [IntegrationFact]
    public async Task Comments_are_listed_in_sequence_order_and_can_be_paged()
    {
        var assetId = await RegisterAsset();
        using var oscar = Oscar();
        using var tom = Tom();
        var (id, _) = await Raise(oscar, assetId);
        foreach (var text in new[] { "one", "two", "three" })
        {
            Assert.Equal(HttpStatusCode.Created, (await PostComment(tom, id, text)).StatusCode);
        }

        var all = await Comments(oscar, id);
        Assert.Equal(["one", "two", "three"], all.Select(c => c.GetProperty("body").GetString()));
        Assert.Equal([1, 2, 3], all.Select(c => c.GetProperty("sequence").GetInt32()));

        var page = await Comments(oscar, id, "?skip=1&take=1");
        Assert.Equal("two", Assert.Single(page).GetProperty("body").GetString());
    }

    [IntegrationFact]
    public async Task The_author_can_edit_a_comment_and_the_list_shows_the_new_text_and_edit_time()
    {
        var assetId = await RegisterAsset();
        using var oscar = Oscar();
        using var tom = Tom();
        var (id, _) = await Raise(oscar, assetId);
        var created = await ReadJson(await PostComment(tom, id, "Replaced the feeder spring."));
        var commentId = created.GetProperty("id").GetGuid();

        var edit = await PutComment(tom, id, commentId, "Replaced the feeder spring and the guide.");

        Assert.Equal(HttpStatusCode.NoContent, edit.StatusCode);
        var comment = Assert.Single(await Comments(tom, id));
        Assert.Equal("Replaced the feeder spring and the guide.", comment.GetProperty("body").GetString());
        Assert.NotEqual(JsonValueKind.Null, comment.GetProperty("editedAt").ValueKind);
    }

    [IntegrationFact]
    public async Task A_blank_comment_is_400_and_an_unknown_work_order_or_comment_is_404()
    {
        var assetId = await RegisterAsset();
        using var oscar = Oscar();
        using var tom = Tom();
        var (id, _) = await Raise(oscar, assetId);

        await AssertProblem(await PostComment(tom, id, "   "), HttpStatusCode.BadRequest);
        await AssertProblem(await PostComment(tom, Guid.NewGuid(), "Hello"), HttpStatusCode.NotFound);
        await AssertProblem(await PutComment(tom, id, Guid.NewGuid(), "Hello"), HttpStatusCode.NotFound);
        var listResponse = await tom.GetAsync($"/api/work-orders/{Guid.NewGuid()}/comments", Ct);
        await AssertProblem(listResponse, HttpStatusCode.NotFound);
    }
}
