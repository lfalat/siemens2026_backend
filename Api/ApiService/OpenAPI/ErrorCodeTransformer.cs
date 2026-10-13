using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Kernel.Errors;
using Kernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace ApiService.OpenAPI;

/// <summary>
/// Operation transformer that lists, under each error response, the codes the operation can answer with there: a
/// table of code and meaning in the response description (what Scalar shows), and the bare values in
/// <c>x-error-codes</c> — plus <c>x-field-error-codes</c> on a <c>400</c> for the per-field codes under
/// <c>errorCodes</c>. A bilingual client translates on these codes, so the document is where a frontend engineer
/// learns which ones an operation needs (ADR-A017).
/// </summary>
/// <remarks>
/// A status a code is answered with but the endpoint never declared — a rule-added <c>409</c> for
/// <c>concurrency.conflict</c>, a policy's <c>403</c> — gets its response here, so the document never lists a code
/// under a status it omits.
/// </remarks>
/// <param name="idempotency">Host-wide idempotency settings — whether a missing key is refused.</param>
internal sealed class ErrorCodeTransformer(IOptions<IdempotencyOptions> idempotency) : IOpenApiOperationTransformer
{
    /// <summary>The extension carrying a response's top-level <c>code</c> values.</summary>
    public const string CodesExtension = "x-error-codes";

    /// <summary>The extension carrying a <c>400</c> response's per-field <c>errorCodes</c> values.</summary>
    public const string FieldCodesExtension = "x-field-error-codes";

    /// <inheritdoc />
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<IErrorCode> codes = OperationErrorCodes.For(
            [.. context.Description.ActionDescriptor.EndpointMetadata],
            context.Description.HttpMethod,
            idempotency.Value);
        if (codes.Count == 0)
            return Task.CompletedTask;

        operation.Responses ??= [];
        foreach (IGrouping<int, IErrorCode> byStatus in codes
                     .GroupBy(static c => c is ErrorCode problem ? problem.Status : StatusCodes.Status400BadRequest)
                     .OrderBy(static g => g.Key))
        {
            string status = byStatus.Key.ToString(CultureInfo.InvariantCulture);
            if (!operation.Responses.TryGetValue(status, out IOpenApiResponse? existing))
                operation.Responses[status] = existing = new OpenApiResponse
                {
                    Description = ReasonPhrases.GetReasonPhrase(byStatus.Key),
                };
            if (existing is not OpenApiResponse response)
                continue;

            List<ErrorCode> problems = [.. Sorted(byStatus.OfType<ErrorCode>())];
            List<FieldErrorCode> fields = [.. Sorted(byStatus.OfType<FieldErrorCode>())];

            response.Description = (response.Description ?? string.Empty) + Describe(problems, fields);
            response.Extensions ??= new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal);
            if (problems.Count > 0)
                response.Extensions[CodesExtension] = Values(problems);
            if (fields.Count > 0)
                response.Extensions[FieldCodesExtension] = Values(fields);
        }

        return Task.CompletedTask;
    }

    private static IEnumerable<T> Sorted<T>(IEnumerable<T> codes) where T : IErrorCode =>
        codes.OrderBy(static c => c.Value, StringComparer.Ordinal);

    /// <summary>The markdown appended to a response description: a table for <c>code</c>, one for <c>errorCodes</c>.</summary>
    private static string Describe(List<ErrorCode> problems, List<FieldErrorCode> fields)
    {
        StringBuilder md = new();
        if (problems.Count > 0)
            Table(md, "`code`", problems);
        if (fields.Count > 0)
            Table(md, "`errorCodes[field]`", fields);
        return md.ToString();
    }

    private static void Table(StringBuilder md, string where, IEnumerable<IErrorCode> codes)
    {
        md.Append("\n\n| ").Append(where).Append(" | Meaning |\n| --- | --- |\n");
        foreach (IErrorCode code in codes)
            md.Append("| `").Append(code.Value).Append("` | ")
              .Append(code.Description.Replace("|", "\\|", StringComparison.Ordinal)).Append(" |\n");
    }

    private static JsonNodeExtension Values(IEnumerable<IErrorCode> codes) =>
        new(new JsonArray([.. codes.Select(static c => (JsonNode)JsonValue.Create(c.Value))]));
}
