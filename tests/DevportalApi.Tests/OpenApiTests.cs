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
        "post /oauth/token",
    ];

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Document_ListsExactlyTheApiOperations()
    {
        var document = await GetDocumentAsync();

        var operations = Operations(document).Select(operation => operation.Key).Order();

        Assert.Equal(_publicOperations.Concat(_privateOperations).Append("get /usage").Order(), operations);
    }

    [Fact]
    public async Task PrivateOperations_DeclareBearerSecurityAndUnauthorizedAndForbiddenErrorResponses()
    {
        var operations = Operations(await GetDocumentAsync());

        foreach (var key in _privateOperations)
        {
            var operation = operations[key];
            var security = operation.GetProperty("security").EnumerateArray().ToList();
            Assert.True(security[0].TryGetProperty("bearerAuth", out _), key);
            Assert.Equal(key == "get /subscriptions" ? 2 : 1, security.Count);
            Assert.Equal(ErrorResponseRef, ResponseSchemaRef(operation, "401"));
            Assert.Equal(ErrorResponseRef, ResponseSchemaRef(operation, "403"));
        }
    }

    [Fact]
    public async Task ListSubscriptions_AcceptsOAuth2WithReadScopeAsAlternative()
    {
        var operation = Operations(await GetDocumentAsync())["get /subscriptions"];

        var oauth2 = operation.GetProperty("security")[1].GetProperty("oauth2");
        Assert.Equal(["subscriptions:read"], oauth2.EnumerateArray().Select(scope => scope.GetString()));
    }

    [Fact]
    public async Task Usage_DeclaresApiKeySecurityAndUnauthorizedErrorResponse()
    {
        var operation = Operations(await GetDocumentAsync())["get /usage"];

        var security = Assert.Single(operation.GetProperty("security").EnumerateArray());
        Assert.True(security.TryGetProperty("apiKeyAuth", out _));
        Assert.Equal(ErrorResponseRef, ResponseSchemaRef(operation, "401"));
        Assert.False(operation.GetProperty("responses").TryGetProperty("403", out _));
    }

    [Fact]
    public async Task Document_DeclaresTheThreeSecuritySchemes()
    {
        var schemes = (await GetDocumentAsync()).GetProperty("components").GetProperty("securitySchemes");

        Assert.Equal(["apiKeyAuth", "bearerAuth", "oauth2"], schemes.EnumerateObject().Select(scheme => scheme.Name).Order());
        Assert.Equal("bearer", schemes.GetProperty("bearerAuth").GetProperty("scheme").GetString());

        var apiKey = schemes.GetProperty("apiKeyAuth");
        Assert.Equal("apiKey", apiKey.GetProperty("type").GetString());
        Assert.Equal("header", apiKey.GetProperty("in").GetString());
        Assert.Equal("X-API-Key", apiKey.GetProperty("name").GetString());
        Assert.Contains("demo-api-key", apiKey.GetProperty("description").GetString());

        var oauth2 = schemes.GetProperty("oauth2");
        Assert.Equal("oauth2", oauth2.GetProperty("type").GetString());
        var flow = oauth2.GetProperty("flows").GetProperty("clientCredentials");
        Assert.Equal("/oauth/token", flow.GetProperty("tokenUrl").GetString());
        Assert.True(flow.GetProperty("scopes").TryGetProperty("subscriptions:read", out _));
        Assert.Contains("portal-demo-secret", oauth2.GetProperty("description").GetString());
    }

    [Fact]
    public async Task OAuthToken_DeclaresFormBodyAndOAuthErrors()
    {
        var operation = Operations(await GetDocumentAsync())["post /oauth/token"];

        var form = operation.GetProperty("requestBody").GetProperty("content").GetProperty("application/x-www-form-urlencoded");
        Assert.True(form.GetProperty("schema").TryGetProperty("$ref", out _));
        Assert.Equal("#/components/schemas/OAuthErrorResponse", ResponseSchemaRef(operation, "400"));
        Assert.Equal("#/components/schemas/OAuthErrorResponse", ResponseSchemaRef(operation, "401"));
        Assert.Equal(ErrorResponseRef, ResponseSchemaRef(operation, "415"));
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
    [InlineData("ApiDto", "visibility", new[] { "internal", "public" })]
    public async Task SchemaProperty_WithFixedValues_IsStringEnum(string schemaName, string property, string[] values)
    {
        var document = await GetDocumentAsync();

        var schema = document.GetProperty("components").GetProperty("schemas").GetProperty(schemaName)
            .GetProperty("properties").GetProperty(property);

        Assert.Equal("string", schema.GetProperty("type").GetString());
        Assert.Equal(values, schema.GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
    }

    [Theory]
    [InlineData("CreateSubscriptionRequest", "apiId")]
    [InlineData("OAuthTokenRequest", "client_id")]
    [InlineData("OAuthTokenRequest", "scope")]
    public async Task OptionalRequestField_IsNonNullableString(string schemaName, string property)
    {
        var document = await GetDocumentAsync();

        var schema = document.GetProperty("components").GetProperty("schemas").GetProperty(schemaName)
            .GetProperty("properties").GetProperty(property);

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
            ["Salud", "Autenticación", "Catálogo", "Clientes", "Dashboard", "Suscripciones", "Uso"],
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
