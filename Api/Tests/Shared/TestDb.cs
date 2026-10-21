using System.Diagnostics.Metrics;
using Kernel.Idempotency;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Tests.Shared;

/// <summary>
/// Helpers for tests against a real Postgres (via <see cref="PostgresTestHost"/>): every context is backed by a fresh
/// database cloned from the real schema, so tests exercise exactly what the AppHost runs and don't bleed into each
/// other.
/// </summary>
internal static class TestDb
{
    /// <summary>A fresh idempotency context (the shared <c>kernel</c> schema).</summary>
    public static IdempotencyDbContext NewIdempotencyContext() =>
        new(Options<IdempotencyDbContext>().Options);

    /// <summary>An idempotency context on an already-created database — e.g. one reached through a proxy.</summary>
    /// <param name="connectionString">The connection string to use.</param>
    /// <param name="interceptors">Optional deterministic faults at the database command boundary.</param>
    public static IdempotencyDbContext NewIdempotencyContextOn(string connectionString, params IInterceptor[] interceptors) =>
        new(OptionsOn<IdempotencyDbContext>(connectionString).AddInterceptors(interceptors).Options);

    /// <summary>An <see cref="IdempotencyMetrics"/> backed by a throwaway <see cref="IMeterFactory"/>.</summary>
    public static IdempotencyMetrics NewIdempotencyMetrics() =>
        new(new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>());

    /// <summary>
    /// Opens a second context onto the <b>same</b> database as <paramref name="db"/> — another request's view, or a
    /// way to assert what actually committed rather than reading the change tracker back.
    /// </summary>
    public static TContext NewContextOnSameDb<TContext>(DbContext db) where TContext : DbContext
    {
        string connectionString = db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("Context has no connection string.");
        return (TContext)Activator.CreateInstance(typeof(TContext), OptionsOn<TContext>(connectionString).Options)!;
    }

    private static DbContextOptionsBuilder<TContext> Options<TContext>() where TContext : DbContext
    {
        // Before PostgresTestHost.Current, not after: reaching for the host starts the container, so a check passed
        // as an argument to OptionsOn would run too late to skip anything.
        PostgresTestHost.SkipIfSchemaUnavailable();
        return OptionsOn<TContext>(PostgresTestHost.Current.CreateDatabase());
    }

    /// <summary>
    /// Builds context options against <paramref name="connectionString"/> the way the host builds them — the
    /// retry-on-failure execution strategy included.
    /// </summary>
    /// <remarks>
    /// The strategy is not incidental. The store's raw-SQL writes go through <c>CreateExecutionStrategy()</c>
    /// precisely because of it; a test context configured without it runs that code down a path the host never
    /// takes, and cannot fail on the difference.
    /// </remarks>
    private static DbContextOptionsBuilder<TContext> OptionsOn<TContext>(string connectionString)
        where TContext : DbContext
    {
        PostgresTestHost.SkipIfSchemaUnavailable();

        DbContextOptionsBuilder<TContext> builder = new();
        builder.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure());
        return builder;
    }
}
