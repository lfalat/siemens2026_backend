using System.Net.Http.Json;
using System.Text.Json;
using Kernel.Auth;
using Kernel.CQRS;
using Kernel.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using RecruitmentModule.Features.StaffManagementFeatures.CreateUser;
using RecruitmentModule.Infrastructure.Entra;
using Tests.ApiService.Infrastructure.Auth;
using Tests.RecruitmentModule.Infrastructure.Entra;

namespace Tests.RecruitmentModule.Features.StaffManagementFeatures.CreateUser;

/// <summary><c>POST /staff-users</c>: administrators only.</summary>
public class CreateUserEndpointTests
{
    private static readonly object Body = new { displayName = "Jana Nováková", username = "jana.novakova" };

    private static async Task<WebApplication> StartHostAsync(FakeStaffDirectory directory, CancellationToken ct)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.AddTestStaffAuthentication();
        builder.Services.AddCqrsHandlers(typeof(CreateUserHandler).Assembly);
        builder.Services.AddSingleton<IStaffDirectory>(directory);

        WebApplication app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGroup("").MapEndpoints(typeof(CreateUserHandler).Assembly);

        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync(ct);
        return app;
    }

    [Fact]
    public async Task An_admin_creates_a_recruiter()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        FakeStaffDirectory directory = new();
        await using WebApplication app = await StartHostAsync(directory, ct);
        using HttpClient client = StaffTokens.Client(app, StaffTokens.Token(StaffTokens.Claims(StaffRoles.Admin)));

        using HttpResponseMessage response = await client.PostAsJsonAsync("/staff-users", Body, ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement created = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)).RootElement;
        Assert.Equal(directory.NextId.ToString(), created.GetProperty("id").GetString());
        Assert.Equal(StaffRoles.Recruiter, created.GetProperty("role").GetString());
        Assert.Equal(directory.Created.Single().Password, created.GetProperty("temporaryPassword").GetString());
    }

    [Theory]
    [InlineData(StaffRoles.Recruiter)]
    [InlineData(StaffRoles.HiringManager)]
    public async Task Any_other_role_is_403(string role)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        FakeStaffDirectory directory = new();
        await using WebApplication app = await StartHostAsync(directory, ct);
        using HttpClient client = StaffTokens.Client(app, StaffTokens.Token(StaffTokens.Claims(role)));

        using HttpResponseMessage response = await client.PostAsJsonAsync("/staff-users", Body, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(directory.Created);
    }

    [Fact]
    public async Task Without_a_token_is_401()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(new FakeStaffDirectory(), ct);
        using HttpClient client = StaffTokens.Client(app);

        using HttpResponseMessage response = await client.PostAsJsonAsync("/staff-users", Body, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
