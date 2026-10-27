using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace DevportalApi.Tests;

public class ApisTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetApis_ReturnsWholeCatalogInPortalOrder()
    {
        var apis = await GetApisAsync("/apis");

        Assert.Equal(["portal", "petstore", "pokeapi"], Ids(apis));
    }

    [Fact]
    public async Task GetApis_ReturnsApiFields()
    {
        var pokeapi = (await GetApisAsync("/apis")).Single(api => api.GetProperty("id").GetString() == "pokeapi");

        Assert.Equal("PokeAPI", pokeapi.GetProperty("name").GetString());
        Assert.StartsWith("Datos de Pokémon", pokeapi.GetProperty("description").GetString());
        Assert.Equal("stable", pokeapi.GetProperty("status").GetString());
        Assert.Equal("public", pokeapi.GetProperty("visibility").GetString());
        Assert.Equal("Datos abiertos", pokeapi.GetProperty("category").GetString());
        Assert.Equal(["pokemon", "juegos", "items"], pokeapi.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()));
        Assert.Equal("v2.10.0", pokeapi.GetProperty("version").GetString());
    }

    [Theory]
    [InlineData("pokemon", "pokeapi")]
    [InlineData("POKEMON", "pokeapi")]
    [InlineData("mascotas", "petstore")]
    [InlineData("clasico", "petstore")]
    [InlineData("suscripciones", "portal")]
    [InlineData("users", "petstore")]
    [InlineData("graphql", "")]
    public async Task GetApis_WithQuery_FiltersByNameDescriptionAndTagsIgnoringCaseAndAccents(string query, string expectedIds)
    {
        var apis = await GetApisAsync($"/apis?q={query}");

        Assert.Equal(expectedIds.Split(',', StringSplitOptions.RemoveEmptyEntries), Ids(apis));
    }

    [Theory]
    [InlineData("Datos abiertos")]
    [InlineData("datos abiertos")]
    public async Task GetApis_WithCategory_FiltersByExactCategoryIgnoringCase(string category)
    {
        var apis = await GetApisAsync($"/apis?category={Uri.EscapeDataString(category)}");

        Assert.Equal(["pokeapi"], Ids(apis));
    }

    [Fact]
    public async Task GetApis_WithQueryAndCategory_AppliesBothFilters()
    {
        var apis = await GetApisAsync("/apis?q=users&category=Ejemplo");

        Assert.Equal(["petstore"], Ids(apis));
    }

    [Fact]
    public async Task GetApis_WithEmptyFilters_ReturnsWholeCatalog()
    {
        var apis = await GetApisAsync("/apis?q=&category=");

        Assert.Equal(3, apis.Length);
    }

    [Fact]
    public async Task GetApiById_ReturnsApi()
    {
        var response = await _client.GetAsync("/apis/portal");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var api = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("portal", api.GetProperty("id").GetString());
        Assert.Equal("Portal API", api.GetProperty("name").GetString());
        Assert.Equal("beta", api.GetProperty("status").GetString());
        Assert.Equal("internal", api.GetProperty("visibility").GetString());
        Assert.Equal("Plataforma", api.GetProperty("category").GetString());
        Assert.Equal("v1 / v2", api.GetProperty("version").GetString());
    }

    [Fact]
    public async Task GetApiById_UnknownId_ReturnsNotFoundError()
    {
        var response = await _client.GetAsync("/apis/nope");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error");
        Assert.Equal("not_found", error.GetProperty("code").GetString());
        Assert.Contains("'nope'", error.GetProperty("message").GetString());
    }

    private async Task<JsonElement[]> GetApisAsync(string url)
    {
        var response = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return [.. (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()];
    }

    private static string[] Ids(JsonElement[] apis) => [.. apis.Select(api => api.GetProperty("id").GetString() ?? "")];
}
