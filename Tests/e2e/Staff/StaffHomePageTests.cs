using System.Text.RegularExpressions;
using E2E.Infrastructure;

namespace E2E.Staff;

/// <summary>The staff area: Entra sign-in, MSAL, and the API accepting the token (<c>GET /api/v1/me</c>).</summary>
public class StaffHomePageTests : StaffTest
{
    [Theory]
    [InlineData(StaffRole.Recruiter)]
    [InlineData(StaffRole.HiringManager)]
    [InlineData(StaffRole.Admin)]
    public async Task Every_role_is_greeted_by_name(string role)
    {
        IPage page = await SignInAsAsync(role);

        await page.GotoAsync("/staff");

        // The greeting comes from /me, so it proves the whole chain, not just that the page rendered.
        await Expect(page.GetByRole(AriaRole.Heading, new() { NameRegex = new Regex("^Welcome, ") })).ToBeVisibleAsync();
        await Expect(page).ToHaveURLAsync(new Regex($"^{Regex.Escape(E2ESettings.BaseUrl)}/staff"));
    }
}
