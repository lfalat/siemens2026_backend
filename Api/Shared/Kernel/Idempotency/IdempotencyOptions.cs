namespace Kernel.Idempotency;

/// <summary>
/// Host-wide settings for the idempotency middleware. Registered via
/// <see cref="IdempotencyRegistration.AddIdempotency"/>; a per-endpoint TTL override is expressed as
/// endpoint metadata (<see cref="IdempotencyTtl"/>) rather than here.
/// </summary>
public sealed class IdempotencyOptions
{
    /// <summary>
    /// How long a completed response is remembered and replayed for a repeated key. Applied to every
    /// covered endpoint unless the endpoint carries an <see cref="IdempotencyTtl"/> override. Defaults to
    /// one hour.
    /// </summary>
    public TimeSpan DefaultTtl { get; set; } = TimeSpan.FromHours(1);

    /// <summary>The request header carrying the client-supplied idempotency key. Defaults to <c>Idempotency-Key</c>.</summary>
    public string HeaderName { get; set; } = "Idempotency-Key";

    /// <summary>
    /// Response header stamped on a replayed (cache-hit) response so clients can tell an original result
    /// from a replayed one. Defaults to <c>Idempotency-Replayed</c>.
    /// </summary>
    public string ReplayHeaderName { get; set; } = "Idempotency-Replayed";

    /// <summary>
    /// When <c>true</c> (the default), a covered request without a non-empty key header is rejected with
    /// <c>400 Bad Request</c>. Set to <c>false</c> to make idempotency opt-in per request (a missing key
    /// simply passes through). Endpoints marked with <see cref="IdempotencyDisabled"/> are exempt either way.
    /// </summary>
    public bool RequireKey { get; set; } = true;

    /// <summary>
    /// How long a claim may stay unfinished before another request may take it over. It bounds the damage
    /// when a process dies mid-request: that claim is never completed or abandoned, and without a takeover
    /// window a durable store would answer every retry of the key with <c>409 Conflict</c> until the whole
    /// <see cref="DefaultTtl"/> elapsed. Must comfortably exceed endpoint execution plus response capture
    /// and write-back — a lease that
    /// expires while the work is still running lets a duplicate through. Must exceed
    /// <see cref="WriteBackTimeout"/>; startup fails otherwise. Defaults to one minute.
    /// </summary>
    public TimeSpan ClaimLease { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Cancellation budget for recording or releasing a claim after the endpoint finishes. Independent
    /// of client disconnects so a lost response can still be recorded. Includes database retry delays;
    /// defaults to five seconds. Provider cancellation may take additional time to finish unwinding.
    /// </summary>
    public TimeSpan WriteBackTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Longest accepted <see cref="HeaderName"/> value, in characters; a longer key is rejected with
    /// <c>400 Bad Request</c>. The key is caller-controlled and ends up in a cache key, so it is bounded
    /// here rather than at the header-size limit. Defaults to 128, comfortably above a UUID or a base64
    /// 128-bit value, which is what clients actually send.
    /// </summary>
    public int MaxKeyLength { get; set; } = 128;

    /// <summary>
    /// Memory budget for remembered responses, in bytes. The in-memory store keeps its own cache sized to
    /// this, and evicts completed entries early once it is exceeded — an evicted key simply re-executes on
    /// the next request. In-flight claims are tracked separately, outside this budget, until both TTL
    /// and lease expire or the request completes/abandons them. Defaults to 64 MB.
    /// </summary>
    /// <remarks>
    /// Applies to the in-memory store only. After <see cref="IdempotencyRegistration.AddDbIdempotencyStore"/>
    /// records live in Postgres and are bounded by their TTL and the retention job instead, so this value
    /// has no effect.
    /// </remarks>
    public long CacheBudgetBytes { get; set; } = 64L * 1024 * 1024;

    /// <summary>
    /// Largest response body worth remembering, in bytes. A successful response above this is sent to the
    /// client but not cached, so a retry re-runs the endpoint rather than pinning a large buffer for the
    /// whole TTL. Defaults to 256 KB.
    /// </summary>
    public int MaxCachedResponseBytes { get; set; } = 256 * 1024;
}
