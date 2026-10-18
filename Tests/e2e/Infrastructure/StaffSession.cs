using System.Collections.Concurrent;
using E2E.Infrastructure;

[assembly: AssemblyFixture(typeof(StaffSession))]

namespace E2E.Infrastructure;

/// <summary>What a signed-in browser holds, to be handed to a staff test's fresh context.</summary>
/// <param name="StorageState">Cookies and localStorage, including Entra's session cookies.</param>
/// <param name="SessionStorage">
/// The frontend's sessionStorage as a JSON object. MSAL keeps its tokens there (ADR-F006), and Playwright's storage
/// state does not carry sessionStorage, so it is restored separately (<see cref="StaffTest"/>).
/// </param>
public sealed record SignedInState(string StorageState, string SessionStorage);

/// <summary>
/// The test users' sessions, one per role, shared by every staff test in the run (ADR-019). A role signs in the first
/// time a test asks for it, so a run only needs the users of the roles it uses. A failed sign-in is kept and reported
/// to every test of that role, instead of retried per test: repeated failures with codes would lock the user out.
/// </summary>
public sealed class StaffSession
{
    private readonly ConcurrentDictionary<string, Lazy<Task<SignedInState>>> _states = new(StringComparer.Ordinal);

    /// <summary>The signed-in state of <paramref name="role"/>'s test user, signing in the first time it is asked for.</summary>
    /// <param name="role">One of <see cref="StaffRole"/>.</param>
    public Task<SignedInState> GetAsync(string role) =>
        _states.GetOrAdd(role, static r => new Lazy<Task<SignedInState>>(() => SignInAsync(r, E2ESettings.UserFor(r)))).Value;

    private static async Task<SignedInState> SignInAsync(string role, TestUser user)
    {
        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(new() { Headless = !E2ESettings.Headed });
        await using IBrowserContext context = await browser.NewContextAsync(new() { BaseURL = E2ESettings.BaseUrl });
        IPage page = await context.NewPageAsync();

        await Entra.SignInAsync(page, user, role);

        return new SignedInState(
            await context.StorageStateAsync(),
            await page.EvaluateAsync<string>("() => JSON.stringify(sessionStorage)"));
    }
}
