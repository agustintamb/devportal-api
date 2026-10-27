using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace DevportalApi.Tests;

public class DashboardTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Theory]
    [InlineData("none")]
    [InlineData("basic")]
    [InlineData("bad-signature")]
    [InlineData("other-audience")]
    [InlineData("expired")]
    public async Task GetStats_WithoutValidToken_ReturnsUnauthorizedError(string authCase)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/dashboard/stats");
        request.Headers.Authorization = authCase switch
        {
            "none" => null,
            "basic" => new AuthenticationHeaderValue("Basic", "x"),
            "bad-signature" => Bearer(TestTokens.Create(secret: "another-secret-with-at-least-32-bytes!!")),
            "other-audience" => Bearer(TestTokens.Create(audience: "someone-else")),
            "expired" => Bearer(TestTokens.Create(expires: DateTime.UtcNow.AddMinutes(-1))),
            _ => throw new ArgumentOutOfRangeException(nameof(authCase)),
        };

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
        var error = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error");
        Assert.Equal("unauthorized", error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task GetStats_WithTokenFromAuthEndpoint_ReturnsPortalStats()
    {
        var stats = await GetArrayAsync("/dashboard/stats", await IssueTokenAsync());

        Assert.Equal(4, stats.Length);
        Assert.Equal("Solicitudes totales", stats[0].GetProperty("label").GetString());
        Assert.Equal("1.284", stats[0].GetProperty("value").GetString());
        Assert.Equal("+12 % esta semana", stats[0].GetProperty("detail").GetString());
        Assert.Equal("APIs integradas", stats[2].GetProperty("label").GetString());
        Assert.Equal("Portal · Petstore · PokeAPI", stats[2].GetProperty("detail").GetString());
    }

    [Fact]
    public async Task GetActivity_WithoutStatus_ReturnsAllActivity()
    {
        var activity = await GetArrayAsync("/dashboard/activity", TestTokens.Create());

        Assert.Equal([1, 2, 3, 4, 5], activity.Select(item => item.GetProperty("id").GetInt32()));
        Assert.Equal(
            [
                ("GET /api/v2/pokemon/pikachu", "PokeAPI", "hace 2 min", "success"),
                ("POST /auth/token", "Portal API", "hace 5 min", "success"),
                ("POST /pet", "Petstore", "hace 12 min", "success"),
                ("DELETE /pet/5", "Petstore", "hace 1 h", "error"),
                ("GET /v2/apis", "Portal API", "hace 2 h", "pending"),
            ],
            activity.Select(item => (
                item.GetProperty("action").GetString(),
                item.GetProperty("api").GetString(),
                item.GetProperty("time").GetString(),
                item.GetProperty("status").GetString())));
    }

    [Theory]
    [InlineData("error", new[] { 4 })]
    [InlineData("pending", new[] { 5 })]
    [InlineData("success", new[] { 1, 2, 3 })]
    public async Task GetActivity_WithStatus_FiltersByStatus(string status, int[] expectedIds)
    {
        var activity = await GetArrayAsync($"/dashboard/activity?status={status}", TestTokens.Create());

        Assert.Equal(expectedIds, activity.Select(item => item.GetProperty("id").GetInt32()));
    }

    [Fact]
    public async Task GetActivity_WithUnknownStatus_ReturnsValidationError()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/dashboard/activity?status=otro");
        request.Headers.Authorization = Bearer(TestTokens.Create());

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error");
        Assert.Equal("validation_error", error.GetProperty("code").GetString());
        Assert.Contains("status", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task OpenApiDocument_DeclaresBearerSecurityOnlyOnDashboard()
    {
        var document = await _client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");

        var scheme = document.GetProperty("components").GetProperty("securitySchemes").GetProperty("bearerAuth");
        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
        var paths = document.GetProperty("paths");
        foreach (var path in new[] { "/dashboard/stats", "/dashboard/activity" })
        {
            var operation = paths.GetProperty(path).GetProperty("get");
            Assert.True(operation.GetProperty("security")[0].TryGetProperty("bearerAuth", out _));
            Assert.True(operation.GetProperty("responses").TryGetProperty("401", out _));
        }

        Assert.False(paths.GetProperty("/apis").GetProperty("get").TryGetProperty("security", out _));
        Assert.False(paths.GetProperty("/auth/token").GetProperty("post").TryGetProperty("security", out _));
    }

    private async Task<string> IssueTokenAsync()
    {
        var response = await _client.PostAsJsonAsync("/auth/token", new { username = "demo", password = "demo1234" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
    }

    private async Task<JsonElement[]> GetArrayAsync(string url, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = Bearer(token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return [.. (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()];
    }

    private static AuthenticationHeaderValue Bearer(string token) => new("Bearer", token);
}
