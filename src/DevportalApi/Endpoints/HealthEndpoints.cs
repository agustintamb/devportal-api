namespace DevportalApi.Endpoints;

public sealed record HealthResponse(string Status);

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/health").WithTags("Salud");

        group.MapGet("", () => TypedResults.Ok(new HealthResponse("ok")))
            .WithName("getHealth")
            .WithSummary("Consultar el estado de la API");

        return app;
    }
}
