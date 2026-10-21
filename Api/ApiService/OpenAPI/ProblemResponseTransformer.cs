using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using Kernel.CQRS;
using Kernel.Idempotency;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.OpenApi;

namespace ApiService.OpenAPI;

/// <summary>
/// Operation transformer that declares the problem responses the host answers by rule rather than per endpoint:
/// <list type="bullet">
/// <item>a <c>409</c> on every write except a <c>MapQuery</c> read, for a stale optimistic-concurrency version;</item>
/// <item>a <c>403</c> wherever an authorization policy applies;</item>
/// <item>a <c>400</c> where the dispatched command has validation rules, which the validation decorator enforces.</item>
/// </list>
/// Without it the document omits statuses the API answers, and Schemathesis (ADR-009) fails on them.
/// </summary>
internal sealed class ProblemResponseTransformer : IOpenApiOperationTransformer
{
    /// <inheritdoc />
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        IList<object> metadata = context.Description.ActionDescriptor.EndpointMetadata;
        string? method = context.Description.HttpMethod;

        if (method is not null && !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method)
            && !metadata.OfType<ReadOnlyEndpoint>().Any())
            Add(operation, StatusCodes.Status409Conflict);

        if (metadata.OfType<IAuthorizeData>().Any(static a => a.Policy is not null || a.Roles is not null))
            Add(operation, StatusCodes.Status403Forbidden);

        if (DispatchesValidatedCommand(metadata))
            Add(operation, StatusCodes.Status400BadRequest);

        return Task.CompletedTask;
    }

    /// <summary>Declares <paramref name="status"/>, leaving a response the endpoint already declared alone.</summary>
    private static void Add(OpenApiOperation operation, int status)
    {
        operation.Responses ??= [];
        operation.Responses.TryAdd(
            status.ToString(CultureInfo.InvariantCulture),
            new OpenApiResponse { Description = ReasonPhrases.GetReasonPhrase(status) });
    }

    /// <summary>
    /// Whether the command this endpoint dispatches carries a validation attribute — read off the
    /// <c>ICommandHandler</c> its delegate takes, since the command, not the request contract, carries the attributes.
    /// </summary>
    private static bool DispatchesValidatedCommand(IList<object> metadata) =>
        metadata.OfType<MethodInfo>().FirstOrDefault() is { } handler
        && handler.GetParameters()
            .Select(static p => p.ParameterType)
            .Where(static t => t.IsGenericType
                               && (t.GetGenericTypeDefinition() == typeof(ICommandHandler<>)
                                   || t.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)))
            .Select(static t => t.GetGenericArguments()[0])
            .SelectMany(static command => command.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Any(static p => p.GetCustomAttributes<ValidationAttribute>(inherit: true).Any());
}
