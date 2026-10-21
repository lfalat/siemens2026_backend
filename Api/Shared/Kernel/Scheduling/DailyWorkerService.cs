using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kernel.Scheduling;

/// <summary>
/// Runs a scoped <typeparamref name="TWorker"/> once a day at <c>runAt</c> (UTC), so daily background jobs don't
/// each copy the hosted-loop scaffolding: each run takes its own scope (a fresh pooled context), and the delay to
/// the next run is recomputed from the wall clock every iteration so the schedule stays anchored across restarts
/// and long runs rather than drifting from process start.
/// </summary>
/// <remarks>
/// The host logs only failures (generically, with the worker's type name); a worker logs its own success from
/// inside <see cref="IScheduledWorker.RunAsync"/>, so per-job messages and event ids stay specific. Registered
/// through <c>AddDailyWorker</c>, which also picks the run time — keeping the stagger between jobs at the one
/// composition root that owns it rather than buried in each worker.
/// </remarks>
public sealed partial class DailyWorkerService<TWorker>(
    IServiceScopeFactory scopeFactory,
    ILogger<DailyWorkerService<TWorker>> logger,
    TimeOnly runAt) : BackgroundService
    where TWorker : IScheduledWorker
{
    private readonly string _workerName = typeof(TWorker).Name;

    // Built once per host: AddDailyWorker registers exactly one singleton per worker type. Pushed around RunAsync
    // so that records this worker does not write — EF Core's, a client library's — are attributable to the job
    // that caused them. Outside a request there is no trace to join on, which is where a scope earns its cost.
    private readonly KeyValuePair<string, object?>[] _workerScope = [new("Worker", typeof(TWorker).Name)];

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(DailyWorkerSchedule.DelayUntilNextRun(DateTime.UtcNow, runAt), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

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
    }

    [LoggerMessage(EventId = 1100, EventName = "scheduling.daily_worker.failed", Level = LogLevel.Error,
        Message = "Scheduled worker {Worker} failed; will retry next run.")]
    private static partial void LogFailed(ILogger logger, string worker, Exception exception);
}

/// <summary>
/// The daily-run scheduling arithmetic, factored out of the generic host so it can be unit-tested without a
/// type argument or the hosted loop.
/// </summary>
internal static class DailyWorkerSchedule
{
    /// <summary>
    /// The wait until the next <paramref name="runAt"/> (UTC): later today if that time hasn't passed,
    /// otherwise tomorrow.
    /// </summary>
    /// <param name="utcNow">The current UTC time.</param>
    /// <param name="runAt">The daily wall-clock time (UTC) to run at.</param>
    internal static TimeSpan DelayUntilNextRun(DateTime utcNow, TimeOnly runAt)
    {
        DateTime todayRun = utcNow.Date + runAt.ToTimeSpan();
        DateTime nextRun = utcNow < todayRun ? todayRun : todayRun.AddDays(1);
        return nextRun - utcNow;
    }
}
