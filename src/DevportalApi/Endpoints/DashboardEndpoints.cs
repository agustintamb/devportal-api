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
        new("APIs integradas", "3", "Petstore · Blog · Auth"),
        new("Latencia promedio", "84 ms", "p95: 210 ms"),
    ];

    private static readonly string[] _activityStatuses = ["success", "error", "pending"];

    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        // RequireAuthorization on the group: every route below needs a valid user JWT (the default policy).
        var group = app.MapGroup("/dashboard").WithTags("Dashboard").RequireAuthorization();

        group.MapGet("/stats", GetStats)
            .WithName("getDashboardStats")
            .WithSummary("Obtener las métricas del dashboard")
            .Produces<IReadOnlyList<StatDto>>();

        group.MapGet("/activity", ListActivityAsync)
            .WithName("listDashboardActivity")
            .WithSummary("Listar la actividad reciente")
            .Produces<IReadOnlyList<ActivityDto>>()
            .ProducesError(StatusCodes.Status400BadRequest, "El parámetro `status` no es success, error ni pending (`validation_error`).");

        return app;
    }

    private static IResult GetStats() => TypedResults.Ok(_stats);

    private static async Task<IResult> ListActivityAsync(
        Database database,
        [Description("Filtra por estado; sin el parámetro devuelve toda la actividad.")] string? status)
    {
        var filter = string.IsNullOrWhiteSpace(status) ? null : status.Trim();
        if (filter is not null && !_activityStatuses.Contains(filter))
        {
            return ApiErrors.Problem(
                StatusCodes.Status400BadRequest,
                "validation_error",
                "El parámetro 'status' debe ser success, error o pending.");
        }

        await using var connection = await database.OpenConnectionAsync();
        var activity = await connection.QueryAsync<ActivityDto>(
            """
            SELECT id AS Id, action AS Action, api AS Api, time AS Time, status AS Status
            FROM activity
            WHERE @status IS NULL OR status = @status
            ORDER BY id
            """,
            new { status = filter });

        return TypedResults.Ok(activity);
    }
}
