using System.Security.Cryptography;
using Kernel.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using static Tests.ApiService.Infrastructure.Auth.StaffTokens;

namespace Tests.ApiService.Infrastructure.Auth;

/// <summary>
/// Staff sign-in as <c>StaffAuthentication</c> registers it: real bearer validation of tokens shaped like Entra's
/// (<see cref="StaffTokens"/>), and the role policies.
/// </summary>
public class StaffAuthenticationTests
{
    /// <summary>A host with the real registration and one endpoint per policy, plus an anonymous one.</summary>
    private static async Task<WebApplication> StartHostAsync(
        CancellationToken ct, bool configured = true, string environment = "Development")
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(
            new WebApplicationOptions { EnvironmentName = environment });
        builder.Logging.ClearProviders();
        builder.AddTestStaffAuthentication(configured);

        WebApplication app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/public", static () => Results.Ok());
        app.MapGroup("/staff").RequireStaff().MapGet("", static () => Results.Ok());
        app.MapGroup("/recruiter").RequireStaff(StaffPolicies.Recruiter).MapGet("", static () => Results.Ok());
        app.MapGroup("/hiring-manager").RequireStaff(StaffPolicies.HiringManager).MapGet("", static () => Results.Ok());
        app.MapGroup("/admin").RequireStaff(StaffPolicies.Admin).MapGet("", static () => Results.Ok());
        // A stricter endpoint inside a staff group: both policies apply.
        app.MapGroup("/mixed").RequireStaff().MapGet("", static () => Results.Ok())
            .RequireAuthorization(StaffPolicies.Admin);

        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync(ct);
        return app;
    }

    private static async Task<HttpStatusCode> GetAsync(WebApplication app, string path, string? token, CancellationToken ct)
    {
        using HttpClient client = Client(app, token);
        using HttpResponseMessage response = await client.GetAsync(path, ct);
        return response.StatusCode;
    }

    [Theory]
    [InlineData(StaffRoles.Recruiter, "/staff", HttpStatusCode.OK)]
    [InlineData(StaffRoles.Recruiter, "/recruiter", HttpStatusCode.OK)]
    [InlineData(StaffRoles.Recruiter, "/hiring-manager", HttpStatusCode.Forbidden)]
    [InlineData(StaffRoles.Recruiter, "/admin", HttpStatusCode.Forbidden)]
    [InlineData(StaffRoles.Recruiter, "/mixed", HttpStatusCode.Forbidden)]
    [InlineData(StaffRoles.HiringManager, "/staff", HttpStatusCode.OK)]
    [InlineData(StaffRoles.HiringManager, "/recruiter", HttpStatusCode.Forbidden)]
    [InlineData(StaffRoles.HiringManager, "/hiring-manager", HttpStatusCode.OK)]
    [InlineData(StaffRoles.HiringManager, "/admin", HttpStatusCode.Forbidden)]
    [InlineData(StaffRoles.HiringManager, "/mixed", HttpStatusCode.Forbidden)]
    [InlineData(StaffRoles.Admin, "/staff", HttpStatusCode.OK)]
    [InlineData(StaffRoles.Admin, "/recruiter", HttpStatusCode.OK)]
    [InlineData(StaffRoles.Admin, "/hiring-manager", HttpStatusCode.OK)]
    [InlineData(StaffRoles.Admin, "/admin", HttpStatusCode.OK)]
    [InlineData(StaffRoles.Admin, "/mixed", HttpStatusCode.OK)]
    public async Task Each_role_reaches_its_own_policy_and_admin_reaches_all(string role, string path, HttpStatusCode expected)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);

        Assert.Equal(expected, await GetAsync(app, path, Token(Claims(role)), ct));
    }

    [Fact]
    public async Task Without_a_token_staff_endpoints_answer_401_and_public_ones_run()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);

        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(app, "/staff", null, ct));
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(app, "/admin", null, ct));
        Assert.Equal(HttpStatusCode.OK, await GetAsync(app, "/public", null, ct));
    }

    [Fact]
    public async Task A_v1_token_for_the_app_id_uri_is_accepted()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);

        string token = Token(Claims(StaffRoles.Recruiter), issuer: V1Issuer, audience: $"api://{ApiClientId}");

        Assert.Equal(HttpStatusCode.OK, await GetAsync(app, "/staff", token, ct));
    }

    public static TheoryData<string, Func<string>> InvalidTokens => new()
    {
        { "another tenant", () => Token(Claims(StaffRoles.Admin), issuer: $"https://login.microsoftonline.com/{Guid.NewGuid()}/v2.0") },
        { "another API", () => Token(Claims(StaffRoles.Admin), audience: Guid.NewGuid().ToString()) },
        { "expired", () => Token(Claims(StaffRoles.Admin), expires: DateTime.UtcNow.AddMinutes(-10)) },
        { "foreign key", () => Token(Claims(StaffRoles.Admin), key: new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-key" }) },
    };

    [Theory]
    [MemberData(nameof(InvalidTokens))]
    public async Task A_token_that_fails_validation_is_401(string because, Func<string> token)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);

        Assert.True(HttpStatusCode.Unauthorized == await GetAsync(app, "/staff", token(), ct), because);
    }

    public static TheoryData<string, Func<Dictionary<string, object>>> NotStaffTokens => new()
    {
        { "no role", () => Claims() },
        { "unknown role", () => Claims("Applicant") },
        { "no scope (app-only token)", () => Without(Claims(StaffRoles.Admin), StaffClaims.Scope) },
        { "another scope", () => With(Claims(StaffRoles.Admin), StaffClaims.Scope, "user_impersonation") },
        { "no object id", () => Without(Claims(StaffRoles.Admin), StaffClaims.ObjectId) },
    };

    [Theory]
    [MemberData(nameof(NotStaffTokens))]
    public async Task A_valid_token_that_is_not_staff_is_403(string because, Func<Dictionary<string, object>> claims)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);

        Assert.True(HttpStatusCode.Forbidden == await GetAsync(app, "/staff", Token(claims()), ct), because);
    }

    [Fact]
    public async Task The_scope_is_found_among_several()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);

        string token = Token(With(Claims(StaffRoles.Recruiter), StaffClaims.Scope, "openid access_as_user profile"));

        Assert.Equal(HttpStatusCode.OK, await GetAsync(app, "/staff", token, ct));
    }

    [Fact]
    public async Task Unconfigured_in_Development_the_API_runs_and_rejects_every_token()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct, configured: false);

        Assert.Equal(HttpStatusCode.OK, await GetAsync(app, "/public", null, ct));
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(app, "/staff", Token(Claims(StaffRoles.Admin)), ct));
    }

    [Fact]
    public async Task Unconfigured_outside_Development_startup_fails()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<OptionsValidationException>(
            () => StartHostAsync(ct, configured: false, environment: "Production"));
    }

    private static Dictionary<string, object> Without(Dictionary<string, object> claims, string name)
    {
        claims.Remove(name);
        return claims;
    }

    private static Dictionary<string, object> With(Dictionary<string, object> claims, string name, object value)
    {
        claims[name] = value;
        return claims;
    }
}
