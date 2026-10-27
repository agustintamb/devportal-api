using DevportalApi.Infrastructure;

namespace DevportalApi.Endpoints;

public static class AuthEndpoints
{
    // Public on purpose: this is a demo API and the credentials are documented in the spec.
    public const string DemoUsername = "demo";
    private const string DemoPassword = "demo1234";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Autenticación");

        group.MapPost("/token", CreateToken)
            .WithName("createToken")
            .WithSummary("Obtener un token de acceso con el usuario demo")
            .WithDescription(
                "Credenciales demo, públicas a propósito: usuario `demo` y contraseña `demo1234`. " +
                "El `accessToken` de la respuesta se pega en la sección Autorización (o se envía como " +
                "`Authorization: Bearer <token>`) para llamar a los endpoints de Dashboard y Suscripciones.")
            .Produces<TokenResponse>()
            .ProducesError(StatusCodes.Status400BadRequest, "Cuerpo vacío o con JSON mal formado (`validation_error`).")
            .ProducesError(StatusCodes.Status401Unauthorized, "Usuario o contraseña incorrectos (`invalid_credentials`).");

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
