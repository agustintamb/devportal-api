using System.Net;
using System.Text;
using DevportalApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace DevportalApi.Endpoints;

// OAuth 2.0 token endpoint (RFC 6749) for the client credentials grant. Its responses and errors follow the
// RFC ({ error, error_description }) instead of the API's error format, so standard OAuth clients understand them.
public static class OAuthEndpoints
{
    // Public on purpose, like the demo user: the client is documented in the spec.
    private const string DemoClientId = "portal-demo";
    private const string DemoClientSecret = "portal-demo-secret";

    // The demo client acts on behalf of the demo user, limited to these scopes.
    private static readonly string[] _demoClientScopes = [AuthPolicies.SubscriptionsReadScope];

    public static IEndpointRouteBuilder MapOAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/oauth").WithTags("Autenticación");

        group.MapPost("/token", CreateTokenAsync)
            .WithName("createOAuthToken")
            .WithSummary("Obtener un token con OAuth2 client credentials")
            .WithDescription(
                "Cliente demo, público a propósito: `client_id` `portal-demo` y `client_secret` `portal-demo-secret`, " +
                "en el cuerpo o en el header `Authorization: Basic`. El token actúa en nombre del usuario demo y solo " +
                "sirve para lo que cubren sus scopes: con `subscriptions:read`, `GET /subscriptions`. Las respuestas y " +
                "los errores siguen el estándar OAuth 2.0 (RFC 6749), no el formato de errores del resto de la API.")
            .Accepts<OAuthTokenRequest>("application/x-www-form-urlencoded")
            .Produces<OAuthTokenResponse>()
            .ProducesOAuthError(
                StatusCodes.Status400BadRequest,
                "Falta `grant_type` o el cuerpo (`invalid_request`), el grant no es " +
                "`client_credentials` (`unsupported_grant_type`) o se pidió un scope que el cliente no tiene (`invalid_scope`).")
            .ProducesOAuthError(StatusCodes.Status401Unauthorized, "Cliente o secreto incorrectos (`invalid_client`).")
            .ProducesError(
                StatusCodes.Status415UnsupportedMediaType,
                "El cuerpo no es `application/x-www-form-urlencoded` (`unsupported_media_type`, con el formato de errores de la API).");

        return app;
    }

    // Reads the form by hand so every malformed request gets an OAuth error instead of the API's generic 400.
    private static async Task<IResult> CreateTokenAsync(HttpRequest request, HttpResponse response, Tokens tokens)
    {
        if (!request.HasFormContentType)
        {
            return OAuthError(
                StatusCodes.Status400BadRequest,
                "invalid_request",
                "El cuerpo debe ser un formulario application/x-www-form-urlencoded.");
        }

        var form = await request.ReadFormAsync();
        var grantType = form["grant_type"].ToString();
        if (grantType.Length == 0)
        {
            return OAuthError(StatusCodes.Status400BadRequest, "invalid_request", "Falta el parámetro grant_type.");
        }

        var (clientId, clientSecret) = ClientCredentials(request, form);
        if (clientId != DemoClientId || clientSecret != DemoClientSecret)
        {
            // RFC 6749 §5.2: a 401 invalid_client carries a WWW-Authenticate challenge.
            response.Headers.WWWAuthenticate = "Basic realm=\"devportal-api\"";
            return OAuthError(StatusCodes.Status401Unauthorized, "invalid_client", "Cliente o secreto incorrectos.");
        }

        if (grantType != "client_credentials")
        {
            return OAuthError(
                StatusCodes.Status400BadRequest,
                "unsupported_grant_type",
                "Solo se admite grant_type=client_credentials.");
        }

        var requestedScopes = form["scope"].ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var unknownScopes = requestedScopes.Except(_demoClientScopes).ToList();
        if (unknownScopes.Count > 0)
        {
            return OAuthError(
                StatusCodes.Status400BadRequest,
                "invalid_scope",
                $"El cliente no tiene estos scopes: {string.Join(' ', unknownScopes)}.");
        }

        // Without a scope parameter the client gets all of its scopes (RFC 6749 §3.3 lets the server pick a default).
        var scope = string.Join(' ', requestedScopes.Length == 0 ? _demoClientScopes : requestedScopes.Distinct());
        response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(new OAuthTokenResponse(
            tokens.Create(AuthEndpoints.DemoUsername, scope),
            "Bearer",
            (int)Tokens.Lifetime.TotalSeconds,
            scope));
    }

    // client_secret_basic (Authorization: Basic base64(id:secret)) when the header is present, otherwise client_secret_post.
    private static (string? Id, string? Secret) ClientCredentials(HttpRequest request, IFormCollection form)
    {
        var header = request.Headers.Authorization.ToString();
        if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return (form["client_id"].ToString(), form["client_secret"].ToString());
        }

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..].Trim()));
            var separator = decoded.IndexOf(':');
            // RFC 6749 §2.3.1: id and secret are form-urlencoded before being joined with ':'.
            return separator < 0
                ? (null, null)
                : (WebUtility.UrlDecode(decoded[..separator]), WebUtility.UrlDecode(decoded[(separator + 1)..]));
        }
        catch (FormatException)
        {
            return (null, null);
        }
    }

    private static IResult OAuthError(int status, string error, string description) =>
        TypedResults.Json(new OAuthErrorResponse(error, description), statusCode: status);

    private static RouteHandlerBuilder ProducesOAuthError(this RouteHandlerBuilder builder, int status, string description) =>
        builder.WithMetadata(new ProducesResponseTypeAttribute<OAuthErrorResponse>(status, "application/json")
        {
            Description = description,
        });
}
