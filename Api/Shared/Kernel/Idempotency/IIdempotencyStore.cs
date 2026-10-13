namespace Kernel.Idempotency;

/// <summary>The captured, replayable result of a completed idempotent request.</summary>
/// <param name="StatusCode">The HTTP status code the endpoint produced.</param>
/// <param name="ContentType">The response <c>Content-Type</c>, if any.</param>
/// <param name="Location">The response <c>Location</c> header (e.g. for a <c>201 Created</c>), if any.</param>
/// <param name="Body">The exact response body bytes to replay.</param>
public sealed record IdempotencyRecord(int StatusCode, string? ContentType, string? Location, byte[] Body);

/// <summary>The outcome of claiming an idempotency key for the current request.</summary>
public enum IdempotencyBeginStatus
{
    /// <summary>
    /// No live claim on this key: the caller owns execution and must later
    /// <see cref="IIdempotencyStore.CompleteAsync"/> or <see cref="IIdempotencyStore.AbandonAsync"/> it.
    /// Also returned when a previous claim's lease expired without either — see
    /// <see cref="IdempotencyOptions.ClaimLease"/>.
    /// </summary>
    Started,

    /// <summary>Another request under this key is still executing: the caller should reject with <c>409 Conflict</c>.</summary>
    InProgress,

    /// <summary>A completed response exists under this key and matches the payload: replay <see cref="IdempotencyBeginResult.Record"/>.</summary>
    Completed,

    /// <summary>The key was already used with a different request payload: the caller should reject with <c>422 Unprocessable Entity</c>.</summary>
    Mismatch,
}

/// <summary>The result of <see cref="IIdempotencyStore.BeginAsync"/>.</summary>
/// <param name="Status">What the store found for the key.</param>
/// <param name="Record">The response to replay when <paramref name="Status"/> is <see cref="IdempotencyBeginStatus.Completed"/>; otherwise <c>null</c>.</param>
/// <param name="Reclaimed">
/// Whether this <see cref="IdempotencyBeginStatus.Started"/> took over a stranded claim rather than being
/// the key's first sight or normal completed-response expiry. Causes include a crashed request, failed
/// write-back, or work outliving its lease.
/// </param>
public readonly record struct IdempotencyBeginResult(
    IdempotencyBeginStatus Status, IdempotencyRecord? Record, bool Reclaimed = false);

/// <summary>
/// Stores in-flight and completed idempotent requests keyed by a client-supplied key (scoped per user and
/// endpoint by the middleware). Implementations must be safe for concurrent use — two requests racing on the
/// same key must see exactly one <see cref="IdempotencyBeginStatus.Started"/>.
/// </summary>
/// <remarks>
/// A claim carries a <em>lease</em> as well as a TTL, and the two answer different questions. The TTL is how
/// long a finished response stays replayable. The lease is how long a claim may sit unfinished before
/// another request may take it over, and it exists because a process that dies mid-request never reaches
/// <see cref="CompleteAsync"/> or <see cref="AbandonAsync"/>. Without a lease, a durable store would answer
/// every retry of that key with <c>409</c> for the whole TTL — the in-memory store gets away without one
/// only because its state dies with the process that stranded it.
/// </remarks>
public interface IIdempotencyStore
{
    /// <summary>
    /// Atomically claims <paramref name="key"/> for the current request. On first sight — or when a
    /// previous claim's lease has run out — records it as in-flight and returns
    /// <see cref="IdempotencyBeginStatus.Started"/>; otherwise reports the existing entry's state.
    /// </summary>
    /// <param name="key">The fully-scoped cache key.</param>
    /// <param name="fingerprint">A hash of the request identity/payload, used to detect key reuse with a different payload.</param>
    /// <param name="ttl">How long a newly-created entry lives.</param>
    /// <param name="lease">How long this claim may stay unfinished before another request may take it over.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>The claim outcome.</returns>
    Task<IdempotencyBeginResult> BeginAsync(string key, byte[] fingerprint, TimeSpan ttl, TimeSpan lease, CancellationToken ct = default);

    /// <summary>Records the captured <paramref name="record"/> as the completed result for <paramref name="key"/>, to be replayed until the entry expires.</summary>
    /// <param name="key">The fully-scoped cache key previously claimed via <see cref="BeginAsync"/>.</param>
    /// <param name="record">The response to remember.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <remarks>
    /// A no-op unless the caller still holds the claim — see the note on <see cref="AbandonAsync"/>.
    /// </remarks>
    Task CompleteAsync(string key, IdempotencyRecord record, CancellationToken ct = default);

    /// <summary>Releases a claimed but uncompleted <paramref name="key"/> (e.g. the endpoint threw or produced a non-cacheable status) so a later request can retry.</summary>
    /// <param name="key">The fully-scoped cache key to release.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <remarks>
    /// Both this and <see cref="CompleteAsync"/> act only on a claim the calling request still holds, and do
    /// nothing otherwise. The case that makes the distinction matter is a request slow enough to lose its
    /// lease: by the time it finishes, the key belongs to whoever took it over, and acting on it anyway
    /// would let the attempt that lost the key overwrite the winner's captured response — or, on this path,
    /// delete a live claim and leave the key open for a third request to start alongside the second.
    /// <para>
    /// A store that cannot distinguish requests cannot honour this. The in-memory store is one: its claims
    /// carry no owner, so a stranded-then-resumed request does act on the new holder's entry. It is
    /// used for tests and database-less development; use the database store when lease ownership matters.
    /// </para>
    /// </remarks>
    Task AbandonAsync(string key, CancellationToken ct = default);
}
