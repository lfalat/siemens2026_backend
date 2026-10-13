using System.Reflection;
using System.Text.Json.Nodes;
using Kernel.Errors;
using Kernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace ApiService.OpenAPI;

/// <summary>
/// Document transformer that publishes every error code as two component schemas — <c>ErrorCode</c> for a problem's
/// <c>code</c> and <c>FieldErrorCode</c> for the values under <c>errorCodes</c> — each a string enum with an
/// <c>x-enum-descriptions</c> map, and points the problem schemas' <c>code</c> and <c>errorCodes</c> at them. A
/// client can generate its list of translation keys from these two schemas (ADR-A017).
/// </summary>
/// <remarks>
/// The set is everything an operation declares, plus every code in <see cref="CommonErrors"/> and
/// <see cref="ValidationRuleErrors"/> — the host answers some of those (a <c>405</c>, a <c>413</c>) on any route.
/// </remarks>
/// <param name="endpoints">The host's endpoints.</param>
/// <param name="idempotency">Host-wide idempotency settings.</param>
internal sealed class ErrorCodeCatalogTransformer(
    EndpointDataSource endpoints,
    IOptions<IdempotencyOptions> idempotency) : IOpenApiDocumentTransformer
{
    /// <summary>The component schema of a problem's <c>code</c>.</summary>
    public const string ProblemSchema = "ErrorCode";

    /// <summary>The component schema of a value under <c>errorCodes</c>.</summary>
    public const string FieldSchema = "FieldErrorCode";

    /// <inheritdoc />
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        List<IErrorCode> codes = [.. Catalog(typeof(CommonErrors)), .. Catalog(typeof(ValidationRuleErrors))];

        foreach (RouteEndpoint endpoint in endpoints.Endpoints.OfType<RouteEndpoint>())
            codes.AddRange(OperationErrorCodes.For(
                [.. endpoint.Metadata],
                endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods is [string method, ..] ? method : null,
                idempotency.Value));

        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
        document.Components.Schemas[ProblemSchema] = EnumSchema(
            codes.OfType<ErrorCode>(),
            "A problem's `code`: the stable key a client translates on. `detail` is English and is not a key.");
        document.Components.Schemas[FieldSchema] = EnumSchema(
            codes.OfType<FieldErrorCode>(),
            "A value under a validation problem's `errorCodes[field]`, index-aligned with `errors[field]`.");

        foreach ((string name, IOpenApiSchema schema) in document.Components.Schemas)
        {
            if (schema is not OpenApiSchema { Properties: not null } problem
                || !name.Contains("ProblemDetails", StringComparison.Ordinal))
                continue;

            problem.Properties["code"] = new OpenApiSchemaReference(ProblemSchema, document);
            if (name.Contains("Validation", StringComparison.Ordinal))
                problem.Properties["errorCodes"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Object,
                    AdditionalProperties = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Array,
                        Items = new OpenApiSchemaReference(FieldSchema, document),
                    },
                };
        }

        return Task.CompletedTask;
    }

    /// <summary>The public static codes a catalog class declares.</summary>
    private static IEnumerable<IErrorCode> Catalog(Type catalog) => catalog
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(static f => typeof(IErrorCode).IsAssignableFrom(f.FieldType))
        .Select(static f => (IErrorCode)f.GetValue(null)!);

    /// <summary>A string enum of <paramref name="codes"/>' values, with their descriptions keyed by value.</summary>
    private static OpenApiSchema EnumSchema(IEnumerable<IErrorCode> codes, string description)
    {
        List<IErrorCode> distinct =
            [.. codes.DistinctBy(static c => c.Value).OrderBy(static c => c.Value, StringComparer.Ordinal)];
        JsonObject descriptions = [];
        foreach (IErrorCode code in distinct)
            descriptions[code.Value] = code.Description;

        return new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Description = description,
            Enum = [.. distinct.Select(static c => (JsonNode)JsonValue.Create(c.Value))],
            Extensions = new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal)
            {
                ["x-enum-descriptions"] = new JsonNodeExtension(descriptions),
            },
        };
    }
}
