using System.Net;

namespace DevportalApi.Tests;

public class CorsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Preflight_FromAllowedOrigin_EchoesOrigin()
    {
        var response = await _client.SendAsync(Preflight(ApiFactory.AllowedOrigin));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(ApiFactory.AllowedOrigin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    [Fact]
    public async Task Preflight_FromAllowedOrigin_AllowsAuthHeaders()
    {
        var request = Preflight(ApiFactory.AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Headers", "content-type,authorization,x-api-key");

        var response = await _client.SendAsync(request);

        var allowedHeaders = string.Join(',', response.Headers.GetValues("Access-Control-Allow-Headers")).ToLowerInvariant();
        Assert.Contains("content-type", allowedHeaders);
        Assert.Contains("authorization", allowedHeaders);
        Assert.Contains("x-api-key", allowedHeaders);
    }

    [Fact]
    public async Task Preflight_FromUnknownOrigin_OmitsAllowOriginHeader()
    {
        var response = await _client.SendAsync(Preflight("https://evil.example"));

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    private static HttpRequestMessage Preflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/health");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        return request;
    }
}
