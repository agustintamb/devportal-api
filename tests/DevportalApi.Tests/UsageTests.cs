using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace DevportalApi.Tests;

public class UsageTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong-key")]
    public async Task GetUsage_WithoutValidApiKey_ReturnsUnauthorizedError(string? apiKey)
    {
        var response = await _client.SendAsync(UsageRequest(apiKey));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var error = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error");
        Assert.Equal("unauthorized", error.GetProperty("code").GetString());
        Assert.Contains("X-API-Key", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task GetUsage_WithUserBearerToken_ReturnsUnauthorized()
    {
        var request = UsageRequest(null);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create());

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetUsage_WithDemoApiKey_ReturnsUsageByApi()
    {
        var response = await _client.SendAsync(UsageRequest("demo-api-key"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var usage = await response.Content.ReadFromJsonAsync<JsonElement>();
        var byApi = usage.GetProperty("byApi").EnumerateArray().ToList();
        Assert.Equal(["petstore", "blog", "auth"], byApi.Select(item => item.GetProperty("apiId").GetString()));
        Assert.Equal(usage.GetProperty("requestsThisMonth").GetInt32(), byApi.Sum(item => item.GetProperty("requests").GetInt32()));
        Assert.True(usage.GetProperty("monthlyLimit").GetInt32() > 0);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithOnlyApiKey_ReturnsUnauthorized()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/dashboard/stats");
        request.Headers.Add("X-API-Key", "demo-api-key");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static HttpRequestMessage UsageRequest(string? apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/usage");
        if (apiKey is not null)
        {
            request.Headers.Add("X-API-Key", apiKey);
        }

        return request;
    }
}
