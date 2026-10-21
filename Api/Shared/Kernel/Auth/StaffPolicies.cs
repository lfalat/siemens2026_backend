namespace Kernel.Auth;

/// <summary>
/// Names of the authorization policies a staff endpoint can require. The host registers them; a module only names
/// them, through <see cref="StaffEndpointExtensions.RequireStaff"/> on its staff group or
/// <c>.RequireAuthorization(StaffPolicies.Recruiter)</c> on a single endpoint.
/// </summary>
/// <remarks>
/// <see cref="StaffRoles.Recruiter"/> and <see cref="StaffRoles.HiringManager"/> are peers, each with a policy of its
/// own; <see cref="StaffRoles.Admin"/> is admitted by every policy. A stricter endpoint inside a staff group narrows
/// the group's policy rather than replacing it. Every policy also requires a valid Entra access token for this API
/// with the <c>access_as_user</c> scope; a caller without one gets <c>401</c>, a caller without a fitting role
/// gets <c>403</c>.
/// </remarks>
public static class StaffPolicies
{
    /// <summary>Any staff role. Also the default policy.</summary>
    public const string Staff = "staff";

    /// <summary><see cref="StaffRoles.Recruiter"/> or <see cref="StaffRoles.Admin"/>.</summary>
    public const string Recruiter = "staff-recruiter";

    /// <summary><see cref="StaffRoles.HiringManager"/> or <see cref="StaffRoles.Admin"/>.</summary>
    public const string HiringManager = "staff-hiring-manager";

    /// <summary><see cref="StaffRoles.Admin"/> only.</summary>
    public const string Admin = "staff-admin";
}
