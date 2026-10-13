using Microsoft.AspNetCore.Builder;

namespace Kernel.Endpoints;

/// <summary>
/// One request header an endpoint refuses to run without, declared so the OpenAPI document says so.
/// </summary>
/// <remarks>
/// <para>
/// A header enforced by a filter or by middleware is invisible to everything downstream of the document: a
/// generated client never sends it, and a request that satisfies the whole schema is answered <c>400</c> —
/// which a Schemathesis run reports as the API rejecting its own contract (ADR-009).
/// </para>
/// <para>
/// Declared as endpoint metadata rather than written into the document directly, so it sits with the code
/// that enforces it — including on a route group, where the enforcement usually lives and where the
/// metadata reaches every endpoint in the group. <c>RequiredHeaderTransformer</c> in the host turns it
/// into a parameter and the <c>400</c> it implies.
/// </para>
/// </remarks>
/// <param name="Name">The header name, exactly as the enforcing code reads it.</param>
/// <param name="Description">What the header carries and what the endpoint does with it.</param>
/// <param name="Format">
/// The OpenAPI <c>format</c> for the header's value (e.g. <c>uuid</c>), or <c>null</c> for a plain string.
/// The value is always a string on the wire; this narrows what a valid one looks like.
/// </param>
public sealed record RequiredHeader(string Name, string Description, string? Format = null);

/// <summary>Declares request headers an endpoint or group requires.</summary>
public static class RequiredHeaderExtensions
{
    /// <summary>
    /// Declares that this endpoint — or every endpoint in this group — requires <paramref name="name"/>,
    /// and documents the <c>400</c> that follows when it is missing or malformed.
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint or group builder.</param>
    /// <param name="name">The header name, exactly as the enforcing code reads it.</param>
    /// <param name="description">What the header carries and what the endpoint does with it.</param>
    /// <param name="format">The OpenAPI <c>format</c> of the value, or <c>null</c> for a plain string.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static TBuilder RequiresHeader<TBuilder>(
        this TBuilder builder, string name, string description, string? format = null)
        where TBuilder : IEndpointConventionBuilder
    {
        RequiredHeader header = new(name, description, format);
        builder.Add(b => b.Metadata.Add(header));
        return builder;
    }
}
