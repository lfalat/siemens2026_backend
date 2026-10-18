using E2E.Infrastructure;

namespace E2E.Applicants;

/// <summary>The public site, as an applicant sees it: no sign-in.</summary>
public class HomePageTests : E2EPageTest
{
    [Fact]
    public async Task Applicants_see_the_home_page_without_signing_in()
    {
        await Page.GotoAsync("/");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Hello, HealthyHires!" })).ToBeVisibleAsync();
    }
}
