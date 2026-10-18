using System.Security.Claims;

namespace Kernel.Auth;

/// <summary>
/// The access-token claims the API relies on (iam.md §2.3), under their names in the token: the host turns off
/// inbound claim mapping, so <c>oid</c> stays <c>oid</c> rather than becoming a long schema URI.
/// </summary>
public static class StaffClaims
{
    /// <summary>The user's object id, stable across every app in the tenant. Identifies the user in the database.</summary>
    public const string ObjectId = "oid";

    /// <summary>The subject, stable for this app only. The rate limiter and idempotency keys partition by it.</summary>
    public const string Subject = "sub";

    /// <summary>The assigned app roles (<see cref="StaffRoles"/>).</summary>
    public const string Roles = "roles";

    /// <summary>The delegated scopes, space-separated.</summary>
    public const string Scope = "scp";

    /// <summary>The display name. For display only, never for authorization.</summary>
    public const string Name = "name";

    /// <summary>The sign-in name in v2.0 tokens. For display only, never for authorization.</summary>
    public const string PreferredUsername = "preferred_username";

    /// <summary>The sign-in name in v1.0 tokens. For display only, never for authorization.</summary>
    public const string UniqueName = "upn";

    /// <summary>
    /// The signed-in staff user's object id — the value to store as e.g. <c>created_by</c>. Every staff policy
    /// requires the claim, so on an endpoint behind one it is always present; a validated token already proves the
    /// user exists, so never re-query it.
    /// </summary>
    /// <param name="user">The endpoint's <see cref="ClaimsPrincipal"/>.</param>
    /// <exception cref="InvalidOperationException">The endpoint is not behind a staff policy.</exception>
    public static Guid GetStaffId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ObjectId), out Guid id)
            ? id
            : throw new InvalidOperationException(
                "The caller has no object id; GetStaffId is only valid on an endpoint behind a staff policy.");
}
