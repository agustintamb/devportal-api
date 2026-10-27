using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DevportalApi.Infrastructure;

public sealed record ErrorDetail(string Code, string Message);

public sealed record ErrorResponse(ErrorDetail Error);

// In Development, ASP.NET throws BadHttpRequestException for an unreadable body or parameter. It is the client's
// mistake, so it is answered here (no Error-level log) instead of falling into the generic 500 handler.
public sealed class BadRequestExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException badRequest)
        {
            return false;
        }

        await ApiErrors.FromStatusCode(badRequest.StatusCode).ExecuteAsync(httpContext);
        return true;
    }
}

public static class ApiErrors
{
    public static IResult Problem(int status, string code, string message) =>
        TypedResults.Json(new ErrorResponse(new ErrorDetail(code, message)), statusCode: status);

    // Documents an error response in the OpenAPI spec: status, ErrorResponse body and a description in Spanish.
    public static RouteHandlerBuilder ProducesError(this RouteHandlerBuilder builder, int status, string description) =>
        builder.WithMetadata(new ProducesResponseTypeAttribute<ErrorResponse>(status, "application/json")
        {
            Description = description,
        });

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

    internal static IResult FromStatusCode(int status) => status switch
    {
        StatusCodes.Status400BadRequest => Problem(status, "validation_error", "La solicitud tiene un cuerpo o parámetros con formato inválido."),
        StatusCodes.Status403Forbidden => Problem(status, "forbidden", "El token no tiene permiso para este recurso."),
        StatusCodes.Status404NotFound => Problem(status, "not_found", "El recurso solicitado no existe."),
        StatusCodes.Status405MethodNotAllowed => Problem(status, "method_not_allowed", "El método HTTP no está permitido para este recurso."),
        StatusCodes.Status415UnsupportedMediaType => Problem(status, "unsupported_media_type", "El Content-Type del cuerpo no es el que acepta este endpoint."),
        _ => Problem(status, "http_error", "La solicitud no pudo completarse."),
    };
}
