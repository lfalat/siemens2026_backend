using Kernel.Errors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ApiService.Infrastructure.ExceptionHandlers;

/// <summary>
/// Catch-all exception handler: logs the failure and returns a generic HTTP 500 <c>ProblemDetails</c> response
/// with <see cref="ErrorMessages.ServerError"/>, never anything from the exception. Registered last so the typed
/// handlers get first chance.
/// </summary>
internal sealed partial class UnhandledExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<UnhandledExceptionHandler> logger) : IExceptionHandler
{
    [LoggerMessage(
        EventId = 1000,
        EventName = "host.request.unhandled_exception",
        Level = LogLevel.Error,
        Message = "Unhandled exception on {Method} {Path}")]
    private static partial void LogUnhandledException(
        ILogger logger,
        string method,
        string path,
        Exception exception);

    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken ct)
    {
        // The route template, not the path: a path carries ids and applicant-supplied values.
        LogUnhandledException(
            logger,
            context.Request.Method,
            ProblemDetailsTelemetry.RouteTemplate(context),
            exception);

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Detail = ErrorMessages.ServerError,
            }
        });
    }
}
