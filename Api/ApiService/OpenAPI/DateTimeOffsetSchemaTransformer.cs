using Kernel.Serialization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ApiService.OpenAPI;

/// <summary>
/// Restores <c>type: string</c> on every <see cref="DateTimeOffset"/> in the document.
/// </summary>
/// <remarks>
/// <para>
/// The schema generator derives a type from how System.Text.Json handles it, and a custom converter is opaque to
/// that — registering <see cref="UtcDateTimeOffsetConverter"/> leaves every timestamp described as
/// <c>{"format": "date-time"}</c> with no <c>type</c> at all. A JSON Schema with no type permits every type, so the
/// document would say a number or a boolean is an acceptable timestamp; the API answers 400 and Schemathesis
/// reports it as rejecting valid data (ADR-009), and the generated frontend client types it as <c>unknown</c>
/// (ADR-F005).
/// </para>
/// <para>
/// The nullability already on the schema is preserved: an optional timestamp stays <c>["string","null"]</c> rather
/// than being narrowed to a value that must be present.
/// </para>
/// </remarks>
internal sealed class DateTimeOffsetSchemaTransformer : IOpenApiSchemaTransformer
{
    /// <inheritdoc />
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        Type type = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type;
        if (type != typeof(DateTimeOffset))
            return Task.CompletedTask;

        bool nullable = schema.Type?.HasFlag(JsonSchemaType.Null) is true
            || Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) is not null;

        schema.Type = nullable ? JsonSchemaType.String | JsonSchemaType.Null : JsonSchemaType.String;
        schema.Format = "date-time";

        return Task.CompletedTask;
    }
}
