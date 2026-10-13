using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ApiService.OpenAPI;

/// <summary>
/// Validation lives on the CQRS command/query (ADR-A003), but OpenAPI documents the
/// <c>*Request</c> DTO the endpoint binds. This transformer copies the DataAnnotation-derived
/// constraints (required, length, pattern, email format, range) from the matching command/query
/// onto the request schema — so Scalar/Swagger show formats without duplicating the attributes.
///
/// Convention: <c>FooRequest</c> maps to <c>FooCommand</c> or <c>FooQuery</c> in the same namespace.
/// Properties are matched by name (case-insensitive), so camelCase JSON props line up with the
/// PascalCase command properties.
/// </summary>
internal sealed class CommandValidationSchemaTransformer : IOpenApiSchemaTransformer
{
    // Reflection results are stable for a given request type — cache them.
    private static readonly ConcurrentDictionary<Type, Type?> CommandFor = new();

    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        Type requestType = context.JsonTypeInfo.Type;
        if (!requestType.Name.EndsWith("Request", StringComparison.Ordinal))
            return Task.CompletedTask;

        if (schema.Properties is null || schema.Properties.Count == 0)
            return Task.CompletedTask;

        Type? commandType = CommandFor.GetOrAdd(requestType, ResolveCommandType);
        if (commandType is null)
            return Task.CompletedTask;

        foreach ((string jsonName, IOpenApiSchema propSchema) in schema.Properties)
        {
            if (propSchema is not OpenApiSchema target)
                continue;

            PropertyInfo? prop = commandType.GetProperty(
                jsonName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop is null)
                continue;

            ValidationSchemaRules.Apply(prop.GetCustomAttributes<ValidationAttribute>(inherit: true), target, schema, jsonName);
        }

        return Task.CompletedTask;
    }

    private static Type? ResolveCommandType(Type requestType)
    {
        string baseName = requestType.Name[..^"Request".Length];
        string? ns = requestType.Namespace;
        Assembly assembly = requestType.Assembly;

        foreach (string suffix in (ReadOnlySpan<string>)["Command", "Query"])
        {
            Type? candidate = assembly.GetType($"{ns}.{baseName}{suffix}");
            if (candidate is not null)
                return candidate;
        }

        return null;
    }
}
