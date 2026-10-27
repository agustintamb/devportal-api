using System.Globalization;
using System.Security.Claims;
using Dapper;
using DevportalApi.Data;
using DevportalApi.Infrastructure;

namespace DevportalApi.Endpoints;

public static class SubscriptionsEndpoints
{
    private const string SelectSubscriptions = """
        SELECT id AS Id, api_id AS ApiId, plan AS Plan, created_at AS CreatedAt
        FROM subscriptions
        """;

    public static IEndpointRouteBuilder MapSubscriptionsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/subscriptions").WithTags("Suscripciones").RequireAuthorization();

        group.MapGet("", ListSubscriptionsAsync)
            .WithName("listSubscriptions")
            .WithSummary("Listar las suscripciones del usuario")
            .Produces<IReadOnlyList<SubscriptionDto>>();

        group.MapPost("", CreateSubscriptionAsync)
            .WithName("createSubscription")
            .WithSummary("Suscribirse a una API del catálogo")
            .Produces<SubscriptionDto>(StatusCodes.Status201Created)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status422UnprocessableEntity);

        // `:guid` is a route constraint: a non-GUID id matches no route, so the global 404 answers it.
        group.MapGet("/{id:guid}", GetSubscriptionAsync)
            .WithName("getSubscription")
            .WithSummary("Obtener una suscripción del usuario")
            .Produces<SubscriptionDto>()
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        group.MapPatch("/{id:guid}", UpdateSubscriptionAsync)
            .WithName("updateSubscription")
            .WithSummary("Cambiar el plan de una suscripción")
            .Produces<SubscriptionDto>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", DeleteSubscriptionAsync)
            .WithName("deleteSubscription")
            .WithSummary("Cancelar una suscripción")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        return app;
    }

    // ASP.NET passes the authenticated user (HttpContext.User) to any ClaimsPrincipal parameter.
    private static async Task<IResult> ListSubscriptionsAsync(Database database, ClaimsPrincipal user)
    {
        await using var connection = await database.OpenConnectionAsync();
        // ORDER BY rowid = insertion order = creation order.
        var rows = await connection.QueryAsync<SubscriptionRow>(
            $"{SelectSubscriptions} WHERE user_name = @userName ORDER BY rowid",
            new { userName = UserName(user) });

        return TypedResults.Ok(rows.Select(ToDto).ToList());
    }

    private static async Task<IResult> CreateSubscriptionAsync(
        CreateSubscriptionRequest request,
        Database database,
        ClaimsPrincipal user)
    {
        if (string.IsNullOrWhiteSpace(request.ApiId))
        {
            return ApiErrors.Problem(StatusCodes.Status400BadRequest, "validation_error", "El campo 'apiId' es obligatorio.");
        }

        if (request.Plan is not ("free" or "pro"))
        {
            return InvalidPlan();
        }

        await using var connection = await database.OpenConnectionAsync();
        var apiExists = await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM apis WHERE id = @apiId)",
            new { apiId = request.ApiId });
        if (!apiExists)
        {
            return ApiErrors.Problem(
                StatusCodes.Status422UnprocessableEntity,
                "unknown_api",
                $"No existe una API con el id '{request.ApiId}'.");
        }

        var subscription = new SubscriptionDto(Guid.NewGuid(), request.ApiId, request.Plan, DateTimeOffset.UtcNow);
        // ON CONFLICT ... DO NOTHING: if the user already has this API, the UNIQUE index skips the insert (0 rows).
        var inserted = await connection.ExecuteAsync(
            """
            INSERT INTO subscriptions (id, user_name, api_id, plan, created_at)
            VALUES (@id, @userName, @apiId, @plan, @createdAt)
            ON CONFLICT (user_name, api_id) DO NOTHING
            """,
            new
            {
                id = subscription.Id.ToString(),
                userName = UserName(user),
                apiId = subscription.ApiId,
                plan = subscription.Plan,
                // "O" = round-trip ISO 8601, e.g. 2026-10-27T07:29:26.7521234+00:00.
                createdAt = subscription.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
            });
        if (inserted == 0)
        {
            return ApiErrors.Problem(
                StatusCodes.Status409Conflict,
                "already_subscribed",
                $"Ya existe una suscripción a la API '{request.ApiId}'.");
        }

        return TypedResults.Created($"/subscriptions/{subscription.Id}", subscription);
    }

    private static async Task<IResult> GetSubscriptionAsync(Guid id, Database database, ClaimsPrincipal user)
    {
        await using var connection = await database.OpenConnectionAsync();
        var row = await connection.QuerySingleOrDefaultAsync<SubscriptionRow>(
            $"{SelectSubscriptions} WHERE id = @id AND user_name = @userName",
            new { id = id.ToString(), userName = UserName(user) });

        return row is null ? SubscriptionNotFound(id) : TypedResults.Ok(ToDto(row));
    }

    private static async Task<IResult> UpdateSubscriptionAsync(
        Guid id,
        UpdateSubscriptionRequest request,
        Database database,
        ClaimsPrincipal user)
    {
        if (request.Plan is not ("free" or "pro"))
        {
            return InvalidPlan();
        }

        await using var connection = await database.OpenConnectionAsync();
        // RETURNING gives back the updated row in the same statement; no row means not found (or not owned).
        var row = await connection.QuerySingleOrDefaultAsync<SubscriptionRow>(
            """
            UPDATE subscriptions SET plan = @plan
            WHERE id = @id AND user_name = @userName
            RETURNING id AS Id, api_id AS ApiId, plan AS Plan, created_at AS CreatedAt
            """,
            new { plan = request.Plan, id = id.ToString(), userName = UserName(user) });

        return row is null ? SubscriptionNotFound(id) : TypedResults.Ok(ToDto(row));
    }

    private static async Task<IResult> DeleteSubscriptionAsync(Guid id, Database database, ClaimsPrincipal user)
    {
        await using var connection = await database.OpenConnectionAsync();
        var deleted = await connection.ExecuteAsync(
            "DELETE FROM subscriptions WHERE id = @id AND user_name = @userName",
            new { id = id.ToString(), userName = UserName(user) });

        return deleted == 0 ? SubscriptionNotFound(id) : TypedResults.NoContent();
    }

    // Every query filters by this value, so a user never sees or changes another user's subscriptions.
    private static string UserName(ClaimsPrincipal user) =>
        user.FindFirst("sub")?.Value
        ?? throw new InvalidOperationException("The authenticated user has no 'sub' claim.");

    private static IResult InvalidPlan() =>
        ApiErrors.Problem(StatusCodes.Status400BadRequest, "validation_error", "El campo 'plan' debe ser free o pro.");

    private static IResult SubscriptionNotFound(Guid id) =>
        ApiErrors.Problem(StatusCodes.Status404NotFound, "not_found", $"No existe una suscripción con el id '{id}'.");

    private static SubscriptionDto ToDto(SubscriptionRow row) => new(
        Guid.Parse(row.Id),
        row.ApiId,
        row.Plan,
        DateTimeOffset.Parse(row.CreatedAt, CultureInfo.InvariantCulture));

    private sealed record SubscriptionRow(string Id, string ApiId, string Plan, string CreatedAt);
}
