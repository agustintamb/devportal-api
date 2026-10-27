using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace DevportalApi.Tests;

public class ClientsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetClients_ReturnsPortalClientsInOrder()
    {
        var response = await _client.GetAsync("/clients");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var clients = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToArray();
        Assert.Equal(
            ["Google", "Spotify", "Netflix", "Airbnb", "Uber", "Stripe", "Shopify", "GitHub"],
            clients.Select(client => client.GetProperty("name").GetString()));
        Assert.Equal(
            ["google", "spotify", "netflix", "airbnb", "uber", "stripe", "shopify", "github"],
            clients.Select(client => client.GetProperty("slug").GetString()));
    }
}
