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
/// A test of the staff area. Each method below opens a new page in its own browser context, so a single test can
/// compare what two roles see, or what a signed-out visitor sees. The sign-ins behind them are handled here
/// (<see cref="StaffSession"/>); a test only says who it wants to be.
/// </summary>
public abstract class StaffTest : BrowserTest
{
    private static async Task<StaffSession> SessionAsync() =>
        await TestContext.Current.GetFixture<StaffSession>()
        ?? throw new InvalidOperationException("The StaffSession assembly fixture is missing.");

    /// <summary>
    /// A new page on the frontend, signed in as <paramref name="role"/>'s test user. Quick: the role signs in once per
    /// run and every page reuses that session. Use it for every staff test that does not sign out.
    /// </summary>
    /// <param name="role">One of <see cref="StaffRole"/>.</param>
    protected async Task<IPage> SignInAsAsync(string role)
    {
        SignedInState state = await (await SessionAsync()).GetAsync(role);
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

    /// <summary>
    /// A new page on the frontend, signed in as <paramref name="role"/>'s test user with a sign-in of its own, on
    /// <c>/staff</c>. Slower (a full Entra sign-in), so only for a test that signs out: signing out ends the Entra
    /// session, which would break the other tests if it were the shared one.
    /// </summary>
    /// <param name="role">One of <see cref="StaffRole"/>.</param>
    protected async Task<IPage> SignInSeparatelyAsAsync(string role)
    {
        // The shared sign-in first: if this user cannot sign in, that failure is reported here too, rather than the
        // password and code being tried again.
        await (await SessionAsync()).GetAsync(role);

        IPage page = await SignedOutPageAsync();
        await Entra.SignInAsync(page, E2ESettings.UserFor(role), $"{role}-separately");
        return page;
    }

    /// <summary>A new page on the frontend, as a visitor who is not signed in.</summary>
    protected async Task<IPage> SignedOutPageAsync()
    {
        IBrowserContext context = await NewContext(new() { BaseURL = E2ESettings.BaseUrl });
        return await context.NewPageAsync();
    }

    /// <summary>
    /// Finishes a sign-out on Entra's pages, after the test clicked "Sign out" on <paramref name="page"/>, and returns
    /// once the browser is back on the frontend.
    /// </summary>
    /// <param name="page">A page from <see cref="SignInSeparatelyAsAsync"/>.</param>
    /// <param name="role">The role it signed in as.</param>
    protected static Task FinishSignOutAsync(IPage page, string role) =>
        Entra.FinishSignOutAsync(page, E2ESettings.UserFor(role));
}
