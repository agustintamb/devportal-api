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

        Assert.Equal(["petstore", "blog", "auth"], Ids(apis));
    }

    [Fact]
    public async Task GetApis_ReturnsApiFields()
    {
        var blog = (await GetApisAsync("/apis")).Single(api => api.GetProperty("id").GetString() == "blog");

        Assert.Equal("Blog API", blog.GetProperty("name").GetString());
        Assert.StartsWith("Gestión de posts", blog.GetProperty("description").GetString());
        Assert.Equal("stable", blog.GetProperty("status").GetString());
        Assert.Equal("Contenido", blog.GetProperty("category").GetString());
        Assert.Equal(["posts", "comments", "authors"], blog.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()));
        Assert.Equal("v1 / v2", blog.GetProperty("version").GetString());
    }

    [Theory]
    [InlineData("blog", "blog")]
    [InlineData("BLOG", "blog")]
    [InlineData("auth", "blog,auth")]
    [InlineData("paginar", "blog")]
    [InlineData("AUTENTICACION", "auth")]
    [InlineData("comments", "blog")]
    [InlineData("users", "petstore,auth")]
    [InlineData("graphql", "")]
    public async Task GetApis_WithQuery_FiltersByNameDescriptionAndTagsIgnoringCaseAndAccents(string query, string expectedIds)
    {
        var apis = await GetApisAsync($"/apis?q={query}");

        Assert.Equal(expectedIds.Split(',', StringSplitOptions.RemoveEmptyEntries), Ids(apis));
    }

    [Theory]
    [InlineData("Seguridad")]
    [InlineData("seguridad")]
    public async Task GetApis_WithCategory_FiltersByExactCategoryIgnoringCase(string category)
    {
        var apis = await GetApisAsync($"/apis?category={category}");

        Assert.Equal(["auth"], Ids(apis));
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
        var response = await _client.GetAsync("/apis/auth");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var api = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("auth", api.GetProperty("id").GetString());
        Assert.Equal("Auth API", api.GetProperty("name").GetString());
        Assert.Equal("Seguridad", api.GetProperty("category").GetString());
        Assert.Equal("v2.0.0", api.GetProperty("version").GetString());
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
