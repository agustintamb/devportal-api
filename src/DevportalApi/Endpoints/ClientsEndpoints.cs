using System.ComponentModel;
using Dapper;
using DevportalApi.Data;
using DevportalApi.Infrastructure;

namespace DevportalApi.Endpoints;

public static class ClientsEndpoints
{
    public static IEndpointRouteBuilder MapClientsEndpoints(this IEndpointRouteBuilder app, PortalApiVersion version)
    {
        var group = app.MapGroup("/clients").WithTags("Clientes");

        var list = version == PortalApiVersion.V1
            ? group.MapGet("", ListClientsAsync).Produces<IReadOnlyList<ClientDto>>()
            : group.MapGet("", ListClientsPageAsync)
                .Produces<PagedResponse<ClientDto>>()
                .ProducesError(StatusCodes.Status400BadRequest, Paging.ErrorDescription);
        list.WithName(version.EndpointName("listClients"))
            .WithSummary("Listar los clientes que usan las APIs");

        return app;
    }

    private static async Task<IResult> ListClientsAsync(Database database) =>
        TypedResults.Ok(await QueryClientsAsync(database));

    private static async Task<IResult> ListClientsPageAsync(
        Database database,
        [Description(Paging.PageDescription)] int page = Paging.DefaultPage,
        [Description(Paging.PageSizeDescription)] int pageSize = Paging.DefaultPageSize) =>
        Paging.Validate(page, pageSize)
        ?? TypedResults.Ok(Paging.Paginate(await QueryClientsAsync(database), page, pageSize));

    private static async Task<List<ClientDto>> QueryClientsAsync(Database database)
    {
        await using var connection = await database.OpenConnectionAsync();
        var clients = await connection.QueryAsync<ClientDto>(
            "SELECT name AS Name, slug AS Slug FROM clients ORDER BY position");

        return clients.AsList();
    }
}
