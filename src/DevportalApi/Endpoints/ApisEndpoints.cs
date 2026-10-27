using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Dapper;
using DevportalApi.Data;
using DevportalApi.Infrastructure;

namespace DevportalApi.Endpoints;

public static class ApisEndpoints
{
    // Column aliases (AS ...) match the snake_case columns to the ApiRow constructor parameters.
    private const string SelectApis = """
        SELECT id AS Id, name AS Name, description AS Description, status AS Status,
               category AS Category, tags_json AS TagsJson, version AS Version
        FROM apis
        """;

    public static IEndpointRouteBuilder MapApisEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/apis").WithTags("Catálogo");

        group.MapGet("", ListApisAsync)
            .WithName("listApis")
            .WithSummary("Listar el catálogo de APIs")
            .Produces<IReadOnlyList<ApiDto>>();

        group.MapGet("/{id}", GetApiAsync)
            .WithName("getApi")
            .WithSummary("Obtener una API del catálogo")
            .Produces<ApiDto>()
            .ProducesError(StatusCodes.Status404NotFound, "No existe una API con ese id (`not_found`).");

        return app;
    }

    // `q` and `category` are not route parameters, so ASP.NET binds them from the query string.
    // [Description] becomes the parameter description in the OpenAPI spec.
    private static async Task<IResult> ListApisAsync(
        Database database,
        [Description("Texto a buscar en el nombre, la descripción y los tags, sin distinguir mayúsculas ni acentos.")] string? q,
        [Description("Categoría exacta, sin distinguir mayúsculas (por ejemplo, `Seguridad`).")] string? category)
    {
        await using var connection = await database.OpenConnectionAsync();
        // ORDER BY rowid = seed insertion order = portal order.
        var rows = await connection.QueryAsync<ApiRow>(
            $"{SelectApis} WHERE @category IS NULL OR category = @category COLLATE NOCASE ORDER BY rowid",
            new { category = string.IsNullOrWhiteSpace(category) ? null : category.Trim() });

        var search = q?.Trim();
        var apis = rows
            .Select(ToDto)
            .Where(api => string.IsNullOrEmpty(search)
                || ContainsText(api.Name, search)
                || ContainsText(api.Description, search)
                || api.Tags.Any(tag => ContainsText(tag, search)))
            .ToList();

        return TypedResults.Ok(apis);
    }

    private static async Task<IResult> GetApiAsync(
        Database database,
        [Description("Id de la API en el catálogo (por ejemplo, `blog`).")] string id)
    {
        await using var connection = await database.OpenConnectionAsync();
        var row = await connection.QuerySingleOrDefaultAsync<ApiRow>($"{SelectApis} WHERE id = @id", new { id });

        return row is null
            ? ApiErrors.Problem(StatusCodes.Status404NotFound, "not_found", $"No existe una API con el id '{id}'.")
            : TypedResults.Ok(ToDto(row));
    }

    // IgnoreNonSpace skips accents, so "autenticacion" matches "autenticación".
    private static bool ContainsText(string source, string value) =>
        CultureInfo.InvariantCulture.CompareInfo.IndexOf(
            source, value, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

    private static ApiDto ToDto(ApiRow row) => new(
        row.Id,
        row.Name,
        row.Description,
        row.Status,
        row.Category,
        JsonSerializer.Deserialize<string[]>(row.TagsJson) ?? [],
        row.Version);

    private sealed record ApiRow(
        string Id,
        string Name,
        string Description,
        string Status,
        string Category,
        string TagsJson,
        string Version);
}
