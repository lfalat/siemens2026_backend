using Microsoft.EntityFrameworkCore;

namespace Kernel.Idempotency;

/// <summary>
/// PostgreSQL-backed <see cref="IIdempotencyStore"/>. Claims survive a restart and are shared by every
/// instance, which is what the in-memory store cannot do: there, a deployment or a crash forgets every
/// claim, so the retry that follows re-executes the write it was meant to deduplicate.
/// </summary>
/// <param name="db">The idempotency context, resolved per request.</param>
/// <remarks>
/// Every expiry comparison uses the database's <c>now()</c> rather than the process clock, so instances
/// with drifting clocks still agree on whether a claim is live.
/// </remarks>
internal sealed class DbIdempotencyStore(IdempotencyDbContext db) : IIdempotencyStore
{
    /// <summary>
    /// Identifies this request's claims. The store is scoped, so one instance serves one request and the
    /// value is stable for everything that request does — including a command the context retries after a
    /// transient failure, which is the case it exists for.
    /// </summary>
    private readonly Guid _owner = Guid.CreateVersion7();

    /// <inheritdoc />
    public async Task<IdempotencyBeginResult> BeginAsync(
        string key, byte[] fingerprint, TimeSpan ttl, TimeSpan lease, CancellationToken ct = default)
    {
        // One statement claims the key three ways: a first insert, a takeover of a record that has aged
        // out, and a takeover of an unfinished claim whose lease expired. PostgreSQL 18's old row
        // identifies a real lease takeover without counting normal completed-response expiry.
        //
        // The fingerprint guard on the stranded-claim branch keeps a mismatch detectable: a different
        // payload under the same key must still be answered 422, not silently granted the abandoned claim.
        List<bool> claimed = await db.Database.SqlQuery<bool>(
            $"""
             INSERT INTO kernel.idempotency (key, fingerprint, claim_owner, completed, lease_expires_at, expires_at)
             VALUES ({key}, {fingerprint}, {_owner}, false, now() + {lease}, now() + {ttl})
             ON CONFLICT (key) DO UPDATE
                SET fingerprint      = EXCLUDED.fingerprint,
                    claim_owner      = EXCLUDED.claim_owner,
                    completed        = false,
                    lease_expires_at = EXCLUDED.lease_expires_at,
                    expires_at       = EXCLUDED.expires_at,
                    status_code      = NULL,
                    content_type     = NULL,
                    location         = NULL,
                    body             = NULL
              WHERE (kernel.idempotency.completed = true AND kernel.idempotency.expires_at <= now())
                 OR (kernel.idempotency.completed = false
                     AND kernel.idempotency.lease_expires_at <= now()
                     AND (kernel.idempotency.fingerprint = EXCLUDED.fingerprint
                          OR kernel.idempotency.expires_at <= now()))
             RETURNING COALESCE(NOT old.completed, false) AS "Value"
             """).ToListAsync(ct);

        if (claimed.Count > 0)
            return new IdempotencyBeginResult(IdempotencyBeginStatus.Started, null, Reclaimed: claimed[0]);

        // Someone else holds a live claim, or a completed response is waiting.
        IdempotencyEntry? existing = await db.Entries.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Key == key, ct);
        if (existing is null)
            return new IdempotencyBeginResult(IdempotencyBeginStatus.InProgress, null); // pruned between the two statements

        if (!existing.Fingerprint.AsSpan().SequenceEqual(fingerprint))
            return new IdempotencyBeginResult(IdempotencyBeginStatus.Mismatch, null);

        // Our own claim, seen a second time: the INSERT above committed but its acknowledgement was lost
        // and the context retried the command. Answering InProgress here would report a conflict against
        // the very request that holds the claim — a 409 caused by nothing but a dropped packet.
        if (!existing.Completed && existing.ClaimOwner == _owner)
            return new IdempotencyBeginResult(IdempotencyBeginStatus.Started, null);

        return existing is { Completed: true, StatusCode: { } status }
            ? new IdempotencyBeginResult(
                IdempotencyBeginStatus.Completed,
                new IdempotencyRecord(status, existing.ContentType, existing.Location, existing.Body ?? []))
            : new IdempotencyBeginResult(IdempotencyBeginStatus.InProgress, null);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Writes only to a claim this request still holds. A slow request whose lease ran out has already been
    /// taken over, and its response is the losing one: storing it would hand later retries the answer of the
    /// attempt that lost the key, while the winner's completion silently did nothing.
    /// </remarks>
    public async Task CompleteAsync(string key, IdempotencyRecord record, CancellationToken ct = default)
    {
        // No rows means the claim was pruned, expired, or taken over meanwhile — nothing left to complete,
        // and a later identical request re-executes.
        // Both writes are owner-guarded and safe to retry even if the acknowledgement was lost.
        // Raw ExecuteSqlAsync does not use the configured execution strategy on its own.
        await db.Database.CreateExecutionStrategy().ExecuteAsync(token => db.Database.ExecuteSqlAsync(
            $"""
             UPDATE kernel.idempotency
                SET completed    = true,
                    status_code  = {record.StatusCode},
                    content_type = {record.ContentType},
                    location     = {record.Location},
                    body         = {record.Body}
              WHERE key = {key} AND completed = false AND claim_owner = {_owner}
             """, token), ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Releases only a claim this request still holds. Without the owner check a request whose lease had
    /// expired would delete the claim of whoever took it over, and the key would be open for a third request
    /// to start alongside the second — losing the exclusion the store exists to provide.
    /// </remarks>
    public async Task AbandonAsync(string key, CancellationToken ct = default)
    {
        // Only an uncompleted claim of our own is ours to release; a completed record belongs to whoever
        // finished it, and a live claim to whoever holds it now.
        await db.Database.CreateExecutionStrategy().ExecuteAsync(token => db.Database.ExecuteSqlAsync(
            $"DELETE FROM kernel.idempotency WHERE key = {key} AND completed = false AND claim_owner = {_owner}",
            token), ct);
    }
}
