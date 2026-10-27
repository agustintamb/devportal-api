using System.ComponentModel;
using Dapper;
using DevportalApi.Data;
using DevportalApi.Infrastructure;

namespace DevportalApi.Endpoints;

public static class DashboardEndpoints
{
    // Copied from STATS in the portal's src/data/dashboard.ts; fixed demo values, so no table.
    private static readonly StatDto[] _stats =
    [
        new("Solicitudes totales", "1.284", "+12 % esta semana"),
        new("Tasa de éxito", "97,3 %", "+0,5 % frente a la semana pasada"),
        new("APIs integradas", "3", "Portal · Petstore · PokeAPI"),
        new("Latencia promedio", "84 ms", "p95: 210 ms"),
    ];

    private static readonly string[] _activityStatuses = ["success", "error", "pending"];

    private const string StatusDescription = "Filtra por estado; sin el parámetro devuelve toda la actividad.";

    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app, PortalApiVersion version)
    {
        // RequireAuthorization on the group: every route below needs a valid user JWT (the default policy).
        var group = app.MapGroup("/dashboard").WithTags("Dashboard").RequireAuthorization();

        group.MapGet("/stats", GetStats)
            .WithName(version.EndpointName("getDashboardStats"))
            .WithSummary("Obtener las métricas del dashboard")
            .Produces<IReadOnlyList<StatDto>>();

        var list = version == PortalApiVersion.V1
            ? group.MapGet("/activity", ListActivityAsync)
                .Produces<IReadOnlyList<ActivityDto>>()
                .ProducesError(StatusCodes.Status400BadRequest, "El parámetro `status` no es success, error ni pending (`validation_error`).")
            : group.MapGet("/activity", ListActivityPageAsync)
                .Produces<PagedResponse<ActivityDto>>()
                .ProducesError(
                    StatusCodes.Status400BadRequest,
                    "El parámetro `status` no es success, error ni pending, o `page` o `pageSize` están fuera de rango " +
                    "o no son números (`validation_error`).");
        list.WithName(version.EndpointName("listDashboardActivity"))
            .WithSummary("Listar la actividad reciente");

        return app;
    }

    private static IResult GetStats() => TypedResults.Ok(_stats);

    private static async Task<IResult> ListActivityAsync(
        Database database,
        [Description(StatusDescription)] string? status) =>
        IsValidStatus(status)
            ? TypedResults.Ok(await QueryActivityAsync(database, status))
            : InvalidStatus();

    private static async Task<IResult> ListActivityPageAsync(
        Database database,
        [Description(StatusDescription)] string? status,
        [Description(Paging.PageDescription)] int page = Paging.DefaultPage,
        [Description(Paging.PageSizeDescription)] int pageSize = Paging.DefaultPageSize)
    {
        if (!IsValidStatus(status))
        {
            return InvalidStatus();
        }

        return Paging.Validate(page, pageSize)
            ?? TypedResults.Ok(Paging.Paginate(await QueryActivityAsync(database, status), page, pageSize));
    }

    private static bool IsValidStatus(string? status) =>
        string.IsNullOrWhiteSpace(status) || _activityStatuses.Contains(status.Trim());

    private static IResult InvalidStatus() =>
        ApiErrors.Problem(
            StatusCodes.Status400BadRequest,
            "validation_error",
            "El parámetro 'status' debe ser success, error o pending.");

    private static async Task<List<ActivityDto>> QueryActivityAsync(Database database, string? status)
    {
        await using var connection = await database.OpenConnectionAsync();
        var activity = await connection.QueryAsync<ActivityDto>(
            """
            SELECT id AS Id, action AS Action, api AS Api, time AS Time, status AS Status
            FROM activity
            WHERE @status IS NULL OR status = @status
            ORDER BY id
            """,
            new { status = string.IsNullOrWhiteSpace(status) ? null : status.Trim() });

        return activity.AsList();
    }
}
