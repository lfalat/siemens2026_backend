using System.Text.Json;
using ApiService.Endpoints;
using Kernel.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Tests.ApiService.Infrastructure.Auth;

namespace Tests.ApiService.Endpoints;

/// <summary><c>GET /me</c>: the caller as the API sees them, behind the staff policy.</summary>
public class MeEndpointTests
{
    private static async Task<WebApplication> StartHostAsync(CancellationToken ct)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.AddTestStaffAuthentication();

        WebApplication app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGroup("").MapMe();

        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync(ct);
        return app;
    }

    [Fact]
    public async Task Returns_the_callers_identity_and_roles()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient client = StaffTokens.Client(
            app, StaffTokens.Token(StaffTokens.Claims(StaffRoles.HiringManager)));

        JsonElement me = JsonDocument.Parse(await client.GetStringAsync("/me", ct)).RootElement;

        Assert.Equal(StaffTokens.ObjectId, me.GetProperty("id").GetString());
        Assert.Equal("Jana Nováková", me.GetProperty("name").GetString());
        Assert.Equal("jana@example.com", me.GetProperty("username").GetString());
        Assert.Equal([StaffRoles.HiringManager], me.GetProperty("roles").EnumerateArray().Select(static r => r.GetString()));
    }

    [Fact]
    public async Task Without_a_token_is_401()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient client = StaffTokens.Client(app);

        using HttpResponseMessage response = await client.GetAsync("/me", ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
