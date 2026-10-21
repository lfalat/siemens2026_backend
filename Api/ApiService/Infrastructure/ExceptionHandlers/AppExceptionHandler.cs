using System.Globalization;
using Kernel.Errors;
using Kernel.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ApiService.Infrastructure.ExceptionHandlers;

/// <summary>
/// Translates client-error exceptions into a <c>ProblemDetails</c> response whose <c>detail</c> is the Slovak message
/// the frontend shows: any <see cref="AppException"/> (its <see cref="AppException.StatusCode"/> and message, with
/// <see cref="ValidationException"/> also exposing its per-member messages under <c>errors</c>), EF Core's
/// <see cref="DbUpdateConcurrencyException"/> (another request changed the row first — a <c>409</c>), and the
/// framework's <see cref="BadHttpRequestException"/> (a malformed request body / route value — its own status, usually
/// 400, with that status's default message, since the framework's own is English and technical). All are answerable by
/// the caller, so they are turned into a tidy response and <b>not</b> re-thrown to the catch-all handler that logs
/// unhandled faults. Returns <c>false</c> for other exceptions.
/// </summary>
internal sealed class AppExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        int statusCode;
        string detail;
        string? problemType = null;
        IReadOnlyDictionary<string, string[]>? errors = null;
        DateTimeOffset? retryAtExtension = null;

        switch (exception)
        {
            case AppException app:
                statusCode = app.StatusCode;
                detail = app.Message;
                problemType = app.ProblemType;
                if (app is ValidationException validation)
                    errors = validation.Errors;
                // Tells the client how long to wait rather than leaving it to guess or hammer. Both cases
                // mean "come back later": nobody was home, or a limit has to run out first.
                int? retryAfter = app switch
                {
                    ServiceUnavailableException unavailable => unavailable.RetryAfterSeconds,
                    TooManyRequestsException tooMany =>
                        Math.Max(1, (int)Math.Ceiling(tooMany.RetryAfter.TotalSeconds)),
                    _ => null,
                };
                if (retryAfter is { } seconds)
                    context.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
                if (app is TooManyRequestsException { RetryAt: { } retryAt })
                    retryAtExtension = retryAt;
                break;

            // The row's optimistic-concurrency version changed between this request's read and its save — e.g. a job
            // offer edited by two recruiters at once. Nothing was saved; the client reloads and retries. Not a fault.
            case DbUpdateConcurrencyException:
                statusCode = StatusCodes.Status409Conflict;
                detail = ErrorMessages.ConcurrencyConflict;
                break;

            // Unparseable body, bad route/query value, body too large, etc. — a client error, not a fault.
            case BadHttpRequestException bad:
                statusCode = bad.StatusCode;
                detail = ErrorMessages.ForStatus(bad.StatusCode);
                break;

            default:
                return false; // genuine fault — let the catch-all handler log and 500 it
        }

        context.Response.StatusCode = statusCode;
        ProblemDetails problem = new() { Status = statusCode, Detail = detail };
        // Title is filled from the status by the ProblemDetails writer, and so is Type unless the
        // exception names one — which it does only where a client has to tell two problems apart that
        // share a status code (see AppException.ProblemType).
        if (problemType is not null)
            problem.Type = problemType;

        if (errors is not null)
            problem.Extensions["errors"] = errors;

        // When a 429's wait ends, as a timestamp: the header carries seconds, which a client would have to add up.
        if (retryAtExtension is { } at)
            problem.Extensions["retryAt"] = at;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = problem
        });
    }
}
