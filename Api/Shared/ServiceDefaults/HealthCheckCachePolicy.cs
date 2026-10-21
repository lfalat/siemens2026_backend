using Microsoft.AspNetCore.OutputCaching;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Output-cache policy for the public <c>/health</c> endpoint (container-deployment.md §1.3). It differs from
/// the default policy in three ways, each of which would otherwise let a caller reach the dependency checks:
/// it stores <c>503 Unhealthy</c> as well as <c>200</c>, so an outage is cached too; it caches requests that
/// carry an <c>Authorization</c> header; and it keeps one entry regardless of the Host header.
/// </summary>
internal sealed class HealthCheckCachePolicy : IOutputCachePolicy
{
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(10);

    ValueTask IOutputCachePolicy.CacheRequestAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        context.EnableOutputCaching = true;
        context.AllowCacheLookup = true;
        context.AllowCacheStorage = true;
        // Concurrent misses wait for the one request already running the checks instead of each running them.
        context.AllowLocking = true;
        context.ResponseExpirationTimeSpan = Duration;
        context.CacheVaryByRules.VaryByHost = false;

        return ValueTask.CompletedTask;
    }

    ValueTask IOutputCachePolicy.ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    ValueTask IOutputCachePolicy.ServeResponseAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        // The default policy stores only 200 responses; this one keeps whatever status the checks produced.
        context.AllowCacheStorage = true;

        return ValueTask.CompletedTask;
    }
}
