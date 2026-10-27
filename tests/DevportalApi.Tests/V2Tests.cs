using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace DevportalApi.Tests;

public class V2Tests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task ListApis_DefaultsToFirstPage()
    {
        var page = await _client.GetFromJsonAsync<JsonElement>("/v2/apis");

        Assert.Equal(1, page.GetProperty("page").GetInt32());
        Assert.Equal(20, page.GetProperty("pageSize").GetInt32());
        Assert.Equal(3, page.GetProperty("total").GetInt32());
        Assert.Equal(["portal", "petstore", "pokeapi"], Ids(page));
    }

    [Theory]
    [InlineData("?page=2&pageSize=2", new[] { "pokeapi" }, 3)]
    [InlineData("?page=3&pageSize=2", new string[0], 3)]
    [InlineData("?q=pokemon&page=1&pageSize=1", new[] { "pokeapi" }, 1)]
    public async Task ListApis_ReturnsRequestedPageAfterFilters(string query, string[] ids, int total)
    {
        var page = await _client.GetFromJsonAsync<JsonElement>($"/v2/apis{query}");

        Assert.Equal(ids, Ids(page));
        Assert.Equal(total, page.GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=101")]
    [InlineData("page=abc")]
    public async Task ListApis_InvalidPaging_ReturnsValidationError(string query)
    {
        var response = await _client.GetAsync($"/v2/apis?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error");
        Assert.Equal("validation_error", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ListSubscriptions_IsPagedAndEmptyForNewUser()
    {
        var token = TestTokens.Create(subject: $"user-{Guid.NewGuid():N}");

        var empty = await GetWithTokenAsync("/v2/subscriptions", token);
        Assert.Empty(empty.GetProperty("items").EnumerateArray());
        Assert.Equal(0, empty.GetProperty("total").GetInt32());

        var request = new HttpRequestMessage(HttpMethod.Post, "/v2/subscriptions")
        {
            Content = JsonContent.Create(new { apiId = "pokeapi", plan = "free" }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var created = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.StartsWith("/v2/subscriptions/", created.Headers.Location?.OriginalString);

        var list = await GetWithTokenAsync("/v2/subscriptions", token);
        Assert.Equal(1, list.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task V1_ListApis_IsStillAnArray()
    {
        var apis = await _client.GetFromJsonAsync<JsonElement>("/apis");

        Assert.Equal(JsonValueKind.Array, apis.ValueKind);
        Assert.Equal(3, apis.GetArrayLength());
    }

    private async Task<JsonElement> GetWithTokenAsync(string url, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static IEnumerable<string?> Ids(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(api => api.GetProperty("id").GetString());
}
