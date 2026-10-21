using Kernel.Errors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace ApiService.Infrastructure.ExceptionHandlers;

/// <summary>
/// Turns PostgreSQL's <c>22021 character_not_in_repertoire</c> into a <c>400</c>: the request carried a character
/// the database cannot store, most often a NUL byte in a text field.
/// </summary>
/// <remarks>
/// <para>
/// A NUL is legal in JSON — it arrives escaped and only becomes a byte after parsing — and legal in a .NET
/// string, so nothing upstream rejects it. PostgreSQL's <c>text</c> cannot hold <c>0x00</c>, so the insert fails
/// at the very end of the request and would otherwise escape as a <c>500</c>. Schemathesis sends exactly such
/// values (ADR-009).
/// </para>
/// <para>
/// Handled here rather than at deserialization on purpose. A converter over <c>string</c> would reject it earlier
/// and could name the offending field, but a custom converter is opaque to the OpenAPI schema generator, so every
/// string in the document would lose its type. This covers every path to the database instead of only JSON
/// bodies, at the cost of a rolled-back transaction and a message that cannot say which field it was.
/// </para>
/// <para>
/// A stable warning is logged without the original exception text. <c>22021</c> is usually the caller's data,
/// but the same code is raised when the connection's client encoding disagrees with the database's; the warning
/// therefore still tells the operator to check the encoding if the rate is sustained.
/// </para>
/// </remarks>
internal sealed partial class UnstorableTextExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<UnstorableTextExceptionHandler> logger) : IExceptionHandler
{
    /// <summary><c>character_not_in_repertoire</c> — a byte sequence the target encoding cannot represent.</summary>
    private const string CharacterNotInRepertoire = "22021";

    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (!IsUnstorableText(exception))
            return false;

        LogUnstorableText(
            logger,
            context.Request.Method,
            ProblemDetailsTelemetry.RouteTemplate(context));

        context.Response.StatusCode = StatusCodes.Status400BadRequest;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                // Deliberately does not echo the value back: it is caller-supplied and, by definition here,
                // contains bytes that do not belong in a log or a response.
                Detail = ErrorMessages.UnstorableText,
            },
        });
    }

    /// <summary>Whether this failure, at any depth, is PostgreSQL refusing a byte sequence.</summary>
    /// <remarks>
    /// Walked rather than matched on the top frame: EF Core surfaces it wrapped in a <c>DbUpdateException</c>, and
    /// a retrying execution strategy can wrap it again.
    /// </remarks>
    private static bool IsUnstorableText(Exception? exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: CharacterNotInRepertoire })
                return true;
        }

        return false;
    }

    [LoggerMessage(EventId = 1001, EventName = "host.request.unstorable_text", Level = LogLevel.Warning,
        Message = "Rejected {Method} {Path}: the database refused a byte sequence in a text value. "
                  + "Answered 400. If this is not caller data, check the client encoding.")]
    private static partial void LogUnstorableText(ILogger logger, string method, string path);
}
