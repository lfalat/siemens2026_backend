namespace ApiService.Infrastructure.Auth;

/// <summary>
/// The company Entra tenant and the HealthyHires API app registration that staff access tokens are validated
/// against (iam.md §2), bound from the <c>Entra</c> configuration section. None of these is a secret: the API
/// validates tokens with Entra's public signing keys and has no client secret.
/// </summary>
/// <remarks>
/// Required outside Development, where startup fails without them. In Development they may be left empty, as in
/// CI: the API then runs, anonymous endpoints work, and every staff endpoint answers <c>401</c>.
/// </remarks>
public sealed class EntraOptions
{
    /// <summary>The configuration section these are bound from.</summary>
    public const string SectionName = "Entra";

    /// <summary>The company tenant's id. Tokens issued by any other tenant are rejected.</summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// The Application (client) id of the HealthyHires <b>API</b> registration — not the SPA's. Tokens must be
    /// issued for it: <c>aud</c> is this id in a v2.0 token and <c>api://</c> + this id in a v1.0 token.
    /// </summary>
    public string? ApiClientId { get; set; }

    /// <summary>The delegated scope every staff token must carry. Defaults to <c>access_as_user</c>.</summary>
    public string Scope { get; set; } = "access_as_user";

    /// <summary>Whether both ids are set and well-formed.</summary>
    public bool IsConfigured => Guid.TryParse(TenantId, out _) && Guid.TryParse(ApiClientId, out _);
}
