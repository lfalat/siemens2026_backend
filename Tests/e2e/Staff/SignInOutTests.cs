using System.Text.RegularExpressions;
using E2E.Infrastructure;

namespace E2E.Staff;

/// <summary>The "Staff sign in" and "Sign out" buttons, and that both reach Entra.</summary>
public class SignInOutTests : StaffTest
{
    private static readonly Regex EntraUrl = new(@"^https://login\.microsoftonline\.com/");
    private static readonly Regex HomeUrl = new($"^{Regex.Escape(E2ESettings.BaseUrl)}/?$");

    [Fact]
    public async Task Staff_sign_in_sends_a_visitor_to_Entra()
    {
        IPage page = await SignedOutPageAsync();
        await page.GotoAsync("/");

        await page.GetByRole(AriaRole.Link, new() { Name = "Staff sign in" }).ClickAsync();

        await Expect(page).ToHaveURLAsync(EntraUrl);
        await Expect(page.Locator("input[name=loginfmt]")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Sign_out_ends_the_session_in_the_app_and_in_Entra()
    {
        IPage page = await SignInSeparatelyAsAsync(StaffRole.Recruiter);

        await page.GetByRole(AriaRole.Button, new() { Name = "Sign out" }).ClickAsync();
        await FinishSignOutAsync(page, StaffRole.Recruiter);

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
