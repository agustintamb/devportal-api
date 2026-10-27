using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace DevportalApi.Infrastructure;

// Adds the JWT security scheme to the generated OpenAPI document.
public static class OpenApiSecurity
{
    public const string SchemeName = "bearerAuth";

    public static OpenApiOptions AddBearerSecurity(this OpenApiOptions options)
    {
        // Document transformer: runs once and edits the whole document.
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Token obtenido con POST /auth/token.",
            };
            return Task.CompletedTask;
        });

        // Operation transformer: runs per endpoint; RequireAuthorization() leaves IAuthorizeData in its metadata.
        options.AddOperationTransformer(async (operation, context, cancellationToken) =>
        {
            if (!context.Description.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>().Any())
            {
                return;
            }

            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(SchemeName, context.Document)] = [],
            });

            // Registers ErrorResponse in components (a no-op when another endpoint already did) and points to it with $ref.
            var errorSchema = await context.GetOrCreateSchemaAsync(typeof(ErrorResponse), cancellationToken: cancellationToken);
            context.Document?.AddComponent(nameof(ErrorResponse), errorSchema);
            operation.Responses ??= [];
            operation.Responses["401"] = new OpenApiResponse
            {
                Description = "Falta el token de acceso o no es válido (`unauthorized`).",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new() { Schema = new OpenApiSchemaReference(nameof(ErrorResponse), context.Document) },
                },
            };
        });

        return options;
    }
}
