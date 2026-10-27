using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.OpenApi;

namespace DevportalApi.Infrastructure;

// What the spec needs beyond the endpoint metadata: info, servers, tag descriptions, enums and Spanish texts.
public static class OpenApiDocumentation
{
    public const string DefaultProductionUrl = "https://devportal-api.onrender.com";
    private const string LocalUrl = "http://localhost:5080";

    private const string Description = """
        API de prueba del portal de desarrolladores: catálogo de APIs, clientes, métricas del dashboard,
        suscripciones del usuario y consumo de la API key. Los endpoints de **Dashboard** y **Suscripciones**
        requieren un token, **Uso** requiere una API key y el resto es público.

        **Cómo probar un endpoint privado**

        1. Llamar a `POST /auth/token` con el usuario demo (credenciales públicas a propósito):
           `{ "username": "demo", "password": "demo1234" }`.
        2. Copiar el `accessToken` de la respuesta (vence en una hora).
        3. Pegarlo en la sección **Autorización** (esquema `bearerAuth`); se envía como
           `Authorization: Bearer <token>`.
        4. Llamar a un endpoint privado, por ejemplo `GET /dashboard/stats` o `POST /subscriptions`.

        **Otras formas de autenticación**

        - **API key** (esquema `apiKeyAuth`): `GET /usage` se llama con el header `X-API-Key: demo-api-key`.
        - **OAuth2 client credentials** (esquema `oauth2`): `POST /oauth/token` con el cliente `portal-demo` /
          `portal-demo-secret` emite un token con el scope `subscriptions:read`, que actúa en nombre del
          usuario demo y solo sirve para `GET /subscriptions`; en los demás endpoints privados responde 403.

        Los datos son de demostración: la base se vuelve a sembrar en cada reinicio del servidor, así que
        las suscripciones creadas no son permanentes.
        """;

    private static readonly Dictionary<string, string> _tagDescriptions = new()
    {
        ["Salud"] = "Estado de la API.",
        ["Catálogo"] = "APIs publicadas en el portal, con búsqueda y filtro por categoría.",
        ["Clientes"] = "Empresas que usan las APIs del portal.",
        ["Autenticación"] = "Emisión de tokens JWT: con el usuario demo o con el cliente OAuth2 demo (client credentials).",
        ["Dashboard"] = "Métricas y actividad reciente del portal. Requiere token.",
        ["Suscripciones"] = "Suscripciones del usuario del token a las APIs del catálogo. Requiere token.",
        ["Uso"] = "Consumo del mes de la API key. Requiere API key.",
    };

    // Error responses get their description with ProducesError; these replace the English defaults ("OK", …).
    private static readonly Dictionary<int, string> _successDescriptions = new()
    {
        [StatusCodes.Status200OK] = "Respuesta correcta.",
        [StatusCodes.Status201Created] = "Recurso creado.",
        [StatusCodes.Status204NoContent] = "Operación realizada; la respuesta no tiene cuerpo.",
    };

    // The endpoints validate these strings by hand (SubscriptionsEndpoints, DashboardEndpoints); keep both in sync.
    private static readonly string[] _plans = ["free", "pro"];
    private static readonly string[] _activityStatuses = ["success", "error", "pending"];

    public static OpenApiOptions AddPortalApiDocumentation(this OpenApiOptions options, string productionUrl)
    {
        options.AddDocumentTransformer((document, _, _) =>
        {
            // ReplaceLineEndings: the raw string has the .cs file's line endings (CRLF on a Windows checkout).
            document.Info = new OpenApiInfo
            {
                Title = "Portal API",
                Version = "1.0.0",
                Description = Description.ReplaceLineEndings("\n"),
            };
            document.Servers =
            [
                new OpenApiServer { Url = productionUrl, Description = "Producción" },
                new OpenApiServer { Url = LocalUrl, Description = "Local" },
            ];
            foreach (var tag in document.Tags ?? new HashSet<OpenApiTag>())
            {
                tag.Description = _tagDescriptions.GetValueOrDefault(tag.Name ?? "");
            }

            return Task.CompletedTask;
        });

        options.AddOperationTransformer((operation, _, _) =>
        {
            foreach (var (status, response) in operation.Responses ?? [])
            {
                var statusCode = int.Parse(status);
                if (response is OpenApiResponse editable
                    && editable.Description == ReasonPhrases.GetReasonPhrase(statusCode)
                    && _successDescriptions.TryGetValue(statusCode, out var description))
                {
                    editable.Description = description;
                }
            }

            return Task.CompletedTask;
        });

        // Schema transformer: runs for every schema, including each property and each parameter.
        options.AddSchemaTransformer((schema, context, _) =>
        {
            if (AllowedValues(context) is { } values)
            {
                schema.Type = JsonSchemaType.String;
                schema.Enum = [.. values.Select(value => JsonValue.Create(value))];
            }
            else if (IsRequestWithOptionalStrings(context.JsonPropertyInfo?.DeclaringType) && schema.Type is { } type)
            {
                // These fields are `string?` only so a missing field reaches the endpoint; clients send strings or omit them.
                schema.Type = type & ~JsonSchemaType.Null;
            }

            return Task.CompletedTask;
        });

        return options;
    }

    // Pattern matching over the context: `plan` only exists in the subscription records.
    private static string[]? AllowedValues(OpenApiSchemaTransformerContext context) => context switch
    {
        { JsonPropertyInfo.Name: "plan" } => _plans,
        { JsonPropertyInfo: { Name: "status", DeclaringType: var type } } when type == typeof(ActivityDto) => _activityStatuses,
        { JsonPropertyInfo: null, ParameterDescription.Name: "status" } => _activityStatuses,
        _ => null,
    };

    private static bool IsRequestWithOptionalStrings(Type? type) =>
        type == typeof(CreateSubscriptionRequest) || type == typeof(UpdateSubscriptionRequest) || type == typeof(OAuthTokenRequest);
}
