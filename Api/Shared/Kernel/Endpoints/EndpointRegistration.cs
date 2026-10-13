using System.Reflection;
using Microsoft.AspNetCore.Routing;

namespace Kernel.Endpoints;

/// <summary>
/// Discovers and maps a module's <see cref="IEndpoint"/> implementations. Call once from a module's
/// <c>Map&lt;Name&gt;Module</c> composition root, on the route group the endpoints attach to — it replaces the
/// hand-written list of <c>group.MapX();</c> calls, the same way <c>AddCqrsHandlers</c> replaces registering
/// handlers by hand.
/// </summary>
public static class EndpointRegistration
{
    /// <summary>
    /// Instantiates every concrete <see cref="IEndpoint"/> in <paramref name="assembly"/> and maps it onto
    /// <paramref name="group"/>. Endpoints are mapped in a stable, culture-invariant order (by type full
    /// name) so the generated OpenAPI document keeps a deterministic ordering between builds.
    /// </summary>
    /// <param name="group">The module route group to map the endpoints on.</param>
    /// <param name="assembly">The module assembly to scan for <see cref="IEndpoint"/> implementations.</param>
    /// <returns>The same <paramref name="group"/> for chaining.</returns>
    public static IEndpointRouteBuilder MapEndpoints(this IEndpointRouteBuilder group, Assembly assembly)
    {
        IEnumerable<Type> endpointTypes = assembly.GetTypes()
            .Where(static t => t is { IsClass: true, IsAbstract: false } && typeof(IEndpoint).IsAssignableFrom(t))
            .OrderBy(static t => t.FullName, StringComparer.Ordinal);

        foreach (Type type in endpointTypes)
            ((IEndpoint)Activator.CreateInstance(type)!).MapEndpoint(group);

        return group;
    }
}
