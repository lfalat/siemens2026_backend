using System.Diagnostics;
using Kernel.Errors;

namespace ApiService.Infrastructure;

/// <summary>Produces safe, stable request identifiers for errors and operational logs, and the default error message.</summary>
internal static class ProblemDetailsTelemetry
{
    /// <summary>
    /// Adds the request path and the current W3C trace id to every problem response, so a user's error report
    /// leads straight to its trace in Azure Monitor — and a Slovak <c>detail</c> from the status when the writer set
    /// none (the framework's status pages), so every problem carries a message the frontend can show (ADR-A017).
    /// </summary>
    public static void Customize(ProblemDetailsContext context)
    {
        context.ProblemDetails.Instance = context.HttpContext.Request.Path;
        context.ProblemDetails.Extensions["traceId"] =
            Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier;

        // The framework's own validation (ADR-A008) answers with a validation problem and no detail of its own.
        int status = context.ProblemDetails.Status ?? context.HttpContext.Response.StatusCode;
        context.ProblemDetails.Detail ??= context.ProblemDetails is HttpValidationProblemDetails
            ? ErrorMessages.ValidationFailed
            : ErrorMessages.ForStatus(status);
    }

    /// <summary>Returns the matched route template without request-specific route values.</summary>
    public static string RouteTemplate(HttpContext context) =>
        (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";
}
