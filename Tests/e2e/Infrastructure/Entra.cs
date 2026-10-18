using OtpNet;

namespace E2E.Infrastructure;

/// <summary>
/// Microsoft's sign-in and sign-out pages, driven as a person would. The selectors here are Microsoft's, not ours:
/// when Entra changes its pages, this is what breaks.
/// </summary>
internal static class Entra
{
    private static readonly SemaphoreSlim CodeLock = new(1, 1);
    private static readonly Dictionary<string, long> LastCodeStep = new(StringComparer.Ordinal);

    /// <summary>
    /// Signs in through the frontend: /staff redirects to Entra and back. Returns once MSAL holds its access token for
    /// the API. On failure, says where it stopped: the page's address, its text and a screenshot.
    /// </summary>
    /// <param name="page">A page on the frontend, in a context without an Entra session.</param>
    /// <param name="user">Who signs in.</param>
    /// <param name="label">Names the screenshot of a failed sign-in, e.g. the role.</param>
    public static async Task SignInAsync(IPage page, TestUser user, string label)
    {
        try
        {
            await page.GotoAsync("/staff");
            await page.Locator("input[name=loginfmt]").FillAsync(user.Username);
            await page.Locator("#idSIButton9").ClickAsync();
            await page.Locator("input[name=passwd]").FillAsync(user.Password);
            await page.Locator("#idSIButton9").ClickAsync();

            await FinishSignInAsync(page, user);

            // Back on /staff, MSAL has finished once its access token for the API is in sessionStorage.
            await page.WaitForFunctionAsync("() => Object.keys(sessionStorage).some(k => k.includes('accesstoken'))");
        }
        catch (Exception e)
        {
            string screenshot = Path.Combine(AppContext.BaseDirectory, "sign-in-failures", $"{label}.png");
            await page.ScreenshotAsync(new() { Path = screenshot, FullPage = true });
            string text = await page.Locator("body").InnerTextAsync(new() { Timeout = 5000 });
            throw new InvalidOperationException(
                $"Signing in as {label} stopped at {page.Url}: {e.Message}\nScreenshot: {screenshot}\nThe page said:\n{text.Trim()}", e);
        }
    }

    /// <summary>
    /// After the frontend's "Sign out", whatever Entra asks until it sends the browser back: which account to sign out,
    /// when Entra was not told (the ID token has no <c>login_hint</c> claim).
    /// </summary>
    /// <param name="page">The page that clicked "Sign out".</param>
    /// <param name="user">Who is signing out.</param>
    public static async Task FinishSignOutAsync(IPage page, TestUser user)
    {
        ILocator account = page.Locator($"[data-test-id='{user.Username}']");
        bool accountPicked = false;
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);

        // The first check waits for MSAL to leave the frontend, so the loop does not end before it started.
        await page.WaitForURLAsync(url => !url.StartsWith(E2ESettings.BaseUrl, StringComparison.Ordinal));

        while (!page.Url.StartsWith(E2ESettings.BaseUrl, StringComparison.Ordinal))
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Sign-out did not return to {E2ESettings.BaseUrl}; it stopped at {page.Url}.");

            if (!accountPicked && await account.IsVisibleAsync())
            {
                await account.ClickAsync();
                accountPicked = true;
            }

            await page.WaitForTimeoutAsync(250);
        }
    }

    /// <summary>
    /// After the password, whatever Entra asks next until it sends the browser back: an authenticator code (only when
    /// Entra decides MFA is due, which under security defaults is not every sign-in), and "Stay signed in?".
    /// </summary>
    private static async Task FinishSignInAsync(IPage page, TestUser user)
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
    /// The authenticator code for now. Entra refuses a code it has already accepted, so a code already used in this run
    /// (a user who signs in twice) is skipped, and so is one about to expire, which could be the previous run's.
    /// </summary>
    private static async Task<string> CurrentCodeAsync(string secret)
    {
        await CodeLock.WaitAsync();
        try
        {
            Totp totp = new(Base32Encoding.ToBytes(secret));
            while (totp.RemainingSeconds() < 5 || LastCodeStep.GetValueOrDefault(secret) >= CurrentStep())
                await Task.Delay(TimeSpan.FromSeconds(totp.RemainingSeconds() + 1));

            LastCodeStep[secret] = CurrentStep();
            return totp.ComputeTotp();
        }
        finally
        {
            CodeLock.Release();
        }
    }

    /// <summary>The current 30-second TOTP time step.</summary>
    private static long CurrentStep() => DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
}
