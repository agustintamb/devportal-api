using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace DevportalApi.Tests;

// The database is shared by the whole class, so each test works with its own users.
public class SubscriptionsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Create_ReturnsCreatedWithLocationAndAppearsInList()
    {
        var user = NewUser();
        var before = DateTimeOffset.UtcNow;

        var response = await SendAsync(HttpMethod.Post, "/subscriptions", user, """{ "apiId": "pokeapi", "plan": "free" }""");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = Guid.Parse(created.GetProperty("id").GetString()!);
        Assert.Equal("pokeapi", created.GetProperty("apiId").GetString());
        Assert.Equal("free", created.GetProperty("plan").GetString());
        var createdAt = created.GetProperty("createdAt").GetDateTimeOffset();
        Assert.Equal(TimeSpan.Zero, createdAt.Offset);
        Assert.InRange(createdAt, before, DateTimeOffset.UtcNow);
        Assert.Equal($"/subscriptions/{id}", response.Headers.Location?.OriginalString);

        var fetched = await GetJsonAsync($"/subscriptions/{id}", user);
        Assert.Equal(created.GetRawText(), fetched.GetRawText());
        var list = await GetJsonAsync("/subscriptions", user);
        Assert.Equal(created.GetRawText(), Assert.Single(list.EnumerateArray()).GetRawText());
    }

    [Fact]
    public async Task List_ReturnsOnlyOwnSubscriptions()
    {
        var owner = NewUser();
        var other = NewUser();
        await CreateAsync(owner, "petstore", "free");
        await CreateAsync(owner, "portal", "pro");
        await CreateAsync(other, "pokeapi", "free");

        var list = await GetJsonAsync("/subscriptions", owner);

        Assert.Equal(["petstore", "portal"], list.EnumerateArray().Select(item => item.GetProperty("apiId").GetString()));
    }

    [Fact]
    public async Task Create_UnknownApi_ReturnsUnprocessableEntity()
    {
        var response = await SendAsync(HttpMethod.Post, "/subscriptions", NewUser(), """{ "apiId": "nope", "plan": "free" }""");

        await AssertErrorAsync(response, HttpStatusCode.UnprocessableEntity, "unknown_api");
    }

    [Fact]
    public async Task Create_SameApiTwice_ReturnsConflictButOtherUserCanSubscribe()
    {
        var user = NewUser();
        await CreateAsync(user, "pokeapi", "free");

        var repeated = await SendAsync(HttpMethod.Post, "/subscriptions", user, """{ "apiId": "pokeapi", "plan": "pro" }""");
        var otherUser = await SendAsync(HttpMethod.Post, "/subscriptions", NewUser(), """{ "apiId": "pokeapi", "plan": "pro" }""");

        await AssertErrorAsync(repeated, HttpStatusCode.Conflict, "already_subscribed");
        Assert.Equal(HttpStatusCode.Created, otherUser.StatusCode);
        Assert.Single((await GetJsonAsync("/subscriptions", user)).EnumerateArray());
    }

    [Theory]
    [InlineData("""{ "plan": "free" }""")]
    [InlineData("""{ "apiId": "", "plan": "free" }""")]
    [InlineData("""{ "apiId": "pokeapi" }""")]
    [InlineData("""{ "apiId": "pokeapi", "plan": "Free" }""")]
    [InlineData("""{ "apiId": "pokeapi", "plan": "enterprise" }""")]
    [InlineData("""{ "apiId": "nope", "plan": "gold" }""")]
    [InlineData("""{ "apiId": 1, "plan": "free" }""")]
    public async Task Create_InvalidBody_ReturnsValidationError(string body)
    {
        var user = NewUser();

        var response = await SendAsync(HttpMethod.Post, "/subscriptions", user, body);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "validation_error");
        Assert.Empty((await GetJsonAsync("/subscriptions", user)).EnumerateArray());
    }

    [Fact]
    public async Task Patch_ChangesPlan()
    {
        var user = NewUser();
        var created = await CreateAsync(user, "portal", "free");
        var id = created.GetProperty("id").GetString();

        var response = await SendAsync(HttpMethod.Patch, $"/subscriptions/{id}", user, """{ "plan": "pro" }""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("pro", updated.GetProperty("plan").GetString());
        Assert.Equal(id, updated.GetProperty("id").GetString());
        Assert.Equal("portal", updated.GetProperty("apiId").GetString());
        Assert.Equal(created.GetProperty("createdAt").GetString(), updated.GetProperty("createdAt").GetString());
        Assert.Equal(updated.GetRawText(), (await GetJsonAsync($"/subscriptions/{id}", user)).GetRawText());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "plan": "PRO" }""")]
    [InlineData("""{ "plan": "enterprise" }""")]
    public async Task Patch_InvalidPlan_ReturnsValidationErrorAndKeepsPlan(string body)
    {
        var user = NewUser();
        var id = (await CreateAsync(user, "portal", "free")).GetProperty("id").GetString();

        var response = await SendAsync(HttpMethod.Patch, $"/subscriptions/{id}", user, body);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "validation_error");
        Assert.Equal("free", (await GetJsonAsync($"/subscriptions/{id}", user)).GetProperty("plan").GetString());
    }

    [Fact]
    public async Task Delete_ReturnsNoContentAndThenNotFound()
    {
        var user = NewUser();
        var id = (await CreateAsync(user, "petstore", "pro")).GetProperty("id").GetString();

        var response = await SendAsync(HttpMethod.Delete, $"/subscriptions/{id}", user);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, response.Content.Headers.ContentLength ?? 0);
        await AssertErrorAsync(await SendAsync(HttpMethod.Get, $"/subscriptions/{id}", user), HttpStatusCode.NotFound, "not_found");
        await AssertErrorAsync(await SendAsync(HttpMethod.Delete, $"/subscriptions/{id}", user), HttpStatusCode.NotFound, "not_found");
        Assert.Empty((await GetJsonAsync("/subscriptions", user)).EnumerateArray());
    }

    [Fact]
    public async Task OtherUsersSubscription_ReturnsNotFoundAndStaysUntouched()
    {
        var owner = NewUser();
        var intruder = NewUser();
        var created = await CreateAsync(owner, "pokeapi", "free");
        var url = $"/subscriptions/{created.GetProperty("id").GetString()}";

        var get = await SendAsync(HttpMethod.Get, url, intruder);
        var patch = await SendAsync(HttpMethod.Patch, url, intruder, """{ "plan": "pro" }""");
        var delete = await SendAsync(HttpMethod.Delete, url, intruder);

        await AssertErrorAsync(get, HttpStatusCode.NotFound, "not_found");
        await AssertErrorAsync(patch, HttpStatusCode.NotFound, "not_found");
        await AssertErrorAsync(delete, HttpStatusCode.NotFound, "not_found");
        Assert.Empty((await GetJsonAsync("/subscriptions", intruder)).EnumerateArray());
        Assert.Equal(created.GetRawText(), (await GetJsonAsync(url, owner)).GetRawText());
    }

    [Theory]
    [InlineData("GET", "3f2504e0-4f89-11d3-9a0c-0305e82c3301")]
    [InlineData("PATCH", "3f2504e0-4f89-11d3-9a0c-0305e82c3301")]
    [InlineData("DELETE", "3f2504e0-4f89-11d3-9a0c-0305e82c3301")]
    [InlineData("GET", "not-a-guid")]
    [InlineData("PATCH", "not-a-guid")]
    [InlineData("DELETE", "not-a-guid")]
    public async Task UnknownOrMalformedId_ReturnsNotFound(string method, string id)
    {
        var body = method == "PATCH" ? """{ "plan": "pro" }""" : null;

        var response = await SendAsync(new HttpMethod(method), $"/subscriptions/{id}", NewUser(), body);

        await AssertErrorAsync(response, HttpStatusCode.NotFound, "not_found");
    }

    [Theory]
    [InlineData("GET", "/subscriptions")]
    [InlineData("POST", "/subscriptions")]
    [InlineData("GET", "/subscriptions/3f2504e0-4f89-11d3-9a0c-0305e82c3301")]
    [InlineData("PATCH", "/subscriptions/3f2504e0-4f89-11d3-9a0c-0305e82c3301")]
    [InlineData("DELETE", "/subscriptions/3f2504e0-4f89-11d3-9a0c-0305e82c3301")]
    public async Task WithoutToken_ReturnsUnauthorized(string method, string url)
    {
        var response = await SendAsync(new HttpMethod(method), url, user: null);

        await AssertErrorAsync(response, HttpStatusCode.Unauthorized, "unauthorized");
    }

    [Fact]
    public async Task TokenWithoutSubClaim_ReturnsUnauthorized()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/subscriptions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(subject: null));

        var response = await _client.SendAsync(request);

        await AssertErrorAsync(response, HttpStatusCode.Unauthorized, "unauthorized");
    }

    private static string NewUser() => $"user-{Guid.NewGuid():N}";

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? user, string? json = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (user is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(subject: user));
        }

        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return await _client.SendAsync(request);
    }

    private async Task<JsonElement> CreateAsync(string user, string apiId, string plan)
    {
        var response = await SendAsync(HttpMethod.Post, "/subscriptions", user, JsonSerializer.Serialize(new { apiId, plan }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<JsonElement> GetJsonAsync(string url, string user)
    {
        var response = await SendAsync(HttpMethod.Get, url, user);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var error = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error");
        Assert.Equal(code, error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
    }
}
