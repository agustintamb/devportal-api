namespace DevportalApi.Infrastructure;

// Paging of the v2 lists: `page` and `pageSize` from the query string, sliced in memory (the tables are small).
public static class Paging
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    // Not `const`: they are built from the constants above. OpenApiDocumentation puts them on the page/pageSize parameters.
    public static readonly string PageDescription = $"Número de página, desde {DefaultPage}. Por defecto, {DefaultPage}.";
    public static readonly string PageSizeDescription =
        $"Cantidad de ítems por página, entre 1 y {MaxPageSize}. Por defecto, {DefaultPageSize}.";
    public const string ErrorDescription = "`page` o `pageSize` están fuera de rango o no son números (`validation_error`).";

    // Null when the values are valid; otherwise the 400 to return. A non-numeric value never gets here:
    // ASP.NET fails to bind the int and UseApiErrors answers the same 400 validation_error.
    public static IResult? Validate(int page, int pageSize) =>
        page < 1 || pageSize is < 1 or > MaxPageSize
            ? ApiErrors.Problem(
                StatusCodes.Status400BadRequest,
                "validation_error",
                $"'page' debe ser 1 o más y 'pageSize' debe estar entre 1 y {MaxPageSize}.")
            : null;

    public static PagedResponse<T> Paginate<T>(IReadOnlyList<T> all, int page, int pageSize)
    {
        // long: a huge `page` would overflow int and wrap around to a negative skip.
        var skip = (long)(page - 1) * pageSize;
        T[] items = skip >= all.Count ? [] : [.. all.Skip((int)skip).Take(pageSize)];
        return new PagedResponse<T>(items, page, pageSize, all.Count);
    }
}
