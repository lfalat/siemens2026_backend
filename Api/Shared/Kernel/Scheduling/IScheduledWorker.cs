namespace Kernel.Scheduling;

/// <summary>
/// One pass of recurring background work, driven by <see cref="DailyWorkerService{TWorker}"/> or
/// <see cref="PeriodicWorkerService{TWorker}"/>. Implementations are registered scoped (a fresh pooled DbContext
/// per run via <c>AddDailyWorker</c> / <c>AddPeriodicWorker</c>) and stay pure workers:
/// no hosted-loop, scheduling, or cancellation plumbing of their own — the host owns that. A worker logs its
/// own success (the count it returns is what it would report); the host logs only failures.
/// </summary>
public interface IScheduledWorker
{
    /// <summary>Runs one pass; returns a count of work done (rows pruned, events published) for the worker's own logging.</summary>
    /// <param name="ct">Token to cancel the pass.</param>
    Task<int> RunAsync(CancellationToken ct = default);
}
