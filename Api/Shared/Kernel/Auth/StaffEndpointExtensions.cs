using Kernel.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Kernel.Auth;

/// <summary>Marks a route group as staff-only.</summary>
public static class StaffEndpointExtensions
{
    /// <summary>
    /// Requires <paramref name="policy"/> on every endpoint in the group and declares <c>auth.unauthenticated</c> for
    /// them; <c>auth.forbidden</c> follows by rule wherever a policy applies (ADR-A017). An endpoint in the group
    /// that needs a specific role adds e.g. <c>.RequireAuthorization(StaffPolicies.Admin)</c>; both policies then apply.
    /// </summary>
    /// <param name="group">The module's staff route group.</param>
    /// <param name="policy">One of <see cref="StaffPolicies"/>. Defaults to any staff role.</param>
    /// <returns>The same <paramref name="group"/> for chaining.</returns>
    public static RouteGroupBuilder RequireStaff(this RouteGroupBuilder group, string policy = StaffPolicies.Staff)
    {
        group.RequireAuthorization(policy);
        return group.ProducesErrors(CommonErrors.Unauthenticated);
    }
}
