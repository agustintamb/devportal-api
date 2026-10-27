using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace DevportalApi.Tests;

public class AuthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task PostToken_WithDemoCredentials_ReturnsBearerTokenForOneHour()
    {
        var response = await _client.PostAsJsonAsync("/auth/token", new { username = "demo", password = "demo1234" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Bearer", body.GetProperty("tokenType").GetString());
        Assert.Equal(3600, body.GetProperty("expiresIn").GetInt32());

        var validation = await TestTokens.ValidateAsync(body.GetProperty("accessToken").GetString()!);
        Assert.True(validation.IsValid, validation.Exception?.Message);
        Assert.Equal("demo", validation.Claims["sub"]);
        var expectedExpiry = DateTime.UtcNow.AddSeconds(3600);
        Assert.InRange(validation.SecurityToken.ValidTo, expectedExpiry.AddSeconds(-30), expectedExpiry.AddSeconds(30));
    }

    [Theory]
    [InlineData("demo", "wrong")]
    [InlineData("other", "demo1234")]
    [InlineData("", "")]
    public async Task PostToken_WithWrongCredentials_ReturnsInvalidCredentialsError(string username, string password)
    {
        var response = await _client.PostAsJsonAsync("/auth/token", new { username, password });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var error = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error");
        Assert.Equal("invalid_credentials", error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
    }

    [Theory]
    [InlineData("Development", "")]
    [InlineData("Development", "{not json")]
    [InlineData("Production", "{not json")]
    public async Task PostToken_WithMalformedBody_ReturnsValidationError(string environment, string body)
    {
        using var envFactory = factory.WithWebHostBuilder(builder => builder.UseEnvironment(environment));

        var response = await envFactory.CreateClient().PostAsync(
            "/auth/token", new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error");
        Assert.Equal("validation_error", error.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("too-short-secret")]
    public void Startup_WithMissingOrShortJwtSecret_FailsWithClearMessage(string secret)
    {
        using var badFactory = factory.WithWebHostBuilder(builder => builder.UseSetting("JWT_SECRET", secret));

        var exception = Record.Exception(() => badFactory.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains("JWT_SECRET", exception.ToString());
    }
}
