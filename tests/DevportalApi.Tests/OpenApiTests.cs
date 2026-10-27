using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.WebUtilities;

namespace DevportalApi.Tests;

public class OpenApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string ErrorResponseRef = "#/components/schemas/ErrorResponse";

    private static readonly string[] _privateOperations =
    [
        "get /dashboard/stats",
        "get /dashboard/activity",
        "get /subscriptions",
        "post /subscriptions",
        "get /subscriptions/{id}",
        "patch /subscriptions/{id}",
        "delete /subscriptions/{id}",
    ];

    private static readonly string[] _publicOperations =
    [
        "get /health",
        "get /apis",
        "get /apis/{id}",
        "get /clients",
        "post /auth/token",
    ];

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Document_ListsExactlyTheApiOperations()
    {
        var document = await GetDocumentAsync();

        var operations = Operations(document).Select(operation => operation.Key).Order();

        Assert.Equal(_publicOperations.Concat(_privateOperations).Order(), operations);
    }

    [Fact]
    public async Task PrivateOperations_DeclareBearerSecurityAndUnauthorizedErrorResponse()
    {
        var operations = Operations(await GetDocumentAsync());

        foreach (var key in _privateOperations)
        {
            var operation = operations[key];
            var security = Assert.Single(operation.GetProperty("security").EnumerateArray());
            Assert.True(security.TryGetProperty("bearerAuth", out _), key);
            Assert.Equal(ErrorResponseRef, ResponseSchemaRef(operation, "401"));
        }
    }

    [Fact]
    public async Task PublicOperations_DeclareNoSecurity()
    {
        var operations = Operations(await GetDocumentAsync());

        foreach (var key in _publicOperations)
        {
            Assert.False(operations[key].TryGetProperty("security", out _), key);
        }
    }

    [Fact]
    public async Task EveryOperation_HasOperationIdSummaryAndSpanishResponseDescriptions()
    {
        foreach (var (key, operation) in Operations(await GetDocumentAsync()))
        {
            Assert.False(string.IsNullOrWhiteSpace(operation.GetProperty("operationId").GetString()), key);
            Assert.False(string.IsNullOrWhiteSpace(operation.GetProperty("summary").GetString()), key);
            foreach (var response in operation.GetProperty("responses").EnumerateObject())
            {
                var description = response.Value.GetProperty("description").GetString();
                var englishDefault = ReasonPhrases.GetReasonPhrase(int.Parse(response.Name));
                Assert.False(string.IsNullOrWhiteSpace(description), $"{key} {response.Name}");
                Assert.NotEqual(englishDefault, description);
            }
        }
    }

    [Fact]
    public async Task CreateToken_DeclaresValidationErrorForMalformedBody()
    {
        var operation = Operations(await GetDocumentAsync())["post /auth/token"];

        Assert.Equal(ErrorResponseRef, ResponseSchemaRef(operation, "400"));
        Assert.Equal(ErrorResponseRef, ResponseSchemaRef(operation, "401"));
    }

    [Theory]
    [InlineData("SubscriptionDto", "plan", new[] { "free", "pro" })]
    [InlineData("CreateSubscriptionRequest", "plan", new[] { "free", "pro" })]
    [InlineData("UpdateSubscriptionRequest", "plan", new[] { "free", "pro" })]
    [InlineData("ActivityDto", "status", new[] { "success", "error", "pending" })]
    public async Task SchemaProperty_WithFixedValues_IsStringEnum(string schemaName, string property, string[] values)
    {
        var document = await GetDocumentAsync();

        var schema = document.GetProperty("components").GetProperty("schemas").GetProperty(schemaName)
            .GetProperty("properties").GetProperty(property);

        Assert.Equal("string", schema.GetProperty("type").GetString());
        Assert.Equal(values, schema.GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public async Task CreateSubscriptionApiId_IsNonNullableString()
    {
        var document = await GetDocumentAsync();

        var schema = document.GetProperty("components").GetProperty("schemas").GetProperty("CreateSubscriptionRequest")
            .GetProperty("properties").GetProperty("apiId");

        Assert.Equal("string", schema.GetProperty("type").GetString());
    }

    [Fact]
    public async Task ActivityStatusParameter_IsStringEnum()
    {
        var operation = Operations(await GetDocumentAsync())["get /dashboard/activity"];

        var parameter = Assert.Single(operation.GetProperty("parameters").EnumerateArray());
        var schema = parameter.GetProperty("schema");
        Assert.Equal("status", parameter.GetProperty("name").GetString());
        Assert.Equal("string", schema.GetProperty("type").GetString());
        Assert.Equal(["success", "error", "pending"], schema.GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
    }

    [Theory]
    [InlineData("TokenResponse", "expiresIn")]
    [InlineData("ActivityDto", "id")]
    public async Task NumericProperty_IsDeclaredAsIntegerOnly(string schemaName, string property)
    {
        var document = await GetDocumentAsync();

        var schema = document.GetProperty("components").GetProperty("schemas").GetProperty(schemaName)
            .GetProperty("properties").GetProperty(property);

        Assert.Equal("integer", schema.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Document_DescribesPortalApiWithTestFlow()
    {
        var info = (await GetDocumentAsync()).GetProperty("info");

        Assert.Equal("Portal API", info.GetProperty("title").GetString());
        Assert.Equal("1.0.0", info.GetProperty("version").GetString());
        var description = info.GetProperty("description").GetString();
        Assert.Contains("POST /auth/token", description);
        Assert.Contains("demo1234", description);
        Assert.Contains("accessToken", description);
        // The raw string comes from a .cs file whose line endings depend on the git checkout.
        Assert.DoesNotContain("\r", description);
    }

    [Fact]
    public async Task EveryParameter_HasDescription()
    {
        foreach (var (key, operation) in Operations(await GetDocumentAsync()))
        {
            if (!operation.TryGetProperty("parameters", out var parameters))
            {
                continue;
            }

            foreach (var parameter in parameters.EnumerateArray())
            {
                Assert.True(
                    parameter.TryGetProperty("description", out var description) && description.GetString() is { Length: > 0 },
                    $"{key} {parameter.GetProperty("name").GetString()}");
            }
        }
    }

    [Fact]
    public async Task Document_ListsProductionAndLocalServers()
    {
        var servers = (await GetDocumentAsync()).GetProperty("servers").EnumerateArray()
            .Select(server => (server.GetProperty("url").GetString(), server.GetProperty("description").GetString()));

        Assert.Equal(
            [("https://devportal-api.onrender.com", "Producción"), ("http://localhost:5080", "Local")],
            servers);
    }

    [Fact]
    public async Task Document_TakesProductionServerFromProdUrl()
    {
        using var prodFactory = factory.WithWebHostBuilder(
            builder => builder.UseSetting("PROD_URL", "https://portal-api.example.com"));

        var document = await prodFactory.CreateClient().GetFromJsonAsync<JsonElement>("/openapi/v1.json");

        Assert.Equal("https://portal-api.example.com", document.GetProperty("servers")[0].GetProperty("url").GetString());
    }

    [Fact]
    public async Task Document_DescribesEveryTag()
    {
        var tags = (await GetDocumentAsync()).GetProperty("tags").EnumerateArray().ToList();

        Assert.Equal(
            ["Salud", "Catálogo", "Clientes", "Autenticación", "Dashboard", "Suscripciones"],
            tags.Select(tag => tag.GetProperty("name").GetString()));
        Assert.All(tags, tag => Assert.False(string.IsNullOrWhiteSpace(tag.GetProperty("description").GetString())));
    }

    // Fails when the code changes the spec but the committed file was not regenerated with `dotnet build`.
    [Fact]
    public async Task CommittedSpecFile_MatchesServedDocument()
    {
        var committedPath = Path.Combine(RepositoryRoot(), "openapi", "portal-api.json");
        Assert.True(File.Exists(committedPath), $"Missing {committedPath}; run `dotnet build` to generate it.");

        var committed = JsonNode.Parse(await File.ReadAllTextAsync(committedPath));
        var served = JsonNode.Parse(await _client.GetStringAsync("/openapi/v1.json"));

        Assert.True(
            JsonNode.DeepEquals(committed, served),
            "openapi/portal-api.json is out of date; run `dotnet build` and commit the regenerated file.");
    }

    private async Task<JsonElement> GetDocumentAsync() =>
        await _client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");

    // Flattens "paths" into "method path" → operation, e.g. "get /apis/{id}".
    private static Dictionary<string, JsonElement> Operations(JsonElement document) =>
        document.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject()
                .Select(operation => ($"{operation.Name} {path.Name}", operation.Value)))
            .ToDictionary(pair => pair.Item1, pair => pair.Value);

    private static string? ResponseSchemaRef(JsonElement operation, string status) =>
        operation.GetProperty("responses").GetProperty(status)
            .GetProperty("content").GetProperty("application/json")
            .GetProperty("schema").GetProperty("$ref").GetString();

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DevportalApi.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("DevportalApi.sln was not found.");
    }
}
