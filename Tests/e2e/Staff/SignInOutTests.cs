using System.Text.RegularExpressions;
using E2E.Infrastructure;

namespace E2E.Staff;

/// <summary>The "Staff sign in" and "Sign out" buttons, and that both reach Entra.</summary>
public class SignInOutTests(StaffSession session) : StaffTest(session)
{
    private static readonly Regex EntraUrl = new(@"^https://login\.microsoftonline\.com/");
    private static readonly Regex HomeUrl = new($"^{Regex.Escape(E2ESettings.BaseUrl)}/?$");

    [Fact]
    public async Task Staff_sign_in_sends_a_visitor_to_Entra()
    {
        IBrowserContext context = await NewContext(new() { BaseURL = E2ESettings.BaseUrl });
        IPage page = await context.NewPageAsync();
        await page.GotoAsync("/");

        await page.GetByRole(AriaRole.Link, new() { Name = "Staff sign in" }).ClickAsync();

        await Expect(page).ToHaveURLAsync(EntraUrl);
        await Expect(page.Locator("input[name=loginfmt]")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Sign_out_ends_the_session_in_the_app_and_in_Entra()
    {
        // Signing out ends the Entra session, so this test signs in on its own rather than with the shared session.
        // The shared one first: if this user cannot sign in, that failure is reported here too, not attempted again.
        await Session.GetAsync(StaffRole.Recruiter);
        TestUser user = E2ESettings.UserFor(StaffRole.Recruiter);
        IBrowserContext context = await NewContext(new() { BaseURL = E2ESettings.BaseUrl });
        IPage page = await context.NewPageAsync();
        await Entra.SignInAsync(page, user, $"{StaffRole.Recruiter}-sign-out");

        await page.GetByRole(AriaRole.Button, new() { Name = "Sign out" }).ClickAsync();
        await Entra.FinishSignOutAsync(page, user);

        // Back on the public home page, with MSAL's tokens gone from this tab.
        await Expect(page).ToHaveURLAsync(HomeUrl);
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Staff sign in" })).ToBeVisibleAsync();
        Assert.False(await page.EvaluateAsync<bool>("() => Object.keys(sessionStorage).some(k => k.includes('accesstoken'))"));

        // The Entra session ended too: the staff area asks who is signing in instead of signing straight back in.
        await page.GotoAsync("/staff");
        await Expect(page).ToHaveURLAsync(EntraUrl);
        await Expect(page.Locator("input[name=loginfmt]").Or(page.GetByText("Pick an account"))).ToBeVisibleAsync();
    }
}
