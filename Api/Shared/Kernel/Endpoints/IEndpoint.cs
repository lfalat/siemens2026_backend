using Microsoft.AspNetCore.Routing;

namespace Kernel.Endpoints;

/// <summary>
/// A single HTTP endpoint that maps its own route(s) onto a module's route group. Implementations are
/// discovered by an assembly scan and mapped by <see cref="EndpointRegistration.MapEndpoints"/> — so a new
/// endpoint is wired up simply by implementing this interface, mirroring how <c>AddCqrsHandlers</c>
/// auto-registers handlers. This replaces the hand-written <c>group.MapX();</c> list in each module root.
/// </summary>
/// <remarks>
/// Implementations must be concrete classes with a public parameterless constructor: they are stateless
/// route mappers (request-time services are injected into the route-handler delegate, not the class), so the
/// scanner instantiates them directly.
/// </remarks>
public interface IEndpoint
{
    /// <summary>Maps this endpoint's route(s) onto <paramref name="group"/>.</summary>
    /// <param name="group">
    /// The module's route group, already carrying the group prefix, tags, and any shared metadata.
    /// </param>
    void MapEndpoint(IEndpointRouteBuilder group);
}
