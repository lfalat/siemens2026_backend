using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kernel.Scheduling;

/// <summary>Composition-root helpers for registering scheduled background workers.</summary>
public static class SchedulingRegistration
{
    /// <summary>
    /// Registers <typeparamref name="TWorker"/> (scoped, so each run gets a fresh pooled context and tests can
    /// resolve it directly) plus a <see cref="DailyWorkerService{TWorker}"/> that runs it once a day at
    /// <paramref name="runAt"/> (UTC). Choosing the time here keeps the stagger between jobs in one place.
    /// </summary>
    /// <typeparam name="TWorker">The scheduled worker to run.</typeparam>
    /// <param name="services">The application service collection.</param>
    /// <param name="runAt">The daily wall-clock time (UTC) to run at.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddDailyWorker<TWorker>(this IServiceCollection services, TimeOnly runAt)
        where TWorker : class, IScheduledWorker
    {
        services.AddScoped<TWorker>();
        services.AddSingleton<IHostedService>(sp => new DailyWorkerService<TWorker>(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ILogger<DailyWorkerService<TWorker>>>(),
            runAt));
        return services;
    }

    /// <summary>
    /// Registers <typeparamref name="TWorker"/> (scoped, so each tick gets a fresh pooled context and tests can
    /// resolve it directly) plus a <see cref="PeriodicWorkerService{TWorker}"/> that runs it once at startup and
    /// then every <paramref name="interval"/>. For continuously-reconciling jobs (a janitor, a reaper) rather
    /// than ones anchored to a wall-clock time.
    /// </summary>
    /// <typeparam name="TWorker">The scheduled worker to run.</typeparam>
    /// <param name="services">The application service collection.</param>
    /// <param name="interval">How often to run.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddPeriodicWorker<TWorker>(this IServiceCollection services, TimeSpan interval)
        where TWorker : class, IScheduledWorker
    {
        services.AddScoped<TWorker>();
        services.AddSingleton<IHostedService>(sp => new PeriodicWorkerService<TWorker>(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ILogger<PeriodicWorkerService<TWorker>>>(),
            interval));
        return services;
    }
}
