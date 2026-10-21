using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kernel.Auth;

/// <summary>Marks a route group as staff-only.</summary>
public static class StaffEndpointExtensions
{
    /// <summary>
    /// Requires <paramref name="policy"/> on every endpoint in the group and declares its <c>401</c>; the <c>403</c>
    /// follows by rule wherever a policy applies (<c>ProblemResponseTransformer</c>). An endpoint in the group
    /// that needs a specific role adds e.g. <c>.RequireAuthorization(StaffPolicies.Admin)</c>; both policies then apply.
    /// </summary>
    /// <param name="group">The module's staff route group.</param>
    /// <param name="policy">One of <see cref="StaffPolicies"/>. Defaults to any staff role.</param>
    /// <returns>The same <paramref name="group"/> for chaining.</returns>
    public static RouteGroupBuilder RequireStaff(this RouteGroupBuilder group, string policy = StaffPolicies.Staff)
    {
        group.RequireAuthorization(policy);
        return group.ProducesProblem(StatusCodes.Status401Unauthorized);
    }
}
