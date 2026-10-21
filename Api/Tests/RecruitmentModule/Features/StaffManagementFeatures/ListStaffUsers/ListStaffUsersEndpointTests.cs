using System.Text.Json;
using Kernel.Auth;
using Kernel.CQRS;
using Kernel.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using RecruitmentModule.Features.StaffManagementFeatures.ListStaffUsers;
using RecruitmentModule.Infrastructure.Entra;
using Tests.ApiService.Infrastructure.Auth;
using Tests.RecruitmentModule.Infrastructure.Entra;

namespace Tests.RecruitmentModule.Features.StaffManagementFeatures.ListStaffUsers;

/// <summary><c>GET /staff-users</c>: any staff member, optionally filtered by one known role.</summary>
public class ListStaffUsersEndpointTests
{
    private static readonly StaffMember Recruiter = new(Guid.NewGuid(), "Jana Nováková", [StaffRoles.Recruiter]);
    private static readonly StaffMember Manager = new(Guid.NewGuid(), "Peter Horváth", [StaffRoles.HiringManager]);

    private static async Task<WebApplication> StartHostAsync(CancellationToken ct)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.AddTestStaffAuthentication();
        builder.Services.AddValidation();
        builder.Services.AddCqrsHandlers(typeof(ListStaffUsersHandler).Assembly);
        builder.Services.AddSingleton<IStaffDirectory>(new FakeStaffDirectory { Staff = { Recruiter, Manager } });

        WebApplication app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGroup("").MapEndpoints(typeof(ListStaffUsersHandler).Assembly);

        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync(ct);
        return app;
    }

    private static async Task<JsonElement> UsersAsync(HttpResponseMessage response, CancellationToken ct) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("users");

    [Theory]
    [InlineData(StaffRoles.Recruiter)]
    [InlineData(StaffRoles.HiringManager)]
    [InlineData(StaffRoles.Admin)]
    public async Task Any_staff_member_lists_the_recruiters(string role)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient client = StaffTokens.Client(app, StaffTokens.Token(StaffTokens.Claims(role)));

        using HttpResponseMessage response = await client.GetAsync("/staff-users?role=Recruiter", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Recruiter.Id, (await UsersAsync(response, ct)).EnumerateArray().Single().GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Without_a_role_lists_everyone()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient client = StaffTokens.Client(app, StaffTokens.Token(StaffTokens.Claims(StaffRoles.HiringManager)));

        using HttpResponseMessage response = await client.GetAsync("/staff-users", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, (await UsersAsync(response, ct)).GetArrayLength());
    }

    [Fact]
    public async Task An_unknown_role_is_400()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient client = StaffTokens.Client(app, StaffTokens.Token(StaffTokens.Claims(StaffRoles.HiringManager)));

        using HttpResponseMessage response = await client.GetAsync("/staff-users?role=Applicant", ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Without_a_token_is_401()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient client = StaffTokens.Client(app);

        using HttpResponseMessage response = await client.GetAsync("/staff-users", ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
