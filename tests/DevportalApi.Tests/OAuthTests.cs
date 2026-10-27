using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace DevportalApi.Tests;

public class OAuthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string ClientId = "portal-demo";
    private const string ClientSecret = "portal-demo-secret";

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task PostToken_WithClientCredentialsInBody_ReturnsScopedTokenForDemoUser()
    {
        var response = await RequestTokenAsync(Form(("client_id", ClientId), ("client_secret", ClientSecret)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Bearer", body.GetProperty("token_type").GetString());
        Assert.Equal(3600, body.GetProperty("expires_in").GetInt32());
        Assert.Equal("subscriptions:read", body.GetProperty("scope").GetString());

        var validation = await TestTokens.ValidateAsync(body.GetProperty("access_token").GetString()!);
        Assert.True(validation.IsValid, validation.Exception?.Message);
        Assert.Equal("demo", validation.Claims["sub"]);
        Assert.Equal("subscriptions:read", validation.Claims["scope"]);
    }

    [Fact]
    public async Task PostToken_WithClientCredentialsInBasicHeader_ReturnsToken()
    {
        var response = await RequestTokenAsync(Form(("scope", "subscriptions:read")), BasicHeader(ClientId, ClientSecret));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("access_token").GetString()));
    }

    [Theory]
    [InlineData("portal-demo", "wrong")]
    [InlineData("other", "portal-demo-secret")]
    [InlineData("", "")]
    public async Task PostToken_WithWrongClientCredentials_ReturnsInvalidClient(string clientId, string clientSecret)
    {
        var response = await RequestTokenAsync(Form(("client_id", clientId), ("client_secret", clientSecret)));

        await AssertOAuthErrorAsync(response, HttpStatusCode.Unauthorized, "invalid_client");
    }

    [Fact]
    public async Task PostToken_WithWrongBasicCredentials_ReturnsInvalidClientWithBasicChallenge()
    {
        var response = await RequestTokenAsync(Form(), BasicHeader(ClientId, "wrong"));

        await AssertOAuthErrorAsync(response, HttpStatusCode.Unauthorized, "invalid_client");
        Assert.Equal("Basic", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
    }

    [Fact]
    public async Task PostToken_WithUnsupportedGrantType_ReturnsUnsupportedGrantType()
    {
        var response = await RequestTokenAsync(
            Form(("client_id", ClientId), ("client_secret", ClientSecret)), grantType: "password");

        await AssertOAuthErrorAsync(response, HttpStatusCode.BadRequest, "unsupported_grant_type");
    }

    [Fact]
    public async Task PostToken_WithScopeTheClientDoesNotHave_ReturnsInvalidScope()
    {
        var response = await RequestTokenAsync(
            Form(("client_id", ClientId), ("client_secret", ClientSecret), ("scope", "subscriptions:read subscriptions:write")));

        await AssertOAuthErrorAsync(response, HttpStatusCode.BadRequest, "invalid_scope");
    }

    [Fact]
    public async Task PostToken_WithoutBody_ReturnsInvalidRequest()
    {
        var response = await _client.PostAsync("/oauth/token", null);

        await AssertOAuthErrorAsync(response, HttpStatusCode.BadRequest, "invalid_request");
    }

    // Routing rejects a body that isn't a form before the endpoint runs, like any HTTP-level error (404, 405).
    [Fact]
    public async Task PostToken_WithJsonBody_ReturnsUnsupportedMediaType()
    {
        var response = await _client.PostAsync(
            "/oauth/token",
            new StringContent("""{ "grant_type": "client_credentials" }""", Encoding.UTF8, "application/json"));

        await AssertErrorAsync(response, HttpStatusCode.UnsupportedMediaType, "unsupported_media_type");
    }

    [Fact]
    public async Task ClientToken_ListsTheDemoUserSubscriptions()
    {
        var created = await SendAsync(
            HttpMethod.Post, "/subscriptions", TestTokens.Create(), """{ "apiId": "blog", "plan": "free" }""");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var response = await SendAsync(HttpMethod.Get, "/subscriptions", await IssueClientTokenAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("blog", list.EnumerateArray().Select(item => item.GetProperty("apiId").GetString()));
    }

    [Fact]
    public async Task ClientTokenWithoutReadScope_IsForbiddenOnSubscriptions()
    {
        var response = await SendAsync(HttpMethod.Get, "/subscriptions", TestTokens.Create(scope: "other:scope"));

        await AssertErrorAsync(response, HttpStatusCode.Forbidden, "forbidden");
    }

    [Theory]
    [InlineData("GET", "/dashboard/stats", null)]
    [InlineData("POST", "/subscriptions", """{ "apiId": "petstore", "plan": "free" }""")]
    public async Task ClientToken_IsForbiddenOnEndpointsOutsideItsScopes(string method, string path, string? body)
    {
        var response = await SendAsync(new HttpMethod(method), path, await IssueClientTokenAsync(), body);

        await AssertErrorAsync(response, HttpStatusCode.Forbidden, "forbidden");
    }

    private async Task<string> IssueClientTokenAsync()
    {
        var response = await RequestTokenAsync(Form(("client_id", ClientId), ("client_secret", ClientSecret)));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString()!;
    }

    private Task<HttpResponseMessage> RequestTokenAsync(
        List<KeyValuePair<string, string>> form,
        AuthenticationHeaderValue? authorization = null,
        string grantType = "client_credentials")
    {
        form.Insert(0, new("grant_type", grantType));
        var request = new HttpRequestMessage(HttpMethod.Post, "/oauth/token") { Content = new FormUrlEncodedContent(form) };
        request.Headers.Authorization = authorization;
        return _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string token, string? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return _client.SendAsync(request);
    }

    private static List<KeyValuePair<string, string>> Form(params (string Key, string Value)[] fields) =>
        [.. fields.Select(field => new KeyValuePair<string, string>(field.Key, field.Value))];

    // RFC 6749 §2.3.1: id and secret are form-urlencoded before joining them with ':'.
    private static AuthenticationHeaderValue BasicHeader(string clientId, string clientSecret) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"{Uri.EscapeDataString(clientId)}:{Uri.EscapeDataString(clientSecret)}")));

    private static async Task AssertOAuthErrorAsync(HttpResponseMessage response, HttpStatusCode status, string error)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(error, body.GetProperty("error").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("error_description").GetString()));
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var error = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error");
        Assert.Equal(code, error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
    }
}
