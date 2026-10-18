using System.Collections.Concurrent;
using E2E.Infrastructure;
using OtpNet;

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

    /// <summary>Signs in, and on failure says where it stopped: the page's address, its text and a screenshot.</summary>
    private static async Task<SignedInState> SignInAsync(string role, TestUser user)
    {
        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(new() { Headless = !E2ESettings.Headed });
        await using IBrowserContext context = await browser.NewContextAsync(new() { BaseURL = E2ESettings.BaseUrl });
        IPage page = await context.NewPageAsync();

        try
        {
            return await SignInAsync(context, page, user);
        }
        catch (Exception e)
        {
            string screenshot = Path.Combine(AppContext.BaseDirectory, "sign-in-failures", $"{role}.png");
            await page.ScreenshotAsync(new() { Path = screenshot, FullPage = true });
            string text = await page.Locator("body").InnerTextAsync(new() { Timeout = 5000 });
            throw new InvalidOperationException(
                $"Signing in as {role} stopped at {page.Url}: {e.Message}\nScreenshot: {screenshot}\nThe page said:\n{text.Trim()}", e);
        }
    }

    /// <summary>Signs in through the frontend, exactly as a person would: /staff redirects to Entra and back.</summary>
    private static async Task<SignedInState> SignInAsync(IBrowserContext context, IPage page, TestUser user)
    {
        // The selectors below are Microsoft's sign-in page, not ours. When it changes, this is what breaks.
        await page.GotoAsync("/staff");
        await page.Locator("input[name=loginfmt]").FillAsync(user.Username);
        await page.Locator("#idSIButton9").ClickAsync();
        await page.Locator("input[name=passwd]").FillAsync(user.Password);
        await page.Locator("#idSIButton9").ClickAsync();

        await FinishOnEntraAsync(page, user);

        // Back on /staff, MSAL has finished once its access token for the API is in sessionStorage.
        await page.WaitForFunctionAsync("() => Object.keys(sessionStorage).some(k => k.includes('accesstoken'))");

        return new SignedInState(
            await context.StorageStateAsync(),
            await page.EvaluateAsync<string>("() => JSON.stringify(sessionStorage)"));
    }

    /// <summary>
    /// After the password, whatever Entra asks next until it sends the browser back: an authenticator code (only when
    /// Entra decides MFA is due, which under security defaults is not every sign-in), and "Stay signed in?".
    /// </summary>
    private static async Task FinishOnEntraAsync(IPage page, TestUser user)
    {
        ILocator code = page.Locator("input[name=otc]");
        ILocator otherWay = page.Locator("#signInAnotherWay");
        ILocator appCode = page.Locator("[data-value=PhoneAppOTP]");
        ILocator staySignedIn = page.Locator("#KmsiCheckboxField");
        ILocator mfaSetup = page.GetByText("Let's keep your account secure").Or(page.GetByText("More information required"));

        bool switchedToCode = false, kmsiAnswered = false;
        DateTime? codeSubmitted = null;
        DateTime deadline = DateTime.UtcNow.AddSeconds(60);

        while (!page.Url.StartsWith(E2ESettings.BaseUrl, StringComparison.Ordinal))
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Sign-in did not return to {E2ESettings.BaseUrl}.");

            if (await mfaSetup.First.IsVisibleAsync())
                throw new InvalidOperationException(
                    "Entra wants this user to set up MFA first, so it has no authenticator registered. Register one at " +
                    "https://mysignins.microsoft.com/security-info as the user (docs/testing/e2e.md §1.1), then run " +
                    "'just e2e-setup' again with its secret key.");

            if (codeSubmitted is null && await code.IsVisibleAsync())
            {
                await code.FillAsync(await CurrentCodeAsync(user.TotpSecret));
                await page.Locator("#idSubmit_SAOTCC_Continue").ClickAsync();
                codeSubmitted = DateTime.UtcNow;
            }
            // Still asking for a code well after one was sent: Entra refused it. Never retry; wrong codes lock the user out.
            else if (codeSubmitted is { } sent && DateTime.UtcNow - sent > TimeSpan.FromSeconds(5) && await code.IsVisibleAsync())
            {
                throw new InvalidOperationException(
                    "Entra refused the authenticator code. Check this user's secret key ('just e2e-setup'), and that the " +
                    "computer's clock is right.");
            }
            // A user whose default method is the Authenticator push is offered other ways; pick the app's code.
            else if (!switchedToCode && codeSubmitted is null && await otherWay.IsVisibleAsync())
            {
                await otherWay.ClickAsync();
                await appCode.ClickAsync();
                switchedToCode = true;
            }
            else if (!kmsiAnswered && await staySignedIn.IsVisibleAsync())
            {
                await page.Locator("#idSIButton9").ClickAsync();
                kmsiAnswered = true;
            }

            await page.WaitForTimeoutAsync(250);
        }
    }

    /// <summary>
    /// The authenticator code for now. Entra refuses a code it has already accepted, so one about to expire is skipped
    /// rather than risk it being the code of the previous run.
    /// </summary>
    private static async Task<string> CurrentCodeAsync(string secret)
    {
        Totp totp = new(Base32Encoding.ToBytes(secret));
        if (totp.RemainingSeconds() < 5)
            await Task.Delay(TimeSpan.FromSeconds(totp.RemainingSeconds() + 1));
        return totp.ComputeTotp();
    }
}
