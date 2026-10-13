using Kernel.Idempotency;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kernel.Errors;

/// <summary>
/// Endpoint metadata: the error codes an endpoint, or every endpoint in a group, can answer with. The OpenAPI
/// transformer lists them under each error response, which is where a client learns which codes to translate
/// (ADR-A017). <c>ErrorCodeDeclarationTests</c> and <c>DeclaredStatusCodeTests</c> (TODO(first-module)) fail the
/// build when an endpoint can reach a code or a status it does not declare.
/// </summary>
/// <param name="Codes">The declared codes.</param>
/// <param name="Group">Whether a route group declared them for every endpoint in it, rather than the endpoint itself.</param>
public sealed record ErrorCodeDeclaration(IReadOnlyList<IErrorCode> Codes, bool Group = false);

/// <summary>Declares the error codes an endpoint or group answers with.</summary>
public static class ErrorCodeDeclarationExtensions
{
    /// <summary>
    /// Declares the codes this endpoint can answer with, and the problem response for each code's status, so the
    /// endpoint lists no <c>ProducesProblem</c> for a status its codes cover.
    /// </summary>
    /// <param name="builder">The endpoint builder.</param>
    /// <param name="codes">The codes the endpoint's handler, and the services it calls, can produce.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static RouteHandlerBuilder ProducesErrors(this RouteHandlerBuilder builder, params IErrorCode[] codes)
    {
        builder.WithMetadata(new ErrorCodeDeclaration(codes));
        foreach (int status in Statuses(codes))
        {
            if (status == StatusCodes.Status400BadRequest && codes.Any(static c => c is FieldErrorCode))
                builder.ProducesValidationProblem();
            else
                builder.ProducesProblem(status);
        }
        return builder;
    }

    /// <summary>Declares the codes every endpoint in this group can answer with, and their problem responses.</summary>
    /// <param name="builder">The group builder.</param>
    /// <param name="codes">The codes every endpoint in the group can produce.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static RouteGroupBuilder ProducesErrors(this RouteGroupBuilder builder, params IErrorCode[] codes)
    {
        builder.WithMetadata(new ErrorCodeDeclaration(codes, Group: true));
        foreach (int status in Statuses(codes))
            builder.ProducesProblem(status);
        return builder;
    }

    /// <summary>
    /// Every code an endpoint with <paramref name="metadata"/> declares: its own and its groups' declarations, plus the
    /// codes added by rule — <see cref="CommonErrors.ConcurrencyConflict"/> on every write,
    /// <see cref="CommonErrors.Forbidden"/> where an authorization policy applies, and
    /// <see cref="CommonErrors.ValidationFailed"/> wherever a field code is declared.
    /// </summary>
    /// <param name="metadata">The endpoint's metadata.</param>
    /// <param name="httpMethod">The endpoint's HTTP method.</param>
    public static IReadOnlyList<IErrorCode> Declared(IEnumerable<object> metadata, string? httpMethod)
    {
        List<object> items = [.. metadata];
        List<IErrorCode> codes = [.. items.OfType<ErrorCodeDeclaration>().SelectMany(static d => d.Codes)];

        if (httpMethod is not null && !HttpMethods.IsGet(httpMethod) && !HttpMethods.IsHead(httpMethod)
            && !items.OfType<ReadOnlyEndpoint>().Any())
            codes.Add(CommonErrors.ConcurrencyConflict);

        if (items.OfType<IAuthorizeData>().Any(static a => a.Policy is not null || a.Roles is not null))
            codes.Add(CommonErrors.Forbidden);

        if (codes.Any(static c => c is FieldErrorCode))
            codes.Add(CommonErrors.ValidationFailed);

        return [.. codes.Distinct()];
    }

    /// <summary>The distinct statuses <paramref name="codes"/> are answered with; a field code is a <c>400</c>.</summary>
    private static IEnumerable<int> Statuses(IEnumerable<IErrorCode> codes) =>
        codes.Select(static c => c is ErrorCode problem ? problem.Status : StatusCodes.Status400BadRequest).Distinct();
}
