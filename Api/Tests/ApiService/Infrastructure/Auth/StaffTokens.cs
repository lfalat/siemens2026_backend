using System.Net.Http.Headers;
using System.Security.Cryptography;
using ApiService.Infrastructure.Auth;
using Kernel.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Tests.ApiService.Infrastructure.Auth;

/// <summary>
/// Tokens shaped like Entra's, and a host that accepts them through the real <c>StaffAuthentication</c> registration.
/// The tokens are signed with a key of the tests' own, put in place of the tenant's published keys, so everything
/// after the signature check — issuer, audience, lifetime, scope, roles — is the production path.
/// </summary>
internal static class StaffTokens
{
    public static readonly string TenantId = Guid.NewGuid().ToString();
    public static readonly string ApiClientId = Guid.NewGuid().ToString();
    public static readonly string V2Issuer = $"https://login.microsoftonline.com/{TenantId}/v2.0";
    public static readonly string V1Issuer = $"https://sts.windows.net/{TenantId}/";

    /// <summary>The <c>oid</c> every token from <see cref="Claims"/> carries.</summary>
    public const string ObjectId = "6f1c1f0e-2d0a-4a43-9a3b-1b8f8b7a2f10";

    private static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = "test-key" };

    /// <summary>
    /// Registers staff authentication on <paramref name="builder"/> as <c>Program.cs</c> does, configured for the test
    /// tenant unless <paramref name="configured"/> is <c>false</c>, and trusting the test signing key.
    /// </summary>
    public static WebApplicationBuilder AddTestStaffAuthentication(this WebApplicationBuilder builder, bool configured = true)
    {
        if (configured)
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Entra:TenantId"] = TenantId,
                ["Entra:ApiClientId"] = ApiClientId,
            });
        }

        builder.AddStaffAuthentication();

        // The tenant's published keys are replaced by the test's own; nothing else about validation changes.
        builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, jwt =>
        {
            jwt.ConfigurationManager = null;
            jwt.TokenValidationParameters.IssuerSigningKey = SigningKey;
        });

        return builder;
    }

    /// <summary>The claims of a delegated v2.0 access token for this API; tests remove or change what they probe.</summary>
    public static Dictionary<string, object> Claims(params string[] roles) => new()
    {
        [StaffClaims.ObjectId] = ObjectId,
        [StaffClaims.Subject] = "subject-1",
        [StaffClaims.Scope] = "access_as_user",
        [StaffClaims.Name] = "Jana Nováková",
        [StaffClaims.PreferredUsername] = "jana@example.com",
        ["tid"] = TenantId,
        [StaffClaims.Roles] = roles,
    };

    /// <summary>A signed token carrying <paramref name="claims"/>; by default valid, for this tenant and this API.</summary>
    public static string Token(
        Dictionary<string, object> claims, string? issuer = null, string? audience = null,
        DateTime? expires = null, SecurityKey? key = null)
    {
        DateTime notAfter = expires ?? DateTime.UtcNow.AddHours(1);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer ?? V2Issuer,
            Audience = audience ?? ApiClientId,
            Claims = claims,
            NotBefore = notAfter.AddHours(-2),
            IssuedAt = notAfter.AddHours(-2),
            Expires = notAfter,
            SigningCredentials = new SigningCredentials(key ?? SigningKey, SecurityAlgorithms.RsaSha256),
        });
    }

    /// <summary>A client for <paramref name="app"/> that sends <paramref name="token"/> as its bearer token, if given.</summary>
    public static HttpClient Client(WebApplication app, string? token = null)
    {
        HttpClient client = new() { BaseAddress = new Uri(app.Urls.First()) };
        if (token is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
