using Kernel.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace ApiService.Infrastructure.Auth;

/// <summary>
/// Staff sign-in (ADR-004, iam.md §2): Entra access tokens validated locally as JWT bearer tokens, and the role
/// policies in <see cref="StaffPolicies"/>. Applicants never authenticate; their endpoints carry no policy.
/// </summary>
internal static class StaffAuthentication
{
    /// <summary>Registers the bearer scheme, the <see cref="EntraOptions"/> it validates against, and the staff policies.</summary>
    /// <param name="builder">The host builder.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static IHostApplicationBuilder AddStaffAuthentication(this IHostApplicationBuilder builder)
    {
        bool development = builder.Environment.IsDevelopment();
        builder.Services.AddOptions<EntraOptions>()
            .Bind(builder.Configuration.GetSection(EntraOptions.SectionName))
            .Validate(o => development || o.IsConfigured,
                $"{EntraOptions.SectionName}:TenantId and {EntraOptions.SectionName}:ApiClientId must be GUIDs outside Development.")
            .ValidateOnStart();

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Configured from the bound options rather than read once here, so tests and configuration overrides see
        // the same values the validation above checked.
        builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<EntraOptions>>(static (jwt, entraOptions) =>
            {
                // Keep claim names as Entra sends them (`oid`, `roles`, `scp`) instead of mapping them to long
                // schema URIs; StaffClaims and the policies below use the short names.
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters.NameClaimType = StaffClaims.Name;
                jwt.TokenValidationParameters.RoleClaimType = StaffClaims.Roles;

                // Unconfigured (Development only): no authority means no signing keys and no audience, so every
                // token fails validation — staff endpoints answer 401 and anonymous ones are unaffected.
                EntraOptions entra = entraOptions.Value;
                if (!entra.IsConfigured)
                    return;

                // Signing keys come from the tenant's metadata and are cached; Entra is not called per request.
                jwt.Authority = $"https://login.microsoftonline.com/{entra.TenantId}/v2.0";

                // The API registration decides the token version (requestedAccessTokenVersion in its manifest),
                // and the default is v1.0. Accept both forms, for this tenant and this API only.
                jwt.TokenValidationParameters.ValidIssuers =
                [
                    $"https://login.microsoftonline.com/{entra.TenantId}/v2.0",
                    $"https://sts.windows.net/{entra.TenantId}/",
                ];
                jwt.TokenValidationParameters.ValidAudiences = [entra.ApiClientId!, $"api://{entra.ApiClientId}"];
            });

        builder.Services.AddAuthorization();
        builder.Services.AddOptions<AuthorizationOptions>()
            .Configure<IOptions<EntraOptions>>(static (authorization, entraOptions) =>
            {
                string scope = entraOptions.Value.Scope;

                // What every staff policy shares: a token for this API, delegated by a signed-in user (`scp`, not
                // an app-only token), naming who they are (`oid`), with one of the staff roles.
                AuthorizationPolicy staff = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                    .RequireAuthenticatedUser()
                    .RequireClaim(StaffClaims.ObjectId)
                    .RequireAssertion(context => HasScope(context.User.FindFirst(StaffClaims.Scope)?.Value, scope))
                    .RequireRole(StaffRoles.All)
                    .Build();

                // `.RequireAuthorization()` without a name means any staff role.
                authorization.DefaultPolicy = staff;
                authorization.AddPolicy(StaffPolicies.Staff, staff);
                authorization.AddPolicy(StaffPolicies.Recruiter, policy => policy
                    .Combine(staff)
                    .RequireRole(StaffRoles.Recruiter, StaffRoles.Admin));
                authorization.AddPolicy(StaffPolicies.HiringManager, policy => policy
                    .Combine(staff)
                    .RequireRole(StaffRoles.HiringManager, StaffRoles.Admin));
                authorization.AddPolicy(StaffPolicies.Admin, policy => policy
                    .Combine(staff)
                    .RequireRole(StaffRoles.Admin));
            });

        return builder;
    }

    /// <summary>Whether the space-separated <c>scp</c> claim contains <paramref name="scope"/>.</summary>
    private static bool HasScope(string? scopes, string scope) =>
        scopes is not null
        && scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(scope, StringComparer.Ordinal);
}
