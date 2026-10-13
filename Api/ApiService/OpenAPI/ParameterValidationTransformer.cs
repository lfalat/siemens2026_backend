using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ApiService.OpenAPI;

/// <summary>
/// Operation transformer that copies the validation attributes on a handler's path and query parameters
/// onto the corresponding OpenAPI parameters.
/// </summary>
/// <remarks>
/// The framework does not do this: an attribute on a minimal-API parameter is enforced at runtime and
/// invisible in the document, so <c>?status=</c> reads as "any string" while the API accepts four values.
/// A generated client then offers a free-text field, and a contract run reports the API rejecting a
/// request its own schema permitted (ADR-009). Body properties are covered separately by
/// <see cref="CommandValidationSchemaTransformer"/>; both go through <see cref="ValidationSchemaRules"/>
/// so the two cannot describe the same attribute differently.
/// </remarks>
internal sealed class ParameterValidationTransformer : IOpenApiOperationTransformer
{
    /// <inheritdoc />
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (operation.Parameters is null || operation.Parameters.Count == 0)
            return Task.CompletedTask;

        foreach (IParameterBindingMetadata binding in
                 context.Description.ActionDescriptor.EndpointMetadata.OfType<IParameterBindingMetadata>())
        {
            if (binding.ParameterInfo is not { } info)
                continue;

            ValidationAttribute[] rules = [.. info.GetCustomAttributes<ValidationAttribute>(inherit: true)];
            if (rules.Length == 0)
                continue;

            IOpenApiParameter? match = operation.Parameters
                .FirstOrDefault(p => string.Equals(p.Name, binding.Name, StringComparison.OrdinalIgnoreCase));

            if (match is not OpenApiParameter { Schema: OpenApiSchema schema })
                continue;

            // The `owner` argument is where a [Required] would record the property name. A parameter's
            // requiredness is already carried by the parameter itself, so it has nowhere to go here and a
            // throwaway schema absorbs it.
            ValidationSchemaRules.Apply(rules, schema, new OpenApiSchema(), binding.Name);
        }

        return Task.CompletedTask;
    }
}
