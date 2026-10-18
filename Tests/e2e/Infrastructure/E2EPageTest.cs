using System.Text.Json;
using Microsoft.Playwright.Xunit.v3;

namespace E2E.Infrastructure;

/// <summary>A test with its own page on the frontend, as an anonymous visitor (an applicant).</summary>
public abstract class E2EPageTest : PageTest
{
    /// <inheritdoc />
    public override BrowserNewContextOptions ContextOptions() => new() { BaseURL = E2ESettings.BaseUrl };
}

/// <summary>
/// A test that signs in as one or more staff roles. Each <see cref="SignInAsAsync"/> opens a fresh context with that
/// role's session from <see cref="StaffSession"/>, so a single test can compare what two roles see.
/// </summary>
/// <param name="session">The run's shared sessions (an assembly fixture).</param>
public abstract class StaffTest(StaffSession session) : BrowserTest
{
    /// <summary>The run's shared sessions, one per role.</summary>
    protected StaffSession Session => session;

    /// <summary>A new page on the frontend, signed in as <paramref name="role"/>'s test user.</summary>
    /// <param name="role">One of <see cref="StaffRole"/>.</param>
    protected async Task<IPage> SignInAsAsync(string role)
    {
        SignedInState state = await session.GetAsync(role);
        IBrowserContext context = await NewContext(new()
        {
            BaseURL = E2ESettings.BaseUrl,
            StorageState = state.StorageState,
        });

        // Puts MSAL's sessionStorage back before the frontend's scripts run, on the frontend's origin only, and
        // without overwriting anything MSAL has written since (a refreshed token).
        string origin = new Uri(E2ESettings.BaseUrl).GetLeftPart(UriPartial.Authority);
        await context.AddInitScriptAsync($$"""
            if (location.origin === {{JsonSerializer.Serialize(origin)}}) {
              for (const [key, value] of Object.entries({{state.SessionStorage}})) {
                if (sessionStorage.getItem(key) === null) sessionStorage.setItem(key, value);
              }
            }
            """);

        return await context.NewPageAsync();
    }
}
