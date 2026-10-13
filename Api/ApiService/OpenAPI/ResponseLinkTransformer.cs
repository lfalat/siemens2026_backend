using System.Globalization;
using Kernel.Endpoints;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ApiService.OpenAPI;

/// <summary>
/// Operation transformer that writes the <see cref="ResponseLink"/> declarations an endpoint carries into
/// its responses' <c>links</c>.
/// </summary>
/// <remarks>
/// <para>
/// Links are the only part of an OpenAPI document that describes a sequence: without them every operation
/// stands alone, a caller has no stated way to learn a child id, and the stateful phase of a contract run
/// has nothing to chain (ADR-009, <c>docs/architecture/contract-testing.md</c>). An id is a server-generated
/// UUID, so a response that hands one back is the only place a caller can learn it.
/// </para>
/// <para>
/// A declaration whose status code the endpoint never declares is skipped rather than thrown on: a
/// transformer that throws takes the whole document down at runtime, and the mistake is a static one.
/// <c>ResponseLinkDeclarationTests</c> (TODO(first-module)) fails the build on it instead.
/// </para>
/// </remarks>
internal sealed class ResponseLinkTransformer : IOpenApiOperationTransformer
{
    /// <inheritdoc />
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        foreach (ResponseLink link in context.Description.ActionDescriptor.EndpointMetadata.OfType<ResponseLink>())
        {
            string status = link.StatusCode.ToString(CultureInfo.InvariantCulture);

            if (operation.Responses?.TryGetValue(status, out IOpenApiResponse? response) is not true
                || response is not OpenApiResponse target)
                continue;

            target.Links ??= new Dictionary<string, IOpenApiLink>(StringComparer.Ordinal);
            target.Links[link.TargetOperationId] = new OpenApiLink
            {
                OperationId = link.TargetOperationId,
                Parameters = link.Parameters.ToDictionary(
                    static p => p.Key,
                    static p => new RuntimeExpressionAnyWrapper { Expression = RuntimeExpression.Build(p.Value) },
                    StringComparer.Ordinal),
            };
        }

        return Task.CompletedTask;
    }
}
