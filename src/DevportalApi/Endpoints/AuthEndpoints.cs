using DevportalApi.Infrastructure;

namespace DevportalApi.Endpoints;

public static class AuthEndpoints
{
    // Public on purpose: this is a demo API and the credentials are documented in the spec.
    private const string DemoUsername = "demo";
    private const string DemoPassword = "demo1234";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Autenticación");

        group.MapPost("/token", CreateToken)
            .WithName("createToken")
            .WithSummary("Obtener un token de acceso con el usuario demo")
            .Produces<TokenResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);

        return app;
    }

    // `request` is a complex type, so ASP.NET binds it from the JSON body.
    private static IResult CreateToken(TokenRequest request, Tokens tokens)
    {
        if (request.Username != DemoUsername || request.Password != DemoPassword)
        {
            return ApiErrors.Problem(StatusCodes.Status401Unauthorized, "invalid_credentials", "Usuario o contraseña incorrectos.");
        }

        return TypedResults.Ok(new TokenResponse(
            tokens.Create(request.Username),
            "Bearer",
            (int)Tokens.Lifetime.TotalSeconds));
    }
}
