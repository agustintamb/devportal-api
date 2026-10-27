using Dapper;
using DevportalApi.Data;

namespace DevportalApi.Endpoints;

public static class ClientsEndpoints
{
    public static IEndpointRouteBuilder MapClientsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/clients").WithTags("Clientes");

        group.MapGet("", ListClientsAsync)
            .WithName("listClients")
            .WithSummary("Listar los clientes que usan las APIs")
            .Produces<IReadOnlyList<ClientDto>>();

        return app;
    }

    private static async Task<IResult> ListClientsAsync(Database database)
    {
        await using var connection = await database.OpenConnectionAsync();
        var clients = await connection.QueryAsync<ClientDto>(
            "SELECT name AS Name, slug AS Slug FROM clients ORDER BY position");

        return TypedResults.Ok(clients);
    }
}
