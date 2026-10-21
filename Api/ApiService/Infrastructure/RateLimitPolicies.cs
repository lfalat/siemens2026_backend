namespace ApiService.Infrastructure;

/// <summary>Names of the rate-limiting policies endpoints can require (ADR-A007).</summary>
/// <remarks>
/// Constants rather than literals because the name is written in two distant places — where the policy is
/// registered and where an endpoint group requires it — and a typo in the second one is not a startup error
/// in any obvious way.
/// </remarks>
internal static class RateLimitPolicies
{
    /// <summary>
    /// Per signed-in staff user, and per client IP for anonymous applicants. Applied to the versioned API group
    /// and to <c>/health</c>.
    /// </summary>
    public const string PerCaller = "per-caller";
}
