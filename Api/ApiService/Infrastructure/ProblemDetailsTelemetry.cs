using System.Diagnostics;
using Kernel.Errors;

namespace ApiService.Infrastructure;

/// <summary>Produces safe, stable request identifiers for errors and operational logs, and the default error code.</summary>
internal static class ProblemDetailsTelemetry
{
    /// <summary>
    /// Adds the request path and the current W3C trace id to every problem response, so a user's error report
    /// leads straight to its trace in Azure Monitor — and a <c>code</c> from the status when the writer set none
    /// (the framework's status pages, the rate limiter's 429), so every problem carries one (ADR-A017).
    /// </summary>
    public static void Customize(ProblemDetailsContext context)
    {
        context.ProblemDetails.Instance = context.HttpContext.Request.Path;
        context.ProblemDetails.Extensions["traceId"] =
            Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier;

        // The framework's own validation (ADR-A008) answers with a validation problem and no code of its own.
        int status = context.ProblemDetails.Status ?? context.HttpContext.Response.StatusCode;
        ErrorCode code = context.ProblemDetails is HttpValidationProblemDetails
            ? CommonErrors.ValidationFailed
            : CommonErrors.ForStatus(status);
        context.ProblemDetails.Extensions.TryAdd("code", code.Value);
    }

    /// <summary>Returns the matched route template without request-specific route values.</summary>
    public static string RouteTemplate(HttpContext context) =>
        (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";
}
