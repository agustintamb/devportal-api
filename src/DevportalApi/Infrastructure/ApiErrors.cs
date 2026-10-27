namespace DevportalApi.Infrastructure;

public sealed record ErrorDetail(string Code, string Message);

public sealed record ErrorResponse(ErrorDetail Error);

public static class ApiErrors
{
    public static IResult Problem(int status, string code, string message) =>
        TypedResults.Json(new ErrorResponse(new ErrorDetail(code, message)), statusCode: status);

    // Extension method: lets Program.cs call app.UseApiErrors() as if it were a WebApplication method.
    public static WebApplication UseApiErrors(this WebApplication app)
    {
        app.UseExceptionHandler(errorApp => errorApp.Run(context =>
            Problem(StatusCodes.Status500InternalServerError, "internal_error", "Ocurrió un error inesperado.")
                .ExecuteAsync(context)));

        // Runs only for 4xx/5xx responses that have no body yet (e.g. no route matched).
        app.UseStatusCodePages(statusContext =>
        {
            var status = statusContext.HttpContext.Response.StatusCode;
            return FromStatusCode(status).ExecuteAsync(statusContext.HttpContext);
        });

        return app;
    }

    private static IResult FromStatusCode(int status) => status switch
    {
        StatusCodes.Status404NotFound => Problem(status, "not_found", "El recurso solicitado no existe."),
        StatusCodes.Status405MethodNotAllowed => Problem(status, "method_not_allowed", "El método HTTP no está permitido para este recurso."),
        _ => Problem(status, "http_error", "La solicitud no pudo completarse."),
    };
}
