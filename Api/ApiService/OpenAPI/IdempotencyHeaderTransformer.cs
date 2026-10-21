using Kernel.Idempotency;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace ApiService.OpenAPI;

/// <summary>
/// Operation transformer that documents the idempotency contract on the operations
/// <see cref="IdempotencyMiddleware"/> actually acts on: the <c>Idempotency-Key</c> request header and the
/// three rejections the middleware can produce before the endpoint ever runs. Without it the document
/// claims a covered write takes no headers, which is wrong for every client generated from it — and makes
/// contract tests (ADR-009, Schemathesis) fail on a status code the specification never declared.
/// </summary>
/// <param name="options">Host-wide idempotency settings — the header name, whether it is required, and its length limit.</param>
internal sealed class IdempotencyHeaderTransformer(IOptions<IdempotencyOptions> options) : IOpenApiOperationTransformer
{
    private readonly IdempotencyOptions _options = options.Value;

    /// <inheritdoc />
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (!IsCovered(context))
            return Task.CompletedTask;

        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = _options.HeaderName,
            In = ParameterLocation.Header,
            Required = _options.RequireKey,
            Description =
                "Client-generated key making this write at-most-once: resend the same key to retry the same " +
                "logical operation and the original successful response is replayed verbatim, carrying " +
                $"'{_options.ReplayHeaderName}: true'. Use a fresh key for a new operation.",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String, MaxLength = _options.MaxKeyLength },
        });

        if (_options.RequireKey)
            AddResponse(operation, "400", $"The '{_options.HeaderName}' header is missing, empty, or longer than {_options.MaxKeyLength} characters.");

        AddResponse(operation, "409", $"A request with this '{_options.HeaderName}' is still being processed.");
        AddResponse(operation, "422", $"This '{_options.HeaderName}' was already used with a different request payload.");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Whether the middleware would handle this operation — the same method rule it applies, and the same
    /// <see cref="IdempotencyDisabled"/> opt-out (which is what <c>MapQuery</c> carries).
    /// </summary>
    private static bool IsCovered(OpenApiOperationTransformerContext context) =>
        context.Description.HttpMethod is { } method
        && IdempotencyMiddleware.IsCoveredMethod(method)
        && !context.Description.ActionDescriptor.EndpointMetadata.OfType<IdempotencyDisabled>().Any();

    /// <summary>
    /// Declares a status the middleware can return, leaving an existing entry alone: an endpoint that
    /// documents its own <c>409</c> means something more specific by it than "still processing".
    /// </summary>
    private static void AddResponse(OpenApiOperation operation, string statusCode, string description)
    {
        operation.Responses ??= [];
        if (!operation.Responses.ContainsKey(statusCode))
            operation.Responses[statusCode] = new OpenApiResponse { Description = description };
    }
}
