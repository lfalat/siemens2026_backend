using Microsoft.Extensions.Configuration;

namespace E2E.Infrastructure;

/// <summary>The app roles of the HealthyHires API registration, one test user each (iam.md §2.3).</summary>
public static class StaffRole
{
    /// <summary>Recruiter (<i>náborový pracovník</i>).</summary>
    public const string Recruiter = "Recruiter";

    /// <summary>Hiring manager (<i>prijímajúci manažér</i>).</summary>
    public const string HiringManager = "HiringManager";

    /// <summary>Administrator, admitted by every staff policy.</summary>
    public const string Admin = "Admin";
}

/// <summary>A test user's sign-in details.</summary>
/// <param name="Username">The sign-in name.</param>
/// <param name="Password">The password.</param>
/// <param name="TotpSecret">The Base32 secret of the user's authenticator app, which the setup turns into codes.</param>
internal sealed record TestUser(string Username, string Password, string TotpSecret);

/// <summary>
/// Where the tests point and who signs in: user secrets of this project, overridden by environment variables
/// (<c>E2E__BaseUrl</c>, <c>E2E__Users__Admin__Username</c>, ...). Only staff tests need users, and only the roles
/// they sign in as, so the applicant tests run without any.
/// </summary>
internal static class E2ESettings
{
    private static readonly IConfiguration Configuration = new ConfigurationBuilder()
        .AddUserSecrets(typeof(E2ESettings).Assembly, optional: true)
        .AddEnvironmentVariables()
        .Build();

    /// <summary>The frontend: the Vite dev server by default, or the local Caddy (<c>http://localhost:8088</c>).</summary>
    public static string BaseUrl => (Configuration["E2E:BaseUrl"] ?? "http://localhost:5173").TrimEnd('/');

    /// <summary>Whether to show the browser (<c>HEADED=1</c>), as the Playwright test base classes do.</summary>
    public static bool Headed => Environment.GetEnvironmentVariable("HEADED") == "1";

    /// <summary>The test user holding <paramref name="role"/>.</summary>
    /// <param name="role">One of <see cref="StaffRole"/>.</param>
    public static TestUser UserFor(string role) => new(
        Required(role, "Username"), Required(role, "Password"), Required(role, "TotpSecret"));

    private static string Required(string role, string key) =>
        Configuration[$"E2E:Users:{role}:{key}"] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"E2E:Users:{role}:{key} is not set. Staff tests need a test user per role: see docs/testing/e2e.md.");
}
