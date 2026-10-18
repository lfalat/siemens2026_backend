using System.Security.Claims;
using Kernel.Auth;

namespace ApiService.Endpoints;

/// <summary>The signed-in staff user as the API sees them.</summary>
/// <param name="Id">The Entra object id (<c>oid</c>) — the value stored as e.g. <c>created_by</c>.</param>
/// <param name="Name">The display name, when the token carries one.</param>
/// <param name="Username">The sign-in name, when the token carries one.</param>
/// <param name="Roles">The caller's app roles (<see cref="StaffRoles"/>).</param>
public sealed record CurrentStaffResponse(Guid Id, string? Name, string? Username, IReadOnlyList<string> Roles);

/// <summary>
/// <c>GET /api/v1/me</c>: who the bearer token belongs to. Lets the frontend and the test tools confirm that a token
/// is accepted and which roles it carries, without depending on any module.
/// </summary>
internal static class MeEndpoint
{
    /// <summary>Maps the endpoint onto the versioned API group, behind the staff policy.</summary>
    /// <param name="api">The versioned API group.</param>
    public static void MapMe(this RouteGroupBuilder api) =>
        api.MapGroup("/me")
            .RequireStaff()
            .MapGet("", static (ClaimsPrincipal user) => new CurrentStaffResponse(
                user.GetStaffId(),
                user.FindFirstValue(StaffClaims.Name),
                user.FindFirstValue(StaffClaims.PreferredUsername) ?? user.FindFirstValue(StaffClaims.UniqueName),
                [.. user.FindAll(StaffClaims.Roles).Select(static c => c.Value)]))
            .WithName("GetCurrentStaff");
}
