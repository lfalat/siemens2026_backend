using Kernel.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kernel.Idempotency;

/// <summary>
/// Deletes expired idempotency records. Nothing else removes them — a record's TTL only stops it being
/// replayed, it does not delete the row — so without this the table grows with every covered write the
/// system has ever served.
/// </summary>
/// <param name="db">The idempotency context (a fresh pooled instance per run).</param>
/// <param name="metrics">Counters, so the deleted volume is visible.</param>
/// <param name="logger">Logger for the pruned-row count.</param>
/// <remarks>
/// The table is churn-heavy: rows are inserted, updated once and deleted within the TTL, at the rate of
/// covered writes. That pattern produces dead tuples faster than row count suggests, so the table wants
/// more aggressive autovacuum settings than the default — see ADR-D001 in <c>docs/db/decision-log.md</c>.
/// </remarks>
public sealed partial class IdempotencyPruner(
    IdempotencyDbContext db,
    IdempotencyMetrics metrics,
    ILogger<IdempotencyPruner> logger) : IScheduledWorker
{
    /// <summary>
    /// Deletes records whose replay window has closed, preserving unfinished claims with live leases. Backed by
    /// <c>idx_idempotency_expires_at</c>, so it is a cheap range delete.
    /// </summary>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <remarks>
    /// The cutoff is the database's <c>now()</c>, not this process's clock — the store decides what is
    /// still live the same way, and a job on a host whose clock has drifted forward would otherwise delete
    /// records the store would still have replayed.
    /// </remarks>
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        int removed = await db.Database.ExecuteSqlAsync(
            $"DELETE FROM kernel.idempotency WHERE expires_at <= now() AND (completed = true OR lease_expires_at <= now())", ct);

        if (removed > 0)
        {
            metrics.RecordPruned(removed);
            LogPruned(logger, removed);
        }

        return removed;
    }

    [LoggerMessage(EventId = 3210, EventName = "idempotency.record.pruned", Level = LogLevel.Information,
        Message = "Pruned {Count} expired idempotency record(s).")]
    private static partial void LogPruned(ILogger logger, int count);
}
