using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DevportalApi.Tests;

public class ErrorTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string ThrowingPath = "/test/throw";
    private const string SecretDetail = "secret-internal-detail";

    [Fact]
    public async Task UnknownRoute_ReturnsNotFoundError()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = await ReadErrorAsync(response);
        Assert.Equal("not_found", error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task WrongMethod_ReturnsMethodNotAllowedError()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsync("/health", content: null);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        var error = await ReadErrorAsync(response);
        Assert.Equal("method_not_allowed", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task UnhandledException_ReturnsInternalErrorWithoutDetails()
    {
        using var throwingFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, ThrowingEndpointStartupFilter>()));
        var client = throwingFactory.CreateClient();

        var response = await client.GetAsync(ThrowingPath);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(SecretDetail, raw);
        Assert.DoesNotContain(nameof(InvalidOperationException), raw);
        var error = await ReadErrorAsync(response);
        Assert.Equal("internal_error", error.GetProperty("code").GetString());
    }

    private static async Task<JsonElement> ReadErrorAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("error");
    }

    // Test-only: selects an endpoint that throws, so the real exception-handling pipeline runs.
    private sealed class ThrowingEndpointStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (context.Request.Path == ThrowingPath)
                {
                    context.SetEndpoint(new Endpoint(
                        _ => throw new InvalidOperationException(SecretDetail),
                        EndpointMetadataCollection.Empty,
                        "Throwing test endpoint"));
                }

                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
