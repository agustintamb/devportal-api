using DevportalApi.Infrastructure;

namespace DevportalApi.Endpoints;

public static class UsageEndpoints
{
    // Fixed demo values, consistent with the dashboard stats (1.284 requests; Portal · Petstore · PokeAPI).
    private static readonly UsageDto _usage = new(
        1284,
        10000,
        [new("portal", 418), new("petstore", 642), new("pokeapi", 224)]);

    public static IEndpointRouteBuilder MapUsageEndpoints(this IEndpointRouteBuilder app)
    {
        // The ApiKey policy authenticates with the X-API-Key header instead of a JWT.
        var group = app.MapGroup("/usage").WithTags("Uso").RequireAuthorization(AuthPolicies.ApiKey);

        group.MapGet("", GetUsage)
            .WithName("getUsage")
            .WithSummary("Obtener el consumo del mes de la API key")
            .WithDescription(
                "Requiere la API key demo, pública a propósito: header `X-API-Key: demo-api-key` " +
                "(en la sección Autorización, esquema `apiKeyAuth`).")
            .Produces<UsageDto>();

        return app;
    }

    private static IResult GetUsage() => TypedResults.Ok(_usage);
}
