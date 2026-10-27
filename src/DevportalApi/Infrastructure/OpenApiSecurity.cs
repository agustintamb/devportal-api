using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace DevportalApi.Infrastructure;

// Adds the three security schemes (JWT, API key, OAuth2) to the generated OpenAPI document.
public static class OpenApiSecurity
{
    public const string BearerSchemeName = "bearerAuth";
    public const string ApiKeySchemeName = "apiKeyAuth";
    public const string OAuth2SchemeName = "oauth2";

    public static OpenApiOptions AddPortalApiSecurity(this OpenApiOptions options)
    {
        // Document transformer: runs once and edits the whole document.
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[BearerSchemeName] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Token obtenido con POST /auth/token.",
            };
            document.Components.SecuritySchemes[ApiKeySchemeName] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = ApiKeyAuthenticationHandler.HeaderName,
                Description = "API key demo, pública a propósito: `demo-api-key`. Solo sirve para GET /usage.",
            };
            document.Components.SecuritySchemes[OAuth2SchemeName] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                Description =
                    "Client credentials con el cliente demo, público a propósito: `portal-demo` / `portal-demo-secret`. " +
                    "El token actúa en nombre del usuario demo y solo sirve para GET /subscriptions.",
                Flows = new OpenApiOAuthFlows
                {
                    ClientCredentials = new OpenApiOAuthFlow
                    {
                        // Relative: resolved against the selected server, so it works in production and locally.
                        TokenUrl = new Uri("/oauth/token", UriKind.Relative),
                        Scopes = new Dictionary<string, string>
                        {
                            [AuthPolicies.SubscriptionsReadScope] = "Leer las suscripciones del usuario demo.",
                        },
                    },
                },
            };
            return Task.CompletedTask;
        });

        // Operation transformer: runs per endpoint; RequireAuthorization() leaves IAuthorizeData (with its policy) in the metadata.
        options.AddOperationTransformer(async (operation, context, cancellationToken) =>
        {
            var policies = context.Description.ActionDescriptor.EndpointMetadata
                .OfType<IAuthorizeData>()
                .Select(data => data.Policy)
                .ToList();
            if (policies.Count == 0)
            {
                return;
            }

            operation.Security ??= [];
            if (policies.Contains(AuthPolicies.ApiKey))
            {
                operation.Security.Add(Requirement(ApiKeySchemeName, context.Document));
                await AddErrorResponseAsync(
                    operation,
                    context,
                    StatusCodes.Status401Unauthorized,
                    "Falta la API key (header `X-API-Key`) o no es válida (`unauthorized`).",
                    cancellationToken);
                return;
            }

            // Each item of `security` is an alternative: a user token or, where its scope allows it, an OAuth client token.
            operation.Security.Add(Requirement(BearerSchemeName, context.Document));
            if (policies.Contains(AuthPolicies.SubscriptionsRead))
            {
                operation.Security.Add(Requirement(OAuth2SchemeName, context.Document, AuthPolicies.SubscriptionsReadScope));
            }

            await AddErrorResponseAsync(
                operation,
                context,
                StatusCodes.Status401Unauthorized,
                "Falta el token de acceso o no es válido (`unauthorized`).",
                cancellationToken);
            await AddErrorResponseAsync(
                operation,
                context,
                StatusCodes.Status403Forbidden,
                "El token es de un cliente OAuth y sus scopes no cubren este endpoint (`forbidden`).",
                cancellationToken);
        });

        return options;
    }

    private static OpenApiSecurityRequirement Requirement(string schemeName, OpenApiDocument? document, params string[] scopes) =>
        new() { [new OpenApiSecuritySchemeReference(schemeName, document)] = [.. scopes] };

    private static async Task AddErrorResponseAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        int status,
        string description,
        CancellationToken cancellationToken)
    {
        // Registers ErrorResponse in components (a no-op when another endpoint already did) and points to it with $ref.
        var errorSchema = await context.GetOrCreateSchemaAsync(typeof(ErrorResponse), cancellationToken: cancellationToken);
        context.Document?.AddComponent(nameof(ErrorResponse), errorSchema);
        operation.Responses ??= [];
        operation.Responses[status.ToString()] = new OpenApiResponse
        {
            Description = description,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new() { Schema = new OpenApiSchemaReference(nameof(ErrorResponse), context.Document) },
            },
        };
    }
}
