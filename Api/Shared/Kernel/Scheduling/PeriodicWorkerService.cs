using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kernel.Scheduling;

/// <summary>
/// Runs a scoped <typeparamref name="TWorker"/> on a fixed interval — the periodic sibling of
/// <see cref="DailyWorkerService{TWorker}"/> for jobs that reconcile continuously rather than at a wall-clock
/// time (a janitor, a reaper). Each tick takes its own scope (a fresh pooled context). It runs once at startup
/// and then every <c>interval</c>; a run that overruns the interval simply delays the next tick rather than
/// overlapping.
/// </summary>
/// <remarks>
/// As with the daily host, the host logs only failures (generically, with the worker's type name); a worker
/// logs its own per-job detail from inside <see cref="IScheduledWorker.RunAsync"/>. Registered through
/// <c>AddPeriodicWorker</c>, which also sets the interval.
/// </remarks>
public sealed partial class PeriodicWorkerService<TWorker>(
    IServiceScopeFactory scopeFactory,
    ILogger<PeriodicWorkerService<TWorker>> logger,
    TimeSpan interval) : BackgroundService
    where TWorker : IScheduledWorker
{
    private readonly string _workerName = typeof(TWorker).Name;

    // See DailyWorkerService for why this is built once per host and pushed around RunAsync rather than
    // repeated as a parameter on every message the worker writes.
    private readonly KeyValuePair<string, object?>[] _workerScope = [new("Worker", typeof(TWorker).Name)];

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(interval);
        do
        {
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                TWorker worker = scope.ServiceProvider.GetRequiredService<TWorker>();
                using (logger.BeginScope(_workerScope))
                    await worker.RunAsync(stoppingToken); // the worker logs its own result
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogFailed(logger, _workerName, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(EventId = 1101, EventName = "scheduling.periodic_worker.failed", Level = LogLevel.Error,
        Message = "Scheduled worker {Worker} failed; will retry next interval.")]
    private static partial void LogFailed(ILogger logger, string worker, Exception exception);
}
