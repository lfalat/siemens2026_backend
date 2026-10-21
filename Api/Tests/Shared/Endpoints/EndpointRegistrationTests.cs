using Kernel.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Tests.Shared.Endpoints;

/// <summary>
/// <see cref="EndpointRegistration.MapEndpoints"/> maps every <see cref="IEndpoint"/> in an assembly onto the group it
/// is given — a new endpoint is wired up by implementing the interface, not by adding a line to a module root — and in
/// a stable order, so the OpenAPI document does not reshuffle between builds.
/// </summary>
public class EndpointRegistrationTests
{
    /// <summary>Two endpoints, declared out of name order to show the mapping order is by type name, not declaration.</summary>
    internal sealed class ZetaEndpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder group) => group.MapGet("/zeta", static () => Results.Ok());
    }

    internal sealed class AlphaEndpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder group) => group.MapGet("/alpha", static () => Results.Ok());
    }

    private abstract class NotAnEndpointToMap : IEndpoint
    {
        public abstract void MapEndpoint(IEndpointRouteBuilder group);
    }

    [Fact]
    public void Every_endpoint_in_the_assembly_is_mapped_on_the_group_in_type_name_order()
    {
        WebApplication app = WebApplication.CreateSlimBuilder().Build();

        app.MapGroup("/api/v1/probe").MapEndpoints(typeof(EndpointRegistrationTests).Assembly);

        List<string> routes = [.. ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(static s => s.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(static e => e.RoutePattern.RawText!)
            .Where(static r => r.StartsWith("/api/v1/probe/", StringComparison.Ordinal))];

        Assert.Equal(["/api/v1/probe/alpha", "/api/v1/probe/zeta"], routes);
    }
}
