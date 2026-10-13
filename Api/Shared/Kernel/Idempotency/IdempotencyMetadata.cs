using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Kernel.Idempotency;

/// <summary>
/// Endpoint metadata overriding the idempotency <see cref="IdempotencyOptions.DefaultTtl"/> for a single
/// endpoint. Attach it with <see cref="IdempotencyEndpointExtensions.WithIdempotencyTtl{TBuilder}"/>.
/// </summary>
/// <param name="Ttl">How long this endpoint's successful responses are remembered and replayed.</param>
public sealed record IdempotencyTtl(TimeSpan Ttl);

/// <summary>
/// Endpoint metadata marker that opts an endpoint out of idempotency entirely — the middleware passes the
/// request straight through, neither requiring a key nor caching the response. Attach it with
/// <see cref="IdempotencyEndpointExtensions.DisableIdempotency{TBuilder}"/>. Use for endpoints that carry
/// their own dedup contract, or that are not business writes at all (e.g. a SignalR hub handshake).
/// </summary>
/// <remarks>
/// Carries no state — everything it means is in the type, and readers only ever ask whether an endpoint has
/// one. So there is exactly one, and no way to make another: two of them could differ in nothing, and a
/// marker that is asked for by type gains nothing from being allocated per endpoint.
/// </remarks>
public sealed class IdempotencyDisabled
{
    /// <summary>The marker to attach to an endpoint's metadata.</summary>
    public static readonly IdempotencyDisabled Instance = new();

    private IdempotencyDisabled()
    {
    }
}

/// <summary>Fluent helpers for tuning idempotency on individual endpoints (or a whole route group).</summary>
public static class IdempotencyEndpointExtensions
{
    /// <summary>
    /// Overrides the default idempotency TTL for the endpoint(s) this builder configures. A value set on an
    /// endpoint wins over one set on its group (the endpoint's metadata is read last).
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint (or group) builder.</param>
    /// <param name="ttl">How long successful responses are remembered and replayed.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static TBuilder WithIdempotencyTtl<TBuilder>(this TBuilder builder, TimeSpan ttl)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.Add(b => b.Metadata.Add(new IdempotencyTtl(ttl)));
        return builder;
    }

    /// <summary>
    /// Opts the endpoint(s) this builder configures out of idempotency handling entirely (no key
    /// requirement, no caching).
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint (or group) builder.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static TBuilder DisableIdempotency<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.Add(b => b.Metadata.Add(IdempotencyDisabled.Instance));
        return builder;
    }

    /// <summary>
    /// Maps a read-only query that is exposed as <c>POST</c> because its filters, id arrays and keyset
    /// cursor do not fit a query string. The call is safe and idempotent, so idempotency handling is off:
    /// a query needs no at-most-once protection, must not demand an
    /// <see cref="IdempotencyOptions.HeaderName"/> header, and must never replay a result set captured up
    /// to a TTL ago. Use <c>MapPost</c> directly only for endpoints that actually change state.
    /// </summary>
    /// <param name="group">The route group to register the endpoint on.</param>
    /// <param name="pattern">The route pattern.</param>
    /// <param name="handler">The route handler delegate.</param>
    /// <returns>The builder for the mapped endpoint, for further configuration.</returns>
    /// <remarks>
    /// RFC 10008 gives this shape its own method, <c>QUERY</c>, which is what these endpoints should be.
    /// The blockers are external rather than architectural, so the migration is a change to this method and
    /// to nothing else — see ADR-A015.
    /// <para>
    /// <c>QueryEndpointOptOutTests</c> (TODO(first-module)) fails the build when a query is mapped with a plain
    /// <c>MapPost</c>, or a command with this method.
    /// </para>
    /// </remarks>
    public static RouteHandlerBuilder MapQuery(
        this IEndpointRouteBuilder group, string pattern, Delegate handler) =>
        group.MapPost(pattern, handler)
            .DisableIdempotency()
            .WithMetadata(ReadOnlyEndpoint.Instance);
}

/// <summary>
/// Endpoint metadata marking a <c>POST</c> that only reads (<see cref="IdempotencyEndpointExtensions.MapQuery"/>), so
/// rules about writes — such as documenting an optimistic-concurrency conflict — can leave it out.
/// </summary>
public sealed class ReadOnlyEndpoint
{
    /// <summary>The singleton marker instance.</summary>
    public static readonly ReadOnlyEndpoint Instance = new();

    private ReadOnlyEndpoint()
    {
    }
}
