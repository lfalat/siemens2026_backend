namespace Kernel.Auth;

/// <summary>
/// The app roles defined on the HealthyHires API app registration in Entra ID, as they arrive in the access
/// token's <c>roles</c> claim (iam.md §2.3). Each staff user is assigned exactly one, on the enterprise
/// application; a free tenant cannot assign them to groups.
/// </summary>
public static class StaffRoles
{
    /// <summary>Recruiter (<i>náborový pracovník</i>): runs the hiring process — job offers and applicants.</summary>
    public const string Recruiter = "Recruiter";

    /// <summary>Hiring manager (<i>prijímajúci manažér</i>): the manager a position is filled for; reviews its candidates.</summary>
    public const string HiringManager = "HiringManager";

    /// <summary>Administrator (<i>administrátor</i>): manages the application; admitted by every staff policy.</summary>
    public const string Admin = "Admin";

    /// <summary>Every role; holding any of them makes the caller staff.</summary>
    public static IReadOnlyList<string> All { get; } = [Recruiter, HiringManager, Admin];
}
