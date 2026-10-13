using Kernel.Scheduling;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kernel.Idempotency;

/// <summary>
/// Host wiring for the idempotency feature: service registration and pipeline placement. Call
/// <see cref="AddIdempotency"/> when configuring services and <see cref="UseIdempotency"/> in the request
/// pipeline (after authentication, so keys are scoped to the resolved user, and before endpoint execution).
/// </summary>
public static class IdempotencyRegistration
{
    /// <summary>
    /// Registers the options, the metrics, and the in-memory store. Endpoints are covered automatically
    /// once <see cref="UseIdempotency"/> is in the pipeline — no per-endpoint opt-in. Add
    /// <see cref="AddDbIdempotencyStore"/> on top to make claims survive a restart and be shared between
    /// instances.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="configure">Optional overrides (default TTL, header names, key requirement, lease, memory budget).</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// The in-memory store gets a <c>MemoryCache</c> of its own rather than the shared one. The size budget
    /// has to bound this feature alone, and a <c>SizeLimit</c> another component set on the shared cache
    /// would make every write here throw.
    /// </remarks>
    public static IServiceCollection AddIdempotency(this IServiceCollection services, Action<IdempotencyOptions>? configure = null)
    {
        OptionsBuilder<IdempotencyOptions> optionsBuilder = services.AddOptions<IdempotencyOptions>();
        if (configure is not null)
            optionsBuilder.Configure(configure);
        optionsBuilder.Validate(o => o.WriteBackTimeout > TimeSpan.Zero &&
            o.WriteBackTimeout.TotalMilliseconds <= uint.MaxValue - 1,
            "WriteBackTimeout must be a positive, finite cancellation timeout.");
        // Write-back runs inside the lease. A lease no longer than its own write-back budget expires while
        // a completion is still retrying, and a duplicate takes the key over before the response is stored.
        optionsBuilder.Validate(o => o.ClaimLease > o.WriteBackTimeout,
            "ClaimLease must exceed WriteBackTimeout.");
        optionsBuilder.ValidateOnStart();

        services.TryAddSingleton<IdempotencyMetrics>();
        services.TryAddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();

        return services;
    }

    /// <summary>
    /// Replaces the in-memory store with the PostgreSQL-backed one and registers its context and retention
    /// job. Call after <see cref="AddIdempotency"/>.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="configureDb">
    /// Configures the context's provider and connection — the host supplies it, so the Kernel does not take
    /// a dependency on a database provider. The store's SQL is PostgreSQL. It receives the service provider
    /// so the host can hand over its registered data source rather than a bare connection string.
    /// </param>
    /// <param name="pruneAt">When the daily retention pass runs.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// This is what makes a claim outlive the process that took it. Without it a deployment or a crash
    /// forgets every claim, and the retry that follows re-runs the write — a second application or a second
    /// job offer, not a duplicate read.
    /// </remarks>
    public static IServiceCollection AddDbIdempotencyStore(
        this IServiceCollection services, Action<IServiceProvider, DbContextOptionsBuilder> configureDb, TimeOnly pruneAt)
    {
        services.AddDbContextPool<IdempotencyDbContext>(configureDb);

        // Scoped, so the middleware must resolve it per request rather than capture it in its constructor.
        services.RemoveAll<IIdempotencyStore>();
        services.AddScoped<IIdempotencyStore, DbIdempotencyStore>();

        services.AddDailyWorker<IdempotencyPruner>(pruneAt);

        return services;
    }

    /// <summary>
    /// Adds the <see cref="IdempotencyMiddleware"/> to the pipeline. Place after
    /// <c>UseAuthentication</c>/<c>UseAuthorization</c> and before the endpoints are executed.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The same <paramref name="app"/> for chaining.</returns>
    public static IApplicationBuilder UseIdempotency(this IApplicationBuilder app) =>
        app.UseMiddleware<IdempotencyMiddleware>();
}
