using System.ComponentModel.DataAnnotations;

namespace ApiService.Infrastructure;

/// <summary>
/// The per-caller request budget enforced by <see cref="RateLimitPolicies.PerCaller"/> (ADR-A007), bound from
/// the <c>RateLimiting</c> configuration section.
/// </summary>
/// <remarks>
/// <para>
/// The defaults are what runs when nothing is configured. They can be moved without a rebuild, which is what
/// a load test needs: measuring throughput against a fixed hundred requests per minute per caller measures the
/// limiter, not the API (ADR-018). A contract sweep hits the same wall from the other side, where a throttled
/// case passes every conformance check without an endpoint ever running (ADR-009).
/// </para>
/// <para>
/// <see cref="QueueLimit"/> deserves a second look before it is raised. A queued request is not rejected, it
/// waits — and on a fixed window it can wait out the whole of <see cref="Window"/> before being served, so a
/// caller over budget sees a request hang for up to a minute instead of a prompt <c>429</c> with a
/// <c>Retry-After</c>.
/// </para>
/// </remarks>
public sealed class RateLimitOptions
{
    /// <summary>The configuration section these are bound from.</summary>
    public const string SectionName = "RateLimiting";

    /// <summary>Requests one caller may make per <see cref="Window"/>. Defaults to 100.</summary>
    [Range(1, int.MaxValue)]
    public int PermitLimit { get; set; } = 100;

    /// <summary>The fixed window the budget is counted over. Defaults to one minute.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How many over-budget requests wait for the next window instead of being rejected. Defaults to 5;
    /// set to 0 to answer <c>429</c> immediately (see the remarks on this type).
    /// </summary>
    [Range(0, int.MaxValue)]
    public int QueueLimit { get; set; } = 5;
}
