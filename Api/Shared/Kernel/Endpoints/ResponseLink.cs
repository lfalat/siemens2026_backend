using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Kernel.Endpoints;

/// <summary>
/// One OpenAPI <c>link</c>: a named, machine-readable statement that a value in this endpoint's response
/// can be used as a parameter of another operation.
/// </summary>
/// <remarks>
/// <para>
/// A link is the only part of OpenAPI that describes a <em>sequence</em>. Everything else in the document
/// describes one call in isolation, which is why a document without links can be fully conformant and
/// still leave "list job offers, then fetch one by id" untested — the caller has no way to know that the
/// second call's <c>jobOfferId</c> comes from the first call's response. Contract testing reads them to chain
/// operations (<c>docs/architecture/contract-testing.md</c>), and generated clients and Scalar surface
/// them as the next call to make.
/// </para>
/// <para>
/// Declared as endpoint metadata rather than written into the document directly, so a link lives in the
/// feature folder next to the endpoint it describes and cannot be orphaned by a rename that a central
/// table would not notice. <c>ResponseLinkTransformer</c> in the host turns it into document content, and
/// <c>ResponseLinkDeclarationTests</c> (TODO(first-module)) fails the build if a target, a parameter or an
/// expression does not resolve.
/// </para>
/// </remarks>
/// <param name="StatusCode">The response status this link hangs off — the one carrying the value.</param>
/// <param name="TargetOperationId">
/// The <c>operationId</c> of the operation being linked to, which is the name the target endpoint passes
/// to <see cref="RoutingEndpointConventionBuilderExtensions.WithName{TBuilder}"/>.
/// </param>
/// <param name="Parameters">
/// Target parameter name → OpenAPI runtime expression producing its value.
/// </param>
public sealed record ResponseLink(
    int StatusCode,
    string TargetOperationId,
    IReadOnlyDictionary<string, string> Parameters);

/// <summary>Declares OpenAPI links on an endpoint's responses.</summary>
public static class ResponseLinkExtensions
{
    /// <summary>
    /// Declares that this endpoint's <paramref name="statusCode"/> response supplies parameters for
    /// <paramref name="targetOperationId"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Values are OpenAPI <em>runtime expressions</em>. Two forms cover everything here:
    /// </para>
    /// <list type="bullet">
    /// <item><c>$request.path.jobOfferId</c> — a path parameter of <em>this</em> request, carried
    /// through. A link to a nested route needs one for every parent: a link to
    /// <c>/job-offers/{jobOfferId}/applications/{applicationId}</c> that supplies only the application id
    /// leaves the caller inventing an offer, and the call is answered <c>404</c>.</item>
    /// <item><c>$response.body#/items/0/id</c> — a JSON pointer into this response's body. The <c>0</c> is
    /// literal: a link names one value, so a list-returning search offers its first row.</item>
    /// </list>
    /// <para>
    /// The link is named after the target operation, which is unique per response and needs no invention.
    /// Declare several by calling this more than once.
    /// </para>
    /// </remarks>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint builder.</param>
    /// <param name="statusCode">The response status carrying the values — usually 200 or 201.</param>
    /// <param name="targetOperationId">The <c>operationId</c> of the operation being linked to.</param>
    /// <param name="parameters">Target parameter name and the runtime expression that fills it.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static TBuilder ProducesLink<TBuilder>(
        this TBuilder builder,
        int statusCode,
        string targetOperationId,
        params (string Parameter, string Expression)[] parameters)
        where TBuilder : IEndpointConventionBuilder
    {
        ResponseLink link = new(
            statusCode,
            targetOperationId,
            parameters.ToDictionary(p => p.Parameter, p => p.Expression, StringComparer.Ordinal));

        builder.Add(b => b.Metadata.Add(link));
        return builder;
    }
}
