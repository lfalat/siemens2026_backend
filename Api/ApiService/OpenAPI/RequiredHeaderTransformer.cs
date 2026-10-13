using Kernel.Endpoints;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ApiService.OpenAPI;

/// <summary>
/// Operation transformer that writes the <see cref="RequiredHeader"/> declarations an endpoint carries into
/// its parameters, along with the <c>400</c> that a missing or malformed value produces.
/// </summary>
/// <remarks>
/// The point is that a request satisfying the whole document should be one the API accepts. A header
/// enforced by a filter and absent from the document breaks that in the least visible way: a generated
/// client cannot know to send it, and a contract run reports the API rejecting its own contract (ADR-009,
/// <c>docs/architecture/contract-testing.md</c>). A header already declared some other way is left alone.
/// </remarks>
internal sealed class RequiredHeaderTransformer : IOpenApiOperationTransformer
{
    /// <inheritdoc />
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        foreach (RequiredHeader header in context.Description.ActionDescriptor.EndpointMetadata.OfType<RequiredHeader>())
        {
            operation.Parameters ??= [];
            if (operation.Parameters.Any(p => string.Equals(p.Name, header.Name, StringComparison.OrdinalIgnoreCase)))
                continue;

            operation.Parameters.Add(new OpenApiParameter
            {
                Name = header.Name,
                In = ParameterLocation.Header,
                Required = true,
                Description = header.Description,
                Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = header.Format },
            });

            operation.Responses ??= [];
            if (!operation.Responses.ContainsKey("400"))
            {
                operation.Responses["400"] = new OpenApiResponse
                {
                    Description = $"The '{header.Name}' header is missing or malformed.",
                };
            }
        }

        return Task.CompletedTask;
    }
}
